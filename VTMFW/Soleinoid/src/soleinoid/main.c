#include <avr/io.h>
#include <util/delay.h>
#include <stdint.h>
#include <avr/wdt.h>           // ?? dùng watchdog

#define F_CPU 11059200UL
#define BAUD 9600
#define MYUBRR ((F_CPU / (16UL * BAUD)) - 1)

// Pin definitions
#define DATA_PORT PORTA
#define DATA_DDR DDRA
#define DATA_PIN PINA
#define CS1 PC7
#define CS2 PC6
#define CS3 PC5
#define OUT_EN PC4

// UART Frame structure
#define HEADER_BYTE1 0x44
#define HEADER_BYTE2 0x45
#define FOOTER_BYTE 0x56
#define FRAME_LENGTH 10
#define ACK_FRAME_LENGTH 6

// Command definitions
#define OUTPUT_CMD 0x53

// Response codes
#define ACK_SUCCESS     0x01
#define ACK_CRC_ERROR   0x02
#define ACK_FORMAT_ERR  0x03
#define ACK_TIMEOUT     0x04   // Thêm mã m?i cho timeout

// Global variables
uint8_t uart_buffer[FRAME_LENGTH];
uint8_t uart_index = 0;
uint8_t frame_ready = 0;
uint8_t output_values[3] = {0}; // Current values for 24 outputs

// Timeout counter (??n v? ~10ms m?i vòng l?p)
#define RECEIVE_TIMEOUT_MS  5000   // 5 giây
volatile uint16_t receive_timeout_counter = 0;
uint8_t receiving = 0;

// CRC8 XOR
uint8_t xor8_checksum(const uint8_t* data, uint16_t len) {
	uint8_t checksum = 0;
	for (uint16_t i = 0; i < len; i++) {
		checksum ^= data[i];
	}
	return checksum;
}

void clock_aware_delay_us(uint16_t us) {
	while (us--) {
		_delay_us(1);
	}
}

void init_ports(void) {
	// Disable JTAG n?u c?n
	MCUCSR = (1 << JTD);
	MCUCSR = (1 << JTD);

	DATA_DDR = 0xFF;
	DATA_PORT = 0x00;

	DDRC |= (1 << CS1) | (1 << CS2) | (1 << CS3) | (1 << OUT_EN);
	// T?t c? CS = 1 (disable), OUT_EN = 0 (enable output - active low)
	PORTC |= (1 << CS1) | (1 << CS2) | (1 << CS3);
	PORTC &= ~(1 << OUT_EN);
}

void init_uart(void) {
	uint16_t ubrr = MYUBRR;
	UBRR0H = (uint8_t)(ubrr >> 8);
	UBRR0L = (uint8_t)ubrr;

	UCSR0B = (1 << RXEN0) | (1 << TXEN0);
	UCSR0C = (1 << URSEL0) | (1 << UCSZ01) | (1 << UCSZ00);
}

void write_to_ic(uint8_t ic_num, uint8_t data) {
	DATA_PORT = data;

	switch(ic_num) {
		case 1:
		PORTC &= ~(1 << CS1);
		clock_aware_delay_us(1);
		PORTC |= (1 << CS1);
		break;
		case 2:
		PORTC &= ~(1 << CS2);
		clock_aware_delay_us(1);
		PORTC |= (1 << CS2);
		break;
		case 3:
		PORTC &= ~(1 << CS3);
		clock_aware_delay_us(1);
		PORTC |= (1 << CS3);
		break;
	}
}

void update_outputs(void) {
	write_to_ic(1, output_values[0]);
	write_to_ic(2, output_values[1]);
	write_to_ic(3, output_values[2]);
}

uint8_t validate_frame(const uint8_t *frame) {
	if (frame[0] != HEADER_BYTE1 || frame[1] != HEADER_BYTE2 ||
	frame[FRAME_LENGTH-1] != FOOTER_BYTE) {
		return ACK_FORMAT_ERR;
	}

	if (frame[2] != 0x06) {
		return ACK_FORMAT_ERR;
	}

	if (frame[3] != OUTPUT_CMD) {
		return ACK_FORMAT_ERR;
	}

	uint8_t crc = xor8_checksum(frame, FRAME_LENGTH - 2);
	if (crc != frame[FRAME_LENGTH - 2]) {
		return ACK_CRC_ERROR;
	}

	return ACK_SUCCESS;
}

void send_response(uint8_t status) {
	uint8_t response[ACK_FRAME_LENGTH] = {
		HEADER_BYTE1,
		HEADER_BYTE2,
		0x02,           // length
		status,
		0,              // CRC s? tính sau
		FOOTER_BYTE
	};

	response[4] = xor8_checksum(response, 4);

	for (uint8_t i = 0; i < ACK_FRAME_LENGTH; i++) {
		while (!(UCSR0A & (1 << UDRE0)));
		UDR0 = response[i];
	}
}

void process_frame(const uint8_t *frame) {
	output_values[0] = frame[4];
	output_values[1] = frame[5];
	output_values[2] = frame[6];

	update_outputs();
}

void receive_frame(void) {
	// N?u có byte m?i
	if (UCSR0A & (1 << RXC0)) {
		uint8_t data = UDR0;
		receive_timeout_counter = 0;           // Reset timeout

		if (!receiving) {
			// Ch? header
			if (uart_index == 0 && data == HEADER_BYTE1) {
				uart_buffer[uart_index++] = data;
			}
			else if (uart_index == 1 && data == HEADER_BYTE2) {
				uart_buffer[uart_index++] = data;
				receiving = 1;
			}
			else {
				uart_index = 0;   // Reset n?u không kh?p header
			}
		}
		else {
			// ?ang nh?n frame
			if (uart_index < FRAME_LENGTH) {
				uart_buffer[uart_index++] = data;
			}

			if (uart_index >= FRAME_LENGTH) {
				receiving = 0;
				frame_ready = 1;
				uart_index = 0;
			}
		}
	}
	// Không có byte m?i ? ki?m tra timeout
	else if (receiving) {
		if (receive_timeout_counter < RECEIVE_TIMEOUT_MS / 10) {
			receive_timeout_counter++;
			_delay_ms(10);   // ~10ms m?i l?n check
		}
		else {
			// Timeout ? reset tr?ng thái nh?n
			receiving = 0;
			uart_index = 0;
			receive_timeout_counter = 0;
			// Tùy ch?n: g?i thông báo l?i
			// send_response(ACK_TIMEOUT);
		}
	}
}

int main(void) {
	// B?t watchdog 2 giây (r?t quan tr?ng ?? tránh treo v?nh vi?n)
	wdt_enable(WDTO_2S);

	init_ports();
	init_uart();

	// Giá tr? m?c ??nh
	output_values[0] = 0x00;
	output_values[1] = 0x00;
	output_values[2] = 0x00;
	update_outputs();

	while (1) {
		wdt_reset();           // Kick watchdog

		receive_frame();

		if (frame_ready) {
			uint8_t validation = validate_frame(uart_buffer);
			send_response(validation);

			if (validation == ACK_SUCCESS) {
				process_frame(uart_buffer);
			}

			frame_ready = 0;
		}

		// Có th? thêm các tác v? khác ? ?ây n?u c?n
	}

	return 0;
}