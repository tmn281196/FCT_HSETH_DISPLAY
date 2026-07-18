# VTM (Visual Test Machine) - Haengsung Display

## Tech Stack
- C# WPF (.NET Framework)
- OpenCvSharp (camera at 1920x1080 via DirectShow)
- PaddleOCR (FND/LCD vision detection)
- Arduino Mega (solenoid board firmware, system board)
- Serial communication, 9600 baud. **TWO frame formats coexist:** system board = **v2** `[STX OPCODE KEY VALUE CRC ETX]` (see `Firmware/sys_io/PROTOCOL.md`); everything else (solenoid/relay/level/mux/DMM) = **legacy** `0x44 0x45` prefix + XOR checksum + `0x56` suffix

## Architecture
- 4-page architecture: AutoPage, ManualPage, ModelPage, VisionPage (+ SettingPage)
- Partial class pattern for `Program` class split across multiple files
- State machine: BUSY -> WAIT -> READY -> TESTTING -> GOOD/FAIL/STOP
- Serial devices: Solenoid, Relay, MuxCard, SystemBoard, DMM, UUT ports

## Folder Structure
- `VTMMain/` - Main WPF application (MainWindow, pages); assembly `VTMMain.exe`, namespace `VTMMain`
- `VTMProgram/` - Test logic, state machine, device management
  - `ModelTester/Program.cs` - Main state machine (~6700+ lines)
  - `Device/ProgramDevices.cs` - Serial port init, event handlers
  - `Boards/Program.cs` - Board setup (SetBoards)
- `Controls/` - Reusable controls and device drivers
  - `DeviceControl/Solenoid/` - SolenoidCard, SolenoidChannel, SolenoidControls
  - `DeviceControl/SysIO/` - SystemBoard, SystemMachineIO
  - `OtherControls/Serial Display/` - SerialPortDisplay (serial communication)
  - `SystemComunication.cs` - Frame builders: `BuildFrame`/`FrameOk` + opcodes/keys (system v2); `GetFrame` (legacy, other devices)
- `Utility/Debug.cs` - **the** Diagnostic Log utility (the old stale top-level duplicate was removed in the restructure; there is now exactly one)
- `Camera/` - CameraControl (OpenCvSharp capture loop)
- `VTMBase/` - Model/step data structures
- `Firmware/Solenoid/` - Arduino firmware for solenoid board
- `Firmware/sys_io/` - Arduino firmware for the **system board** (v2 protocol, time-based debounce, polling flag)
- `Firmware/sys_io/PROTOCOL.md` - system-board v2 protocol spec (frame layout, opcodes, key tables, behaviour)
- `VTM_Report/` - Report generation

## Key Entry Points
- `App.xaml.cs` - Startup, MAC whitelist, SplashScreen
- `MainWindow.xaml.cs` - Page navigation, model loading
- `Program.ProgramState()` - Main async state machine loop (500ms bottom delay)
- `SystemMachineIO.SS_DOWN` setter - Fires OnStartRequest/OnCancleRequest events
- `ProgramDevices.MachineIO_OnStartRequest` - Sets IsTestting=true to trigger test
- `SystemBoard.SendControl()` - the ONLY path that writes system-board outputs (delta + ACK-retry)
- `Debug.Write` / `Debug.Tx` / `Debug.Rx` - Diagnostic Log entry points (see the Diagnostic Log section)

## Build
- MSBuild: `"/c/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" VTM.sln -p:Configuration=Release`
- NuGet restore may be needed for Sdcb.PaddleInference.runtime.win64.mkl

## Deployed Machines
- 4 customer PCs (FCT41, FCT42, FCT31, FCT32) + dev PCs
- MAC address whitelist in App.xaml.cs

## Current Problems

