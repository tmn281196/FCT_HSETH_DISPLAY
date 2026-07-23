using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using DataFormats = System.Windows.DataFormats;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace VTMUtility
{
    public static class Debug
    {
        public enum ContentType
        {
            Error = 0,
            Notify = 1,
            Log = 2,
            Warning = 3,
            Tx = 4,
            Rx = 5,
        }

        public static RichTextBox LogBox = new RichTextBox()
        {
            Background = new SolidColorBrush(Colors.Black)
        };
        public static Dispatcher dispatcher;

        // Set false to silence TX/RX serial traffic in the log (it can be high-volume).
        public static bool EnableTxRx = true;

        // Optional per-frame annotator (deviceUpper, frame, len, isTx) -> short note (e.g. which outputs/inputs).
        // The app sets this to decode known frames (e.g. SYSTEM). Return "" to add no note.
        public static Func<string, byte[], int, bool, string> FrameAnnotator;
        // Keep the log bounded (trim oldest) instead of wiping it all at once.
        private const int MaxBlocks = 600;

        // Amber-CRT palette: two tones only, the same rule on every line. The leading element of a line - the
        // label before the first ':', or the "DEVICE >>/<< FCT" prefix on a serial line - is the bright amber so
        // it stands out; everything else (value, hex bytes, CRC, note, timestamp) is the dim amber.
        // Frozen -> safe to use from any thread (serial callbacks run off the UI thread).
        // All log text is one uniform amber brightness (no bright-label / dim-value split): AmberDim == AmberBright.
        private static readonly SolidColorBrush AmberBright = Frozen(Color.FromRgb(0xE8, 0x98, 0x00));
        private static readonly SolidColorBrush AmberDim = AmberBright;

        private static SolidColorBrush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

        // --- Daily log file. Every line shown in the log is also appended (plain text) to a per-day file under
        //     the current user's profile: %USERPROFILE%\FCTDebugLog\FCTDebug_yyyy-MM-dd.txt. The folder and file
        //     are created on demand; a new day rolls to a new file automatically. Thread-safe and never throws
        //     (serial callbacks run off the UI thread; logging must not be able to crash the app).
        private static readonly object _fileLock = new object();
        private static string _lastFileKey;
        private static string LogDir => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "FCTDebugLog");

        // A line whose key repeats the previous one is skipped (on screen that line just bumps its "xN" counter),
        // so a quiet poll can't flood the file. key == null -> always written, never treated as a repeat.
        private static void AppendToFile(string line, string key)
        {
            try
            {
                lock (_fileLock)
                {
                    if (key != null && key == _lastFileKey) return;
                    _lastFileKey = key;
                    string dir = LogDir;
                    if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
                    string path = System.IO.Path.Combine(dir, "FCTDebug_" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt");
                    System.IO.File.AppendAllText(path, line + Environment.NewLine);
                }
            }
            catch { /* logging must never crash the app */ }
        }

        // Add a log line's content as runs: the label (before the first ':') is UPPERCASE + bright amber; the
        // value (after ": ", one space) is dim amber, original case. No ':' -> the whole line is the bright label
        // (UPPERCASE). Nothing is bold - emphasis comes from the brighter amber only.
        private static void AddContentRuns(Paragraph p, string content)
        {
            content = (content ?? "").TrimStart('\t');
            int idx = content.IndexOf(':');
            if (idx >= 0)
            {
                string label = content.Substring(0, idx).ToUpperInvariant();
                string value = content.Substring(idx + 1).TrimStart();
                p.Inlines.Add(new Run(label) { Foreground = AmberBright });
                p.Inlines.Add(new Run(": " + value) { Foreground = AmberDim });
            }
            else
            {
                p.Inlines.Add(new Run(content.ToUpperInvariant()) { Foreground = AmberBright });
            }
        }

        // --- Consecutive-duplicate coalescing (UI thread only, so no locking needed). A line whose key repeats the
        //     previous line's does NOT add a new block: it bumps a "xN" counter on that line and refreshes its
        //     timestamp to the latest occurrence. key == null -> never coalesced (always a new line).
        private static string _lastKey;
        private static int _lastCount;
        private static Paragraph _lastParagraph;
        private static Run _lastTsRun;
        private static Run _lastCountRun;

        private static void Emit(Paragraph paragraph, Run leadRun, string key)
        {
            if (key != null && key == _lastKey && _lastParagraph != null)
            {
                _lastCount++;
                if (_lastTsRun != null && leadRun != null) _lastTsRun.Text = leadRun.Text;   // show the latest time
                string mark = "  x" + _lastCount;
                if (_lastCountRun == null)
                {
                    _lastCountRun = new Run(mark) { Foreground = AmberDim };
                    _lastParagraph.Inlines.Add(_lastCountRun);
                }
                else _lastCountRun.Text = mark;
                LogBox.ScrollToEnd();
                return;
            }

            var blocks = LogBox.Document.Blocks;
            blocks.Add(paragraph);
            while (blocks.Count > MaxBlocks && blocks.FirstBlock != null) blocks.Remove(blocks.FirstBlock);
            _lastKey = key;
            _lastCount = 1;
            _lastParagraph = paragraph;
            _lastTsRun = leadRun;
            _lastCountRun = null;
            LogBox.ScrollToEnd();
        }

        public static void Write(string content, ContentType type, int Size = 0)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss") + "   ";
            string text = (content ?? "").TrimStart('\t');
            string key = "W\t" + text;
            AppendToFile(ts + text, key);
            LogBox.Dispatcher.BeginInvoke(new Action(delegate
            {
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                // Timestamp flush-left; then content (label / value). Size ignored.
                var tsRun = new Run(ts) { Foreground = AmberDim };
                paragraph.Inlines.Add(tsRun);
                AddContentRuns(paragraph, content);
                Emit(paragraph, tsRun, key);
            }), DispatcherPriority.Background);
        }

        // Serial helpers: log TX (sent) / RX (received) frames as hex. Gated by EnableTxRx.
        // The arrow shows the real data-flow direction (device on the left, FCT/this-PC on the right):
        //   "DEVICE -> FCT" = the device sent to the FCT (RX) ; "DEVICE <- FCT" = the FCT sent to the device (TX).
        // Arrows built from code points so the source stays pure ASCII (no compile-time encoding surprises):
        // 0x2190 = left arrow (FCT -> device, TX), 0x2192 = right arrow (device -> FCT, RX).
        private static readonly string DirTx = ((char)0x2190) + " VTM :";
        private static readonly string DirRx = ((char)0x2192) + " VTM :";
        public static void Tx(string device, byte[] data, int len = -1) => WriteFrame(device, DirTx, data, len, ContentType.Tx);
        public static void Rx(string device, byte[] data, int len = -1) => WriteFrame(device, DirRx, data, len, ContentType.Rx);

        private static void WriteFrame(string device, string dir, byte[] data, int len, ContentType type)
        {
            if (!EnableTxRx || data == null) return;
            int n = len < 0 || len > data.Length ? data.Length : len;
            string dev = (device ?? "").ToUpperInvariant();
            var sb = new System.Text.StringBuilder();
            sb.Append(" [").Append(n.ToString("D2")).Append("] ");
            for (int i = 0; i < n; i++) { sb.Append(data[i].ToString("X2")); if (i < n - 1) sb.Append(' '); }
            sb.Append(FrameCrcOk(data, n) ? "  (CRC OK)" : "  (CRC NG)");
            var note = FrameAnnotator?.Invoke(dev, data, n, type == ContentType.Tx);
            // No brackets around the note: the annotators emit fixed-width columns, and wrapping them would
            // shift every line's last column by one and undo the alignment.
            if (!string.IsNullOrEmpty(note)) sb.Append("  ").Append(note);
            // "DEVICE >> FCT" / "DEVICE << FCT" prefix in the bright amber; the rest (bytes/CRC/note) in the dim amber.
            WriteFrameLine(dev + " " + dir, sb.ToString());
        }

        private static void WriteFrameLine(string prefix, string rest)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss") + "   ";
            string key = "F\t" + prefix + rest;
            AppendToFile(ts + prefix + rest, key);
            var box = LogBox;
            if (box == null) return;
            var disp = dispatcher ?? box.Dispatcher;
            if (disp == null) return;
            disp.BeginInvoke((Action)(() =>
            {
                var paragraph = new Paragraph { Margin = new Thickness(0) };
                var tsRun = new Run(ts) { Foreground = AmberDim };
                paragraph.Inlines.Add(tsRun);
                // "DEVICE -> VTM :" prefix, then bytes/CRC/note. Identical consecutive frames collapse to "xN".
                paragraph.Inlines.Add(new Run(prefix) { Foreground = AmberBright });
                paragraph.Inlines.Add(new Run(rest) { Foreground = AmberDim });
                Emit(paragraph, tsRun, key);
            }), DispatcherPriority.Background);
        }

        // Frame checksum = XOR of every byte before the checksum byte (2nd from the end). Valid when it matches
        // that byte AND the frame ends with the 0x56 'V' suffix. Display-only; mirrors SystemComunication's XOR.
        private static bool FrameCrcOk(byte[] data, int n)
        {
            if (n < 4) return false;
            // v2 system frame: [STX .. CRC ETX] (ends 0x03). CRC = XOR of every byte before it.
            if (data[0] == 0x02 && data[n - 1] == 0x03)
            {
                byte s = 0;
                for (int i = 0; i < n - 2; i++) s ^= data[i];
                return s == data[n - 2];
            }
            // legacy frame: [.. XOR 0x56].
            byte x = 0;
            for (int i = 0; i < n - 2; i++) x ^= data[i];
            return x == data[n - 2] && data[n - 1] == 0x56;
        }

        public static void ClearLog()
        {
            if (dispatcher != null)
            {
                dispatcher.Invoke(new Action(delegate
                {
                    LogBox.Document.Blocks.Clear();
                    // Drop the coalescing state too - the tracked paragraph is gone, and the next line starts fresh.
                    _lastKey = null;
                    _lastCount = 0;
                    _lastParagraph = null;
                    _lastTsRun = null;
                    _lastCountRun = null;
                }), DispatcherPriority.Normal);
            }
            lock (_fileLock) { _lastFileKey = null; }
        }
    }
}
