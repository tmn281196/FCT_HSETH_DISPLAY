
#define SDOWN 2         // sensor bottom level aka start signal
#define AC220 3         // A site 220V power
#define AC110 11        // A site 110V power
#define START_MANUAL 9  // Button for manual only
#define STOP_ALL 10     // Button for both manual and auto
#define CLUP 4          // Cilynder Up control aka reset
#define LPY 6           // Tower lamp yellow
#define LPG 5           // Tower lamp green
#define LPR 7           // Tower lamp red
#define BZ 8            // Tower lamp buzzer

// ================= System-board protocol (v2) - MUST match SystemComunication.cs on the PC =================
// Fixed 6-byte frame, ONE signal each:  [STX][OPCODE][KEY][VALUE][CRC][ETX]
//   STX = 0x02, ETX = 0x03
//   CRC = XOR of STX ^ OPCODE ^ KEY ^ VALUE
// Bad CRC / bounds -> PC drops the frame and logs (CRC NG).
#define STX 0x02
#define ETX 0x03
#define CMD_INPUT 0x49   // board -> PC : one input state, on change (+ optional periodic re-send)
#define CMD_OUTPUT 0x4F  // PC -> board : one output to write
#define CMD_ACK 0x41     // board -> PC : reply to a CMD_OUTPUT - ONLY ever sent from handleOutput()
#define CMD_VAL 0x53     // board -> PC : the level a pin was actually driven to - sent by writeOutput()

// Input keys (board -> PC). This board only reports SS_DOWN + the two buttons; the rest are reserved on the PC.
#define KEY_SS_DOWN 0x01
#define KEY_BTN_START 0x03
#define KEY_BTN_STOP 0x04
// Output keys (PC -> board).
#define KEY_CLUP 0x01  // MainUP / cylinder up (reset)
#define KEY_AC110 0x02
#define KEY_AC220 0x03
#define KEY_LPR 0x04
#define KEY_LPY 0x05
#define KEY_LPG 0x06
#define KEY_BZ 0x07

// Optional periodic re-send ("polling flag"): re-send every input (one frame each) every POLLING_INTERVAL_MS
// as a heartbeat / re-sync / connect-liveness signal, on top of the on-change sends. 0 = pure on-change.
#define POLLING_ENABLED 1
#define POLLING_INTERVAL_MS 1000

void SetSystemIOPinMode() {
  Serial.begin(9600);
  pinMode(SDOWN, INPUT_PULLUP);
  pinMode(START_MANUAL, INPUT_PULLUP);
  pinMode(STOP_ALL, INPUT_PULLUP);

  pinMode(LPG, OUTPUT);
  pinMode(LPY, OUTPUT);
  pinMode(LPR, OUTPUT);
  pinMode(BZ, OUTPUT);

  pinMode(AC110, OUTPUT);
  pinMode(AC220, OUTPUT);
  pinMode(CLUP, OUTPUT);
}

// ============================================================
// TIME-BASED INPUT GLITCH FILTER (unchanged DebounceChanged)
// Each input is sampled at most once per INPUT_SAMPLE_INTERVAL_MS and a change is accepted only after
// INPUT_STABLE_COUNT consecutive stable samples -> real settle time ~= 100 ms, independent of loop() speed.
// Non-blocking (millis(), no delay()).
// ============================================================
#define INPUT_SAMPLE_INTERVAL_MS 1  // take at most one sample per input per this many ms
#define INPUT_STABLE_COUNT 100      // consecutive stable samples required to accept a change (~100 ms)

struct DebouncedInput {
  uint8_t pin;
  uint8_t lastRaw;             // most recent raw read (0xFF forces a reset on the first call)
  uint16_t stableCount;        // consecutive stable samples with unchanged raw
  uint8_t state;               // accepted (debounced) state
  unsigned long lastSampleMs;  // millis() of the last time-gated sample
};

// Returns true once, on the sample where the accepted state changes; the new state is in di->state.
bool DebounceChanged(struct DebouncedInput* di) {
  unsigned long now = millis();
  if ((unsigned long)(now - di->lastSampleMs) < INPUT_SAMPLE_INTERVAL_MS) return false;  // not time to sample yet
  di->lastSampleMs = now;

  uint8_t raw = digitalRead(di->pin);
  if (raw != di->lastRaw) {  // raw just changed -> restart the stable counter, not accepted yet
    di->lastRaw = raw;
    di->stableCount = 0;
    return false;
  }
  if (di->stableCount < INPUT_STABLE_COUNT) di->stableCount++;
  if (di->stableCount >= INPUT_STABLE_COUNT && raw != di->state) {
    di->state = raw;  // stable long enough -> accept the change
    return true;
  }
  return false;
}

struct DebouncedInput inStartManual = { START_MANUAL, 0xFF, 0, 0, 0 };
struct DebouncedInput inStopAll = { STOP_ALL, 0xFF, 0, 0, 0 };
struct DebouncedInput inSDOWN = { SDOWN, 0xFF, 0, 0, 0 };

// ---- Fixed 6-byte frame TX: [STX OPCODE KEY VALUE CRC ETX] ----
void sendFrame(uint8_t opcode, uint8_t key, uint8_t val) {
  uint8_t crc = (uint8_t)(STX ^ opcode ^ key ^ val);
  uint8_t f[6] = { STX, opcode, key, val, crc, ETX };
  Serial.write(f, 6);
}