### [P1] SS_DOWN Sensor Race Condition - Test Doesn't Start
- **Status:** APPLIED (3 layers): (1) OnCancleRequest READY-guard, (2) PC frame-integrity parser, (3) firmware time-based debounce. Firmware layer needs re-flashing to the Arduino to take effect.
- **Related Files:** `SystemMachineIO.cs` (SS_DOWN setter lines 295-317), `ProgramDevices.cs` (MachineIO_OnCancleRequest line 197-204, MachineIO_OnStartRequest line 206-217), `Program.cs` (READY state line 978-1025)
- **Description:** When cylinder presses down, sometimes the test doesn't start. SS_DOWN sensor bounce or serial data flicker causes rapid OFF->ON->OFF->ON transitions. OnStartRequest sets IsTestting=true, but OnCancleRequest immediately resets TestState=STOP and IsTestting=false before the 500ms polling loop can process the start. After STOP handler runs, state returns to READY with IsTestting=false, but SS_DOWN is already stable ON so no new transition fires OnStartRequest again. Dead state.
- **Root Cause:** OnCancleRequest fires from SS_DOWN ON->OFF transition even when TestState is READY (test hasn't actually started). The cancel kills a freshly-set IsTestting before the state machine processes it.
- **Proposed Fix:** In `MachineIO_OnCancleRequest`, add guard: only cancel when TestState is not READY:
  ```csharp
  private void MachineIO_OnCancleRequest(object sender, EventArgs e)
  {
      if (IsTestting && TestState != RunTestState.READY)
      {
          TestState = RunTestState.STOP;
          IsTestting = false;
      }
  }
  ```
- **Why this works:** Prevents SS_DOWN bounce from killing the start. Once state machine transitions to TESTTING, cancel works normally. BTN_STOP_ALL also uses this handler but can't stop a test that hasn't started (correct behavior).
- **Added:** 2026-03-05
- **Deeper fix for "SS_DOWN rơi rớt" (2026-07-15/16):** the state-machine guard above only stops a bounce from killing a *started* test; the raw signal itself was still dropping/flickering. Root causes, all now fixed — but note the **whole system board was then migrated to protocol v2**, which subsumes most of this (see Gotchas):
  - **PC parser (`SystemBoard.SerialPort_SerialDataReciver`)** validates the frame (bounds + CRC + STX/ETX) **before applying**, so noise/bit-flips can no longer flip SS_DOWN, and scans the whole buffer so every frame in a burst is applied.
  - **Firmware debounce (`sys_io/io_pin.h`)** is **time-based** (`millis()`-gated, 1 sample/ms, `INPUT_STABLE_COUNT`=12 ≈ 12 ms). The old count-based filter looped at ~µs so 10 counts filtered nothing, especially while idle waiting for a press. `DebounceChanged` is the single filter for SDOWN + both buttons.
  - **Re-sync is the FIRMWARE's job, not the PC's.** An earlier PC-side 500ms heartbeat (`ProgramState` calling `GetInput()`) was **removed**: v2 has no PC→board input-poll frame at all. Instead `POLLING_ENABLED`/`POLLING_INTERVAL_MS` in the firmware re-sends SDOWN periodically. Don't re-add a PC poll.
  - `SerialPortDisplay.SendBytes` still serializes `Port.Write` under `_txLock` (state machine + UI + retry can all send at once) — keep it.

## Diagnostic Log (AutoPage) — `Utility/Debug.cs`
Amber-CRT RichTextBox (`rtbProgramLog`, Consolas 12, bg `#0A0A0A`, read-only + Clear button). Reworked 2026-07-15/16:
- **One uniform tone.** `AmberDim = AmberBright = #E89800`, everything normal weight. Earlier bright-label/dim-value and bold variants were all rejected — keep it uniform unless told otherwise. (Consolas has **no SemiBold face** — SemiBold silently renders as Regular, which is why weight was abandoned.)
- **Every line is `Debug.Write`** → `HH:mm:ss` + content, flush-left. `Appent`/`ContentPad` (the indented continuation style) are **deleted** — detail lines group by sharing the parent's timestamp instead.
- **Format:** `AddContentRuns` splits at the first `':'` → label (uppercased) + `": " + value`. Wording: `READY` / `SCANNER A: <bc>` / `NEXT BARCODE A: <bc>` / `BEGIN` / `ENTERING STEP: N` / `END: PASS|FAIL|STOP`.
- **Serial frames:** `SYS ← VTM : [06] 02 4F 01 01 4D 03  (CRC OK)  (OUT> CLUP:1)`. Arrow = **real data-flow direction** (`←` FCT→device = TX, `→` device→FCT = RX), built from `(char)0x2190/0x2192` so the source stays pure ASCII (avoid literal arrows — encoding risk at compile time).
- **`LogTag` vs `DeviceName`** (`SerialPortDisplay`): `LogTag` is the SHORT tag used **only in the log** (`SYS`, `SOL`); `DeviceName` stays full (`SYSTEM`, `SOLENOID`) because `lbPortName` binds it in the settings UI. `LogName` = LogTag ?? DeviceName ?? PortName. **`Debug.FrameAnnotator` matches the LogTag** (`dev == "SYS"` / `"SOL"`) — rename one, rename the other.
- **Repeat coalescing:** `Debug.Emit` collapses a line identical to the previous one into `xN` on that same line and refreshes its timestamp, instead of adding a block. This is what keeps firmware polling from scrolling the log. Receiver-side dedupe was removed in favour of it — every frame is logged and `Emit` decides. `ClearLog` must reset the coalescing state.
- **Daily file:** `%USERPROFILE%\FCTDebugLog\FCTDebug_yyyy-MM-dd.txt`, auto-created, rolls per day, never throws. `AppendToFile` **skips consecutive duplicates** (same key) so polling can't bloat it — the file therefore has no `xN`, just one line per distinct change.
- **No connection logging.** `Connected` / `Fail Connection` were removed from `SerialPortDisplay` (both check methods) and the scanner — connection state lives on the status-bar dots only.
- Only SYS + SOL opt into TX/RX logging (`LogTxRx = true` in `ProgramDevices.CheckComnunication`).

## Boards / Barcode
- **The machine uses ONE board (site A).** Other boards will be removed later — don't invest in multi-site behaviour.
- **`BarcodeNext` = scan-ahead:** scanning during a test stores the next board's barcode; the next run auto-loads it. The auto-load at the GOOD/FAIL ends was hardcoded to `Boards[0]` while the loop above cleared `Barcode` on **all** boards → other sites' scan-ahead was stranded. Now loops all non-skipped boards and only goes READY when every one holds a valid barcode (`BarcodeReady` = `Barcode.Length > 5`).
- `BarcodeReader_SerialDataReciver` has dead/redundant code (untouched): `if (!IsTestting)` nested inside `if (IsTestting)` whose `else` branch is **identical**, plus `IsDuplicatedBarcodeHandler` checks that are always true/false right after it's assigned. Behaviour is correct; ~35 lines are a dead copy.

## Model serialization (`.vmdl`) — System.Text.Json, NOT Newtonsoft
- **Write:** `Extensions.ConvertToJson` (STJ **5.0.2**) → UTF-7 bytes → Base64. **Read:** `AutoPage.LoadModel` does its own `ReadAllText` → Decoder(UTF7) → `ConvertFromJson<Model>`. `Extensions.OpenFromFile` is **NOT** in the `.vmdl` path (it serves `Config.cfg` + printer configs).
- ⚠️ **`[JsonIgnore]` must be `System.Text.Json.Serialization`'s.** `Step.cs` had `using Newtonsoft.Json;`, so all 11 bare `[JsonIgnore]` were the **Newtonsoft** attribute — which STJ does not understand — and every runtime-only member was written into every model file (`SetCMD`, `Condition1Tooltip`, `Min_Max`, `ValueGet1-4`, `Result1-4`). Fixed 2026-07-17 by swapping the `using`. Same trap already documented in `SoundStepConfig.cs`.
- ⚠️ **`[JsonIgnore]` on a PRIVATE BACKING FIELD is a no-op.** STJ only looks at public members. `Step.cs:260` has it on `_CommandDescriptions`, not on the public `CommandDescriptions` property — so **`CommandDescriptions` IS still serialized and deserialized** (~1,055 bytes/step). Consequence, measured on a real model: each step persists a **frozen copy of the command table**, and that stale copy **overrides the canonical `Command.Commands` on load** — 2 of 5 steps in the production `ok.vmdl` load with `CD.Condition2='not use'` while canonical says `'ROIs'`. Moving the attribute to the property fixes it (verified: load-bearing fields stay identical, -10,948 bytes) but changes what the UI shows for existing models — do it deliberately, on its own.
- **Model size is `FNDsBoard0`, nothing else.** Measured per-step (48,715 bytes): `FNDsBoard0` **44,444 (91.2%)**, `RectFNDsBoard0` 2,021 (4.1%), `CommandDescriptions` 1,055 (2.2%), all 11 JsonIgnore'd keys **27 (0.06%)**. Fixing the attributes saved 1,316 bytes of 2.2 MB. If the goal is a smaller `.vmdl`, the only lever is not writing 7 full FND objects into every step — see the section on steps carrying every ROI family.
- ⚠️ **A plain load+save silently mutates customer models** (pre-existing, reproduces without any of the 2026-07-17 changes): 21 `"Visibility": 2` (Collapsed) become `1` (Hidden) on the vision models nested in `FNDsBoard0`/`LedList`, and `DelayBeforeStart`/`DischargeTime` reorder. Opening and saving a model is therefore not a no-op.
- **`VTMMain.exe` does NOT run STJ 5.0.2 despite the csproj.** `VTMBase.csproj` compiles against 5.0.0.2, but `VTMMain.exe.config` has `bindingRedirect oldVersion="0.0.0.0-8.0.0.3" newVersion="8.0.0.3"` and the DLL shipped in `bin/Release` is **8.0.0.3**. Behaviour was verified identical on both.
- **Newtonsoft is used in exactly ONE place:** `SoundPage.xaml.cs:804/911`, `JsonConvert.Serialize/DeserializeObject<SoundStepConfig>` for the step *revert snapshot*. So `SoundStepConfig` needs **BOTH** attributes; `Step` needs only STJ's (it is never Newtonsoft-serialized).
- **Collections REPLACE, never append.** A ctor that pre-populates a list (e.g. `Step()` → `InitialFND()` adds 7) has its work discarded — STJ assigns a fresh list through the setter. Proof in-repo: `Model()` pre-adds a Step, yet a 5-step file loads as 5 steps.
- **A getter-only collection is silently SKIPPED** by STJ 5.0.2 (verified empirically). `LedList` is safe — it *does* have a setter (`Step.cs:81-88`). `JsonObjectCreationHandling.Populate` only exists in .NET 8+.
- Files contain **case-colliding duplicate keys** (`cmd`/`CMD`, `testContent`/`TestContent`) that only work because STJ is **case-sensitive by default**. Never enable `PropertyNameCaseInsensitive` — it would break every deployed model.

## Vision ROI geometry — `Translate()` is the ONLY correct way to move an ROI
- **`X`/`Y` are a CACHE of the `rect` field, not the truth.** The `SingleLED(index, startLocation, …)` / `SingleGLED(index, startLocation)` ctors used to assign `rect` only, leaving `_x`/`_y` at **0** while `rect.X` held the real coordinate. So `led.X += dx` read 0 and teleported the ROI to `dx`. Ctors now sync `_x`/`_y` from `rect` (fixed 2026-07-17) — but **never read X/Y to compute a move anyway**.
- **The `Rect` PROPERTY bounds-guards and truncates.** `if (value.X > 0 && value.X < ParentCanvasSize.Width - value.Width)` — a move near an edge is **silently dropped**; `SingleGLED.Rect` also casts to `int`. Driving a bulk move through it makes ROIs shift by *different* amounts and tear apart.
- ⇒ `Translate(dx, dy)` on `SingleLED` / `SingleGLED` / `LCD` / `FND` offsets `rect` directly, syncs the cache **from** it, calls `SetPosition()`. Bypasses both traps. `FND.Translate` moves the **box only** — `PointSegments` are independent absolute coords, the caller must move them too.
- ⚠️ **That bounds guard was also the only crash protection.** `LCD.TestImage` is **`async void`** (`LCD.cs:1048`), so an OpenCV crop on an out-of-frame rect throws on a ThreadPool thread nobody can catch → **app dies**. And at test time `UpdateLcdRoi`/`UpdateFndRoi` assign through the guarded setter, which **silently rejects** an out-of-range value, leaving the *previous* step's ROI → wrong pass/fail. So `NudgeRois` checks bounds **up front and refuses the whole click** (`NudgeFits`) rather than clamping per-ROI.

## Vision steps carry EVERY ROI family (not a bug — a missing concept)
- `Step` has no notion of "which family do I own". `cmd` is consulted only at Save and at test time; the two places that *create* data ignore it:
  - `Step()` ctor **always** calls `InitialFND()` → 7 FND chars + 7 rects + `Use=false` (placeholder default — a fresh/unassigned step shows no active FND boxes; the user enables digit slots when configuring an FND step), even on a `DMM`/`DLY` step.
  - `VisionStepsGrid_SelectionChanged`'s live→step writeback is guarded only by `if (buf != null)` — it copies LedList + LCDRoi + FND segments into the step being left **regardless of cmd**. (`btSaveModel_Click` *is* cmd-guarded — that asymmetry is the whole story.)
- Confirmed on disk: a real model with steps `SND, DLY, LED, SND, SND` — **no FND step, no LCD step** — still carries `FNDsBoard0=7`, `RectFNDsBoard0=7`, all 4 `LCDRoiValue`.
- **Harmless at test time** (every read path is cmd-guarded), but: changing an existing step's `cmd` resurrects stale ROIs from the new family. This is why `NudgeStepStore` moves *all* families — leaving the unused ones behind would make that landmine worse.
- **If you ever scope this to cmd:** the crash is NOT the ctor — it is `VisionPage.xaml.cs:862/871/918` indexing `buf.FNDsBoard0[index_char]` with **no cmd guard and no count check**, looping a fixed 7 times. Making `InitialFND()` conditional *alone* → new non-FND step has `Count==0` → clicking another grid row throws. Also `cmd` is **unset at construction**, so the check cannot live in the ctor; it must move to the `cmd` setter. Ctor + load + writeback must change **together**. Old models are safe on load either way (STJ restores the 7 through the setter).

## Gotchas
- ⚠️ **`Sdcb.PaddleInference.runtime.win64.mkl.props` in `VTMMain.csproj` is NATIVE-runtime deployment, NOT a removable reference.** It has no `<Reference>` (VTMMain uses OCR only through `Camera.VisionTest.OCR`, no Paddle *managed* types), so a "remove unused references" sweep looks like it's dead — but it copies the native engine (`mkldnn.dll`, `mklml.dll`, `libiomp5md.dll`, `paddle_inference_c.dll`, …) into **`bin/Release/dll/x64/`** (via `<Link>dll\x64\…`), and native `.props` copies do **NOT** flow transitively from Camera. Strip it and OCR dies at inference with `InvalidArgumentError: Filter tensor's layout should be ONEDNN, but got NCHW` (mkldnn missing/broken). Keep this Import (+ its `<Error>` guard) in the **app** project. Version pinned `3.0.0.51` (matches Camera + packages.config).
- ⚠️⚠️ **The native `Sdcb.PaddleInference.runtime.win64.mkl` version MUST match the managed `Sdcb.PaddleInference` (managed **3.0.1** ⇄ native **3.0.0.51**). A mismatch throws `InvalidArgumentError: Filter tensor's layout should be ONEDNN, but got NCHW` at inference — the EXACT same symptom as a threading/shape bug, so it sends you down the wrong rabbit hole (it did — for many rounds).** ALL projects must reference the same runtime version. The trap: `Utility.csproj` carried a stray SDK-style `<PackageReference Include="Sdcb.PaddleInference.runtime.win64.mkl"><Version>3.1.0.54</Version>` (paddle **3.1.0**) — invisible to a `runtime.win64.mkl.<ver>\build` path grep — and since VTMMain → Utility, that native flowed **transitively** into VTMMain's output and overwrote the correct 3.0.0.51. **Diagnose by the DEPLOYED file size:** `bin/**/dll/x64/paddle_inference_c.dll` is **120,820,224** bytes for 3.0.0.51, **126,376,448** for 3.1.0.54. If OCR throws ONEDNN, check this FIRST, before threading.
- ⚠️ **The `InvalidArgumentError: Filter tensor's layout should be ONEDNN, but got NCHW` (`SEHException 0x80004005`, `Sdcb.PaddleOCR.dll`) is a native/managed Sdcb VERSION MISMATCH — NOT a threading or MKL-DNN bug. CONFIRMED FIXED 2026-07-18** by matching managed `Sdcb.PaddleInference` 3.0.1 ⇄ native `runtime.win64.mkl` 3.0.0.51 (see the `runtime.win64.mkl` version gotcha above; diagnose by the deployed `dll/x64/paddle_inference_c.dll` size = 120,820,224). `OCR._ocr` is a **per-page instance** (NOT `[ThreadStatic]`, NOT shared) built with `PaddleDevice.Mkldnn()` and works fine; `OCR.Run` keeps a rebuild-on-exception self-heal. The live path: each page's `GetLCDSampleTimer` fires `_Elapsed` on a ThreadPool thread → `GetLCDSampleImage` → `LCD.TestImage` → `DetectStringRegion` → `ocr.Run` (the `_lcdProcessing` Interlocked guard prevents overlap; only ONE page's timer runs at a time via Enable/DisableLive). **The whole threading saga was a red herring** — `[ThreadStatic]`, `OCR.Shared`, the `LCD._ocrQueues` worker queue, a dedicated engine thread, AND disabling MKL-DNN were all tried and NONE fixed it, because the real cause was the DLL version. Keep MKL-DNN; keep versions matched. The same error ALSO appears if the native runtime DLLs are missing from the app output (`dll/x64/`) — see the `runtime.win64.mkl.props` Import gotcha above.
- MSBuild flag format in bash: use `-p:Configuration=Release` not `/p:Configuration=Release`
- **App version lives in `VTMMain/AppInfo.cs`** (`AppInfo.Version`) — splash, status bar and About all read it. `SplashScreen.xaml` deliberately has **no** `Content` on `lbVersion` (code-behind overwrites it; a hardcoded one there is dead text that lies). `VTMMain/Properties/AssemblyInfo.cs` must be bumped **by hand** alongside it (an attribute needs a compile-time literal). Other projects cannot see `AppInfo` (namespace `VTMMain`) — `VTMReport` is on its own version track.
- `VisionPage`'s live `VisionBuider.Models` is only a **working copy of the selected step** for LED/LCD/FND — but the **one and only store** for **GLED** (Step has no GLED fields). `buf` (not `currentStep`) names the step it mirrors.
- The live **FND box does not mirror `buf` at all**: `VisionPage.xaml.cs:939` is `FNDchar[0].Rect = FNDchar[0].Rect;` (self-assign) with the real load commented out on the next line. So Save stamps the *session-global* live box onto whichever FND step is selected.
- Every live→step writeback (`SelectionChanged`, `Save Model`, `Save As`) is a **wholesale assign**, never an accumulate — which is why `NudgeRois` can safely move `buf`'s store as well as the live copy.
- `SerialPort.Port.DiscardInBuffer()` crashes if port is not open - always guard with null/IsOpen check
- ManualPage vision timers (FND/LCD) must NOT start in constructor - use EnableLive/DisableLive pattern
- `SystemComunication.GetFrame()` Console.Writes every frame sent (noisy in console)
- `SystemBoard.SerialPort_SerialDataReciver` has `Task.Delay(50).Wait()` on every packet (blocking); now checksum-validates and scans the whole buffer for multiple frames
- **System board uses protocol v2** (`Firmware/sys_io/PROTOCOL.md`): fixed **6-byte one-signal frame** `[STX OPCODE KEY VALUE CRC ETX]` (STX=0x02, ETX=0x03, CRC=XOR of STX^OPCODE^KEY^VALUE). Only **one PC→board frame type** exists: `0x4F` output-write. Board→PC: `0x49` input, `0x41` ack.
  - **Inputs addressed by KEY** → the PC updates only signals the board actually reports → **no phantom SW_UP/SW_DOWN → MainUP can't be clobbered** (that was what made a periodic input decode cut UUT power).
  - **Outputs are delta** (only changed ones, one frame each) with **ACK-retry**: `AckTimeoutMs`=150, `MaxOutTries`=**10**, then logs `SYS: REQUEST TIMEOUT CLUP:1`. Retries are driven by **incoming frames** (no timer), so the real cadence is bounded by `POLLING_INTERVAL_MS`.
  - **ACK reports the board's ACTUAL pin state, never the requested value.** Critical: `applyOutput`'s SDOWN interlock can force CLUP/AC LOW, and echoing the request would tell the PC "done" while the pin never moved — it would clear pending and stop retrying. Reporting reality makes the retry meaningful (it succeeds the moment SDOWN allows) and makes a permanently-refused write visible.
  - **Firmware polling re-sends only SDOWN** (a sustained signal worth re-syncing). Buttons are momentary — a poll can't recover a press already released, on-change covers them, and polling them would break up the log's `xN` collapsing. `INPUT_CHANGE_REPEAT` was dropped: on-change sends **once**, polling is the safety net.
  - PC: `SystemComunication.BuildFrame`/`FrameOk`/opcodes/keys, `SystemBoard` (parse/annotate/`SendControl`/`SendOutput`/`RetryStalePendingOutputs`), `SystemMachineIO.OutputKV`/`ApplyInput`. `Debug.FrameCrcOk` accepts BOTH STX/ETX (system v2) and legacy XOR/0x56 (solenoid).
  - **v1 (bitmask) and v2 are NOT interoperable — flash firmware + app together on all 4 machines.**
