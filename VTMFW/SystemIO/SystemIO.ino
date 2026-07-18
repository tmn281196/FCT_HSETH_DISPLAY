
#include "io_pin.h"

void setup() {
  SetSystemIOPinMode();
}

void loop() {
  // Sample inputs (debounced), send on change, and periodically re-send (polling flag).
  CollectInput();
}

// Parse incoming CMD_OUTPUT frames: fixed 6 bytes [STX OPCODE KEY VALUE CRC ETX].
// Reads one contiguous burst (bounded inter-byte wait), scans it, applies + ACKs each valid output frame.
void serialEvent() {
  uint8_t buf[64];
  int len = 0;
  while (len < (int)sizeof(buf)) {
    if (Serial.available()) {
      buf[len++] = Serial.read();
    } else {
      delayMicroseconds(1500);          // wait ~1.4 byte-times (9600 baud) for the next byte
      if (!Serial.available()) break;   // inter-byte gap -> end of burst
    }
  }
  if (len < 6) return;

  for (int i = 0; i + 5 < len; i++) {
    if (buf[i] != STX) continue;
    int end = i + 5;                     // ETX index
    if (buf[end] != ETX) continue;
    uint8_t crc = (uint8_t)(buf[i] ^ buf[i + 1] ^ buf[i + 2] ^ buf[i + 3]);
    if (crc != buf[i + 4]) continue;     // bad CRC -> ignore
    if (buf[i + 1] == CMD_OUTPUT) handleOutput(buf[i + 2], buf[i + 3]);
    i = end;                             // continue scanning after this frame
  }
}
