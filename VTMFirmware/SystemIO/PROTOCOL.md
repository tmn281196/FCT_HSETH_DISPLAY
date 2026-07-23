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
| 0x41 | `CMD_ACK`   | board → PC  | **transport only**: "your `CMD_OUTPUT` arrived" - echoes the requested value |
| 0x53 | `CMD_VAL` | board → PC  | the level a pin was **actually driven to**, whoever asked                    |

There is exactly **one PC→board frame** (`CMD_OUTPUT`) and **three board→PC frames**
(`CMD_INPUT`, `CMD_ACK`, `CMD_VAL`).

The two board→PC output frames answer two different questions and must not be conflated:

| | `CMD_ACK` | `CMD_VAL` |
|---|---|---|
| answers | "did my frame get there?" | "what is the pin actually doing?" |
| value carried | what you **asked for** | what the pin was **driven to** |
| sent from | `handleOutput()` only, one per `CMD_OUTPUT` | `writeOutput()`, on **every** pin write |
| PC uses it to | settle the retry (`_pendingOut`) | update the output cache (`_boardOutputVal`) |

So a `CLUP:1` the interlock refuses produces **both** frames:

```
OUT REQ    CUP=H     we asked for it
OUT ACK    CUP=H     your frame arrived (echo of the request)
OUT VAL    CUP=L     ...but the pin stayed low
```

The PC caches `L`, and its delta re-sends `CLUP:1` on the next `SendControl()` once the interlock lets go.
Caching the ACK's value instead is what once made a PASS never raise the cylinder.

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
  `DebounceChanged`, ~100 ms settle) **once**; a lost frame is recovered by the polling re-send.
- **Polling flag:** `POLLING_ENABLED` (default 1) re-sends **SS_DOWN only** (the
  **debounced** state - never a raw read) every `POLLING_INTERVAL_MS` (default 1000 ms) as a heartbeat
  / re-sync / connect-liveness signal. Set to 0 for pure on-change. The buttons are momentary, so a
  poll cannot recover a press that was already released - their on-change send covers them fully.
- **Output write:** `SendControl()` sends only the outputs that **changed** (delta), one frame per output.
  The delta compares against `_boardOutputVal`, which is fed **only** by `CMD_VAL` - never by what we sent
  and never by the ACK. The board applies each write (with the SDOWN interlock on CLUP/AC) and reports the
  pin's real level as `CMD_VAL`, so a write the interlock swallowed is visible to the PC and is re-sent by
  the next `SendControl()` once the interlock lets go.
- **ACK-retry:** the PC tracks each output write until its `CMD_ACK` arrives; otherwise it re-sends
  (`AckTimeoutMs` 150 ms, `MaxOutTries` 10) then logs `REQUEST TIMEOUT`. Since the ACK is a pure transport
  receipt it arrives even when the interlock refused the value, so a timeout now means one thing only: the
  board never received the frame - a lost frame or a dead link. Retries are driven by incoming frames, so
  the real cadence is bounded by `POLLING_INTERVAL_MS` (1000 ms), **not** by `AckTimeoutMs`.
- **Every pin write reports itself.** `writeOutput(key, pin, level)` is the only place an output pin is driven;
  it does the `digitalWrite()` and sends the `CMD_VAL` frame together, so the report cannot be forgotten.
  `SDOWNHandler()` drives outputs on both edges of the debounced `SS_DOWN`, before the `CMD_INPUT` frame for
  `SS_DOWN` itself:

  | edge        | board drives                               | logs                                        |
  |-------------|--------------------------------------------|---------------------------------------------|
  | `SS_DOWN` ↑ | `LPR`=0, `LPY`=1, `LPG`=0 (lamps)          | `OUT VAL LPR=L` / `LPY=H` / `LPG=L`         |
  | `SS_DOWN` ↓ | `CLUP`=0, `AC110`=0, `AC220`=0 (interlock) | `OUT VAL CUP=L` / `110=L` / `220=L`         |

  No `CMD_ACK` on either edge - nobody asked, so there is nothing to acknowledge.

  **Rule for new code:** never call `digitalWrite()` on an output pin directly; go through `writeOutput()`.
  A silent pin write leaves the PC's cache stale, and its delta then skips the next genuine write to that pin -
  a PASS that never raises the cylinder, or a green lamp that never re-lights.
- **Connect check:** the PC sends a one-signal probe (CLUP), treats the board's ACK / any input
  frame as "Connected", then re-pushes every output once to sync the board.

## Examples

```
SS_DOWN = 1 (board → PC) :  02 49 01 01 4B 03    IN  VAL    SDW=H
LPY on      (PC → board) :  02 4F 05 01 49 03    OUT REQ    LPY=H
  ack       (board → PC) :  02 41 05 01 47 03    OUT ACK    LPY=H
  value     (board → PC) :  02 53 05 01 55 03    OUT VAL    LPY=H
```

Log columns: a 3-char direction, the frame type (`REQ` / `ACK` / `VAL`), then `SIGNAL=H/L`. Signal names are
3 chars each (`CUP`, `SDW`, `BUZ`, `110`, `220`, ...) so the `=` lines up down the whole log.

## Deploy

**Both sides must be flashed/deployed together** on all machines (FCT41/42/31/32). The v1
(bitmask) and v2 (key-value) firmware/app are **not** interoperable.

- Firmware: `Firmware/sys_io/` (`io_pin.h`, `sys_io.ino`).
- PC: `SystemComunication` (framing/keys), `SystemBoard` (send/receive/annotate),
  `SystemMachineIO` (`OutputKV` / `ApplyInput`).