- **Who owns the cylinder interlock: the BOARD, not the PC.** `applyOutput` gates CLUP/AC on its own `inSDOWN.state`. The PC's `_SS_DOWN` is only a cached copy that can be stale, so the `MainUP` setter has **no SS_DOWN guard** — it always sends and lets the board decide (an earlier PC-side guard swallowed valid raises, e.g. operator presses down before scanning, and left the jig stuck down). The **only** block is on the manual UI toggle: `SysIOControl.xaml` binds `IsEnabled="{Binding CanCommandCylinder}"` (= `NotEMC && SS_DOWN`). Don't re-add a check in code.
- There is **no `MainDOWN`**: the cylinder is one signal. `MainUP=true` ⇔ `CLUP:1` (raise), `MainUP=false` ⇔ `CLUP:0` (down). Firmware `readOutput()` reads the pin back for the ACK.
- **`GEN` (0x47) is BROKEN under v2 firmware** (known, parked): `SystemBoard.GEN()` still sends a 13-byte legacy `44 45 … 0x47` frame via `SendAndRead`, but the v2 `serialEvent` only parses STX frames and only handles `0x4F` → the board ignores it → timeout → the step fails with `Sys`. Only matters if a model uses the frequency-generator step. Fixing it needs a v2 frame that can carry a 32-bit value (the 6-byte frame can't).
- (v1/legacy, now DELETED) old SYSTEM frames had TWO shapes: sized `[44 45 06 cmd d0..d3 chk 56]` and no-size `[44 45 cmd data chk 56]`. The bitmask `DataToIO` drove `MainUP` from phantom SW_UP/SW_DOWN bits (=0), so any periodic decode cut UUT power. v2 removes this by addressing inputs by key. The v1 codecs `IOtoData`/`DataToIO`/`ButtonControl`/`GetValue` **and the `SW_*` properties** are now removed (the board never reported SW_*, nothing read them, no XAML binding). `SystemComunication.GetFrame` stays — solenoid/relay/etc. still use it.
- Arduino Solenoid firmware: `delay(10)` was too short at 9600 baud for 10-byte frame (changed to 15)
- `SendToControls` response validation only checks frame length==6, NOT actual content (GetResponse is commented out at line 341)
- `Program.cs` line 2658: range parsing `Split('~')[0]` was parsing start twice instead of end - FIXED to `[1]`
- **Terminal-state race, BOTH directions (fixed 2026-07-16):** `MachineIO_OnCancleRequest` runs on the **serial RX thread**, concurrent with `ProgramState`'s test-done block — they both read/write `TestState` and could overwrite each other. Symptoms: STOP overwriting a PASS/FAIL (a PASS raises the cylinder → SS_DOWN drops → cancel fires), or a FAIL overwriting a STOP the operator just pressed. Fixed with **`_stateLock`** (in `ModelTester/Program.cs`, shared via the partial class) — whoever takes it first wins:
  - Cancel: `lock(_stateLock) { if (IsTestting && (TestState == TESTTING || TestState == PAUSE)) { TestState = STOP; IsTestting = false; } }` → a **concluded** run (GOOD/FAIL) can't be cancelled.
  - Test-done: `lock(_stateLock) { concluded = TestState == TESTTING; if (concluded) TestState = TestOK ? FAIL : GOOD; IsTestting = false; }` then `if (!concluded) break;` → a **STOP that already landed wins** and the result is rejected.
  - Ordering also matters: outputs (`MainUP` raise, `LPG`, `SendControl()`) and the blocking `ShowResult()` run **after** the state is concluded, so raising the cylinder can't drop SS_DOWN while the state still reads TESTTING.

## Version Log
| Version | Date | Summary of Changes |
|---------|------|-------------------|
| - | 2026-07-18 | **Solution de-branded + flattened.** Removed the `TNG.` prefix from every project — name, folder, `.csproj`, `AssemblyName`, **and code namespace** (148 source files): `TNG.Controls`→`Controls`, `TNG.Utility`→`Utility` (folder was `TNG.Utility`, csproj already `Utility.csproj`), `TNG.StandantLocalUsers`→`StandantLocalUsers`, `TNG.VTM.Program`→`VTM.Program`, `TNG.VTM.Base`→`VTM.Base` (**folder `TNG.VTM.Model`→`VTM.Base`** — the one case where folder≠namespace, so its ProjectReference path needed a special replace). `Camera`/`VTMReport` unchanged (never had the prefix; `VTMReport`'s RootNamespace stays `VTM_Report`). **`VTM`→`VTMMain`** (folder + csproj + project name + **code namespace `VTM`→`VTMMain`**; assembly was already `VTMMain`). ⚠️ The app namespace **had** to change: once the state-machine project became `VTM.Program`, it was a *child* namespace of the app's `VTM`, so a bare `Program` inside `namespace VTM` bound to the namespace not the class → `CS0118`. Renaming the app ns to `VTMMain` (matching its assembly) fixes it and is why `VTM.Base`/`VTM.Program` `using`s were preserved verbatim. **Flattened the `.sln`**: deleted the 6 solution folders (Main/Helper/Controls/Model/Program/Report) + the whole `NestedProjects` section; all 8 projects are now siblings of the `.sln` (GUIDs + `ProjectConfigurationPlatforms` unchanged). **`Controls/UI control`→`Controls/OtherControls`** (killed the space; 15 csproj paths). **Deleted** the 3 installer projects (`VTM Setup`, `VTM_Install`, `VTM_Setup`). Assembly rename verified safe up front: **0** `pack://…;component` refs to any `TNG.*` assembly, **0** `TNG.` in any `.config` (no binding redirects), all sibling refs are `ProjectReference` (no `HintPath` to `TNG.*.dll`). Build: **0 errors**, 8 outputs (`Controls.dll`, `Utility.dll`, `StandantLocalUsers.dll`, `VTM.Base.dll`, `VTM.Program.dll`, `Camera.dll`, `VTMReport.exe`, `VTMMain.exe`). |
| **2.8** | 2026-07-17 | **Model files: fixed the `[JsonIgnore]` that never worked** — `Step.cs` imported Newtonsoft's attribute while models save via System.Text.Json, so 11 runtime-only members (results, tooltips, `Min_Max`, …) were written into every `.vmdl`. Verified empirically against the real `ok.vmdl`: load is byte-identical, 0 mismatches — but it saves only **1,316 of 2,220,074 bytes (0.06%)**; the 2.2 MB is `FNDsBoard0` (91.2%), i.e. the every-step-carries-every-family issue, NOT this. **Vision nudge now moves every ROI of every step**, via a new `Translate()` on `SingleLED`/`SingleGLED`/`LCD`/`FND` that offsets `rect` directly — bypassing the `X`/`Y` cache (was 0 on fresh models → first click collapsed all 32 LEDs onto x=dx) and the clamping/truncating `Rect` property (made live and stored copies shift by different amounts). Bounds are checked **up front, all-or-nothing** (`NudgeFits`), because that clamp was also the only thing stopping an out-of-frame ROI from crashing the app through `async void LCD.TestImage`. **Fixed the `SingleLED`/`SingleGLED` ctors** to sync `_x`/`_y` from `rect` (pre-existing; also made the LED grid show 0). **Settings: "Export log" checkbox** (default on) gating the end-of-test `.lgd` write at both call sites — guard sits on the file write only, since `SaveLogFile` also normalises CAM/LED results into the live model. Folder icon → Font Awesome. **Status bar:** `IN START` + `IN STOP` indicators, log-directory warning replaces the startup MessageBox, Check Connections → `CircleCheck` icon (150px → 34px). **Version now single-sourced** from `VTM/AppInfo.cs` (splash/status bar/About) + AssemblyVersion → 2.8.0.0. `Firmware/system_board_anti_glicth` → `Firmware/sys_io`. ⚠️ `VTM/AppInfo.cs` and the firmware folder were **untracked in git** — now added; committing `VTM.csproj` without `AppInfo.cs` breaks the build (CS0103). |
| - | 2026-07-15/16 | **System board → protocol v2** (STX/OPCODE/KEY/VALUE/CRC/ETX, one signal per frame; key-addressed inputs; delta outputs + ACK-retry 10× where the ACK reports the real pin state). **Deleted v1 codecs** (`IOtoData`/`DataToIO`/`ButtonControl`/`GetValue`) and the phantom `SW_*` properties that drove MainUP. **Cylinder interlock moved wholly to the firmware** (PC `MainUP` setter no longer guards on SS_DOWN; only the manual UI toggle blocks, via `CanCommandCylinder`). **Fixed the terminal-state race both directions** with `_stateLock` (STOP can't overwrite a concluded PASS/FAIL; a FAIL can't overwrite a landed STOP). **Fixed `BarcodeNext` auto-load** (was hardcoded `Boards[0]`). **Reworked the Diagnostic Log**: uniform amber, flat timestamped lines, `SYS ← VTM :` arrows by data-flow, short `LogTag`, `xN` repeat coalescing, daily file under `%USERPROFILE%\FCTDebugLog\`, no connection/`non lock` noise. AutoPage: `-1 FAIL` button. ⚠️ Firmware + app must be flashed/deployed **together**. |
