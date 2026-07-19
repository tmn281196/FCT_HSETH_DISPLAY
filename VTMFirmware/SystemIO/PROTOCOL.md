# System Board Serial Protocol v2 (key-value)

9600 baud, 8N1. Replaces the old fixed 32-bit bitmask protocol for the **system board only**
(solenoid/relay/etc. still use their own frames).

## Frame envelope

Fixed **6 bytes, one signal per frame**:

```
STX | OPCODE | KEY | VALUE | CRC | ETX
```

| field  | value       | meaning                             |
|--------|-------------|-------------------------------------|
| STX    | 0x02        | start of frame                      |
| OPCODE | see below   | frame type                          |
| KEY    | see keys    | which signal                        |
| VALUE  | 0x00 / 0x01 | 0 = OFF, non-zero = ON              |
| CRC    | XOR         | `STX ^ OPCODE ^ KEY ^ VALUE`        |
| ETX    | 0x03        | end of frame                        |

A frame is trusted only if **both** the STX/ETX bounds and the CRC match. On the PC a bad frame
is dropped and shown in the Diagnostic Log as `(CRC NG)`.

## Frame types (CMD)

| CMD  | name        | direction   | meaning                                             |
|------|-------------|-------------|-----------------------------------------------------|
| 0x49 | `CMD_INPUT` | board → PC  | input state(s); sent **on change** + periodic re-send |
| 0x4F | `CMD_OUTPUT`| PC → board  | outputs to write                                    |
| 0x41 | `CMD_ACK`   | board → PC  | the output's **actual pin state** after the write    |

There is exactly **one PC→board frame** (`CMD_OUTPUT`) and **two board→PC frames**
(`CMD_INPUT`, `CMD_ACK`).

## Keys

**Inputs** (board → PC). This firmware only reports SS_DOWN + the two buttons; the rest are
reserved on the PC for when the seating/lock sensors get wired.

| key  | signal    | key  | signal |
|------|-----------|------|--------|
| 0x01 | SS_DOWN   | 0x10 | SS_BF  |
| 0x02 | SS_UP     | 0x11 | SS_TF  |
| 0x03 | BTN_START | 0x12 | SS_BL  |
| 0x04 | BTN_STOP  | 0x13 | SS_TL  |
| 0x05 | SW_EMC    | 0x14 | SS_BR  |
| 0x06 | DOOR      | 0x15 | SS_TR  |

**Outputs** (PC → board) — what the system board physically drives.

| key  | signal          |
|------|-----------------|
| 0x01 | CLUP (MainUP)   |
| 0x02 | AC110           |
| 0x03 | AC220           |
| 0x04 | LPR             |
| 0x05 | LPY             |
| 0x06 | LPG             |
| 0x07 | BZ (buzzer)     |

## Behaviour

- **Inputs are addressed by key** → the PC updates only the signal actually reported. There is
  no `SW_UP`/`SW_DOWN` key, so an input frame can never drive `MainUP` (this was the phantom-bit
  hazard of the old full-bitmask `DataToIO`: `SW_DOWN=0` → `MainUP=true` → power cut).
- **On-change:** the firmware sends the changed input as its own frame (debounced by
  `DebounceChanged`, ~12 ms settle) **once**; a lost frame is recovered by the polling re-send.
- **Polling flag:** `POLLING_ENABLED` (default 1) re-sends every input (one frame each, the
  **debounced** state - never a raw read) every `POLLING_INTERVAL_MS` (default 200 ms) as a heartbeat
  / re-sync / connect-liveness signal. Set to 0 for pure on-change.
- **Output write:** `SendControl()` sends only the outputs that **changed** (delta), one frame per
  output. The board applies each (with the SDOWN interlock on CLUP/AC) and ACKs the pin's **actual**
  state — never the requested one, so a write the interlock swallowed is visible to the PC.
- **ACK-retry:** the PC tracks each output write until an ACK **matching the requested value** arrives;
  otherwise it re-sends (`AckTimeoutMs` 150 ms, `MaxOutTries` 10) then logs a warning. This covers both
  a lost frame and a write the board refused. Retries are driven by incoming frames, so the real cadence
  is bounded by `POLLING_INTERVAL_MS`.
- **Connect check:** the PC sends a one-signal probe (CLUP), treats the board's ACK / any input
  frame as "Connected", then re-pushes every output once to sync the board.

## Examples

```
SS_DOWN = 1 (board → PC) :  02 49 01 01 4B 03
LPY on   (PC → board)    :  02 4F 05 01 49 03
  ack    (board → PC)    :  02 41 05 01 47 03
```

## Deploy

**Both sides must be flashed/deployed together** on all machines (FCT41/42/31/32). The v1
(bitmask) and v2 (key-value) firmware/app are **not** interoperable.

- Firmware: `Firmware/sys_io/` (`io_pin.h`, `sys_io.ino`).
- PC: `SystemComunication` (framing/keys), `SystemBoard` (send/receive/annotate),
  `SystemMachineIO` (`OutputKV` / `ApplyInput`).