void sendInput(uint8_t key, uint8_t val) {
  sendFrame(CMD_INPUT, key, val);  // send once; a lost frame is recovered by the polling re-send
}

// ---- The ONLY way an output pin is ever driven ----
// Writes the pin and immediately reports the level it was actually driven to. Every output write in this
// firmware goes through here - PC-requested or interlock - so the PC's cached view can never go stale and
// nobody has to remember to report by hand. Never call digitalWrite() on an output pin directly.
void writeOutput(uint8_t key, uint8_t pin, uint8_t level) {
  digitalWrite(pin, level);
  sendFrame(CMD_VAL, key, level ? 1 : 0);
}

// ---- Input handlers (send only the input that changed) ----
void StartManualHandler() {
  if (DebounceChanged(&inStartManual)) sendInput(KEY_BTN_START, inStartManual.state ? 1 : 0);
}

void StopAllHandler() {
  if (DebounceChanged(&inStopAll)) sendInput(KEY_BTN_STOP, inStopAll.state ? 1 : 0);
}

void SDOWNHandler() {
  if (DebounceChanged(&inSDOWN)) {
    uint8_t s = inSDOWN.state;
    // Local interlock on the SDOWN edge (unchanged behaviour).
    // Both branches drive outputs on our own initiative, without the PC asking. writeOutput() reports each
    // one as CMD_VAL, so the PC's delta stays correct: without that a still-wanted LPG:1 / CLUP:1 would
    // look "unchanged" against a stale cached 1 and never be re-sent - dark green lamp, or a PASS that
    // never raises the cylinder. No CMD_ACK here: nobody asked, so there is nothing to acknowledge.
    if (s == HIGH) {
      writeOutput(KEY_LPR, LPR, LOW);
      writeOutput(KEY_LPY, LPY, HIGH);
      writeOutput(KEY_LPG, LPG, LOW);
    } else {
      writeOutput(KEY_AC110, AC110, LOW);
      writeOutput(KEY_AC220, AC220, LOW);
      writeOutput(KEY_CLUP,  CLUP,  LOW);
    }
    sendInput(KEY_SS_DOWN, s ? 1 : 0);
  }
}

// ---- Optional periodic re-send of every input, one frame each (polling flag) ----
// Sends the DEBOUNCED (glitch-filtered) state of each input - never a raw digitalRead - so a poll can never
// report a value mid-glitch. The inXxx.state fields are updated by DebounceChanged, which runs every loop via
// the handlers in CollectInput() just before this.
unsigned long lastPollMs = 0;
void PollingResend() {
#if POLLING_ENABLED
  unsigned long now = millis();
  if ((unsigned long)(now - lastPollMs) < POLLING_INTERVAL_MS) return;
  lastPollMs = now;
  // Only SDOWN is re-sent. It is a SUSTAINED signal, so a periodic re-send genuinely re-syncs it - and because
  // the poll frames are then all identical, the PC log collapses them into one "xN" line instead of scrolling.
  // The buttons are momentary events: a poll can't recover a press that was already released, so the on-change
  // send fully covers them and re-sending them here would only break up the log.
  sendFrame(CMD_INPUT, KEY_SS_DOWN, inSDOWN.state ? 1 : 0);
#endif
}

void CollectInput() {
  StartManualHandler();
  StopAllHandler();
  SDOWNHandler();
  PollingResend();
}

// ---- Output write (from PC): apply one output ----
// What actually reaches the pin is not always what was asked for: the SDOWN interlock forces CLUP/AC to LOW.
// writeOutput() reports the real level, so the refusal is visible to the PC without the ACK carrying it.
void applyOutput(uint8_t key, uint8_t val) {
  uint8_t on = val ? HIGH : LOW;
  switch (key) {
    // Interlock: CLUP / AC only while the filtered SDOWN (inSDOWN.state) is HIGH; otherwise forced LOW.
    case KEY_CLUP:  writeOutput(KEY_CLUP,  CLUP,  inSDOWN.state ? on : LOW); break;
    case KEY_AC110: writeOutput(KEY_AC110, AC110, inSDOWN.state ? on : LOW); break;
    case KEY_AC220: writeOutput(KEY_AC220, AC220, inSDOWN.state ? on : LOW); break;
    case KEY_LPR:   writeOutput(KEY_LPR, LPR, on); break;
    case KEY_LPY:   writeOutput(KEY_LPY, LPY, on); break;
    case KEY_LPG:   writeOutput(KEY_LPG, LPG, on); break;
    case KEY_BZ:    writeOutput(KEY_BZ,  BZ,  on); break;
    default: break;   // unknown key - nothing driven, nothing to report
  }
}

// ACK means only "your CMD_OUTPUT frame arrived here", so it echoes the value you asked for - it says nothing
// about what the pin did. The pin's real level is reported separately by writeOutput() as CMD_VAL, which is
// what the PC caches. Keeping the two apart is what lets the PC tell "frame lost" from "interlock refused".
void handleOutput(uint8_t key, uint8_t val) {
  applyOutput(key, val);
  sendFrame(CMD_ACK, key, val);
}
