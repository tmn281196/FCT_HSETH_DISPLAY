using VTMBase;
using Utility;
using System.Collections.ObjectModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VTMControls;
using VTMControls.DeviceControl;

namespace VTMBase
{
    public partial class Program
    {
        private ObservableCollection<Board> _Boards = new ObservableCollection<Board>();

        public ObservableCollection<Board> Boards
        {
            get { return _Boards; }
            set
            {
                if (value != null || value != _Boards) _Boards = value;
            }
        }

        public BoardResultPanel ResultPanel = new BoardResultPanel();

        public void SetBoards()
        {
            Boards.Clear();
            for (int i = 0; i < TestModel.Layout.PCB_Count; i++)
            {
                Boards.Add(new Board()
                {
                    ModelSource = TestModel.Path,
                    ModelName = TestModel.Name
                });
            }
            if (Boards.Count >= 1) Boards[0].SiteName = "A";
            if (Boards.Count >= 2) Boards[1].SiteName = "B";
            if (Boards.Count >= 3) Boards[2].SiteName = "C";
            if (Boards.Count >= 4) Boards[3].SiteName = "D";
            switch (Boards.Count)
            {
                case 1:
                    for (int channel = 0; channel < 96; channel++)
                    {
                        Boards[0].MuxChannels.Add(MuxCard.Card.Chanels[channel]);
                    }

                    for (int channel = 0; channel < 8; channel++)
                    {
                        Boards[0].LevelChannels.Add(TestModel.LevelCard.Chanels[channel]);
                    }
                    for (int channel = 8; channel < 16; channel++)
                    {
                        Boards[0].LevelChannels.Add(new LevelChannel { Channel = channel });
                    }

                    for (int channel = 16; channel < 40; channel++)
                    {
                        Boards[0].LevelChannels.Add(TestModel.LevelCard.Chanels[channel]);
                    }
                    for (int channel = 40; channel < 48; channel++)
                    {
                        Boards[0].LevelChannels.Add(new LevelChannel { Channel = channel });
                    }
                    for (int channel = 48; channel < 64; channel++)
                    {
                        Boards[0].LevelChannels.Add(TestModel.LevelCard.Chanels[channel]);
                    }
                    break;

                case 2:
                    for (int channel = 0; channel < 48; channel++)
                    {
                        Boards[0].MuxChannels.Add(MuxCard.Card.Chanels[channel]);
                        Boards[1].MuxChannels.Add(MuxCard.Card.Chanels[channel + 48]);
                    }
                    for (int channel = 0; channel < 32; channel++)
                    {
                        if (channel < 8 || channel > 15)
                        {
                            Boards[0].LevelChannels.Add(TestModel.LevelCard.Chanels[channel]);
                            Boards[1].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 32]);
                        }
                    }
                    break;

                case 3:
                    for (int channel = 0; channel < 24; channel++)
                    {
                        Boards[0].MuxChannels.Add(MuxCard.Card.Chanels[channel]);
                        Boards[1].MuxChannels.Add(MuxCard.Card.Chanels[channel + 48]);
                        Boards[2].MuxChannels.Add(MuxCard.Card.Chanels[channel + 24]);
                    }

                    for (int channel = 0; channel < 4; channel++)
                    {
                        Boards[0].LevelChannels.Add(TestModel.LevelCard.Chanels[channel]);
                        Boards[1].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 32]);
                        Boards[2].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 4]);
                    }
                    for (int channel = 16; channel < 24; channel++)
                    {
                        Boards[0].LevelChannels.Add(TestModel.LevelCard.Chanels[channel]);
                        Boards[1].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 32]);
                        Boards[2].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 8]);
                    }

                    break;

                case 4:
                    for (int channel = 0; channel < 24; channel++)
                    {
                        Boards[0].MuxChannels.Add(MuxCard.Card.Chanels[channel]);
                        Boards[1].MuxChannels.Add(MuxCard.Card.Chanels[channel + 48]);
                        Boards[2].MuxChannels.Add(MuxCard.Card.Chanels[channel + 24]);
                        Boards[3].MuxChannels.Add(MuxCard.Card.Chanels[channel + 72]);
                    }

                    for (int channel = 0; channel < 4; channel++)
                    {
                        Boards[0].LevelChannels.Add(TestModel.LevelCard.Chanels[channel]);
                        Boards[1].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 32]);
                        Boards[2].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 4]);
                        Boards[3].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 36]);
                    }
                    for (int channel = 16; channel < 24; channel++)
                    {
                        Boards[0].LevelChannels.Add(TestModel.LevelCard.Chanels[channel]);
                        Boards[1].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 32]);
                        Boards[2].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 8]);
                        Boards[3].LevelChannels.Add(TestModel.LevelCard.Chanels[channel + 40]);
                    }
                    break;

                default:
                    break;
            }
            for (int i = 0; i < Boards[0].LevelChannels.Count(); i++)
            {
                if (Boards.Count >= 2) Boards[1].LevelChannels[i].IsUse = Boards[0].LevelChannels[i].IsUse;
                if (Boards.Count >= 3) Boards[2].LevelChannels[i].IsUse = Boards[0].LevelChannels[i].IsUse;
                if (Boards.Count >= 4) Boards[3].LevelChannels[i].IsUse = Boards[0].LevelChannels[i].IsUse;
            }
        }

        /// <summary>
        /// Turn MUX channel on
        /// </summary>
        /// <param name="paramString">MUX channel each board (1 ~ 48)</param>
        /// <param name="board">board index (1 ~ 4)</param>
        public bool SetBoardMux(string paramString, int boardIndex, out bool IsMux2)
        {
            int P = 99;
            int N = 99;
            IsMux2 = false;

            if (boardIndex > Boards.Count) return false;

            if (paramString.Contains("/"))
            {
                var channelStrs = paramString.Split('/');
                if (!int.TryParse(channelStrs[0], out P))
                {
                    return false;
                }

                if (!int.TryParse(channelStrs[1], out N))
                {
                    return false;
                }

                switch (Boards.Count)
                {
                    case 1:
                        if (P >= 49) IsMux2 = true;
                        return MuxCard.Card.ManualSetCardStatus(P, N);

                    case 2:
                        if (P >= 49) return false;
                        switch (boardIndex)
                        {
                            case 1:
                                return MuxCard.Card.ManualSetCardStatus(P, N);

                            case 2:
                                return MuxCard.Card.ManualSetCardStatus(P + 48, N + 48);

                            default:
                                break;
                        }
                        break;

                    case 3:
                        if (P >= 25) return false;
                        switch (boardIndex)
                        {
                            case 1:
                                return MuxCard.Card.ManualSetCardStatus(P, N);

                            case 2:
                                return MuxCard.Card.ManualSetCardStatus(P + 48, N + 48);

                            case 3:
                                return MuxCard.Card.ManualSetCardStatus(P + 24, N + 24);

                            default:
                                break;
                        }
                        break;

                    case 4:
                        if (P >= 25) return false;
                        switch (boardIndex)
                        {
                            case 1:
                                return MuxCard.Card.ManualSetCardStatus(P, N);

                            case 2:
                                return MuxCard.Card.ManualSetCardStatus(P + 48, N + 48);

                            case 3:
                                return MuxCard.Card.ManualSetCardStatus(P + 24, N + 24);

                            case 4:
                                return MuxCard.Card.ManualSetCardStatus(P + 72, N + 72);

                            default:
                                break;
                        }
                        break;

                    default:
                        break;
                }
            }
            else
            {
                if (!int.TryParse(paramString, out P))
                {
                    return false;
                }
                else
                {
                    switch (Boards.Count)
                    {
                        case 1:
                            if (P >= 49) IsMux2 = true;
                            return MuxCard.Card.ManualSetCardStatus(P);

                        case 2:
                            if (P >= 49) return false;

                            switch (boardIndex)
                            {
                                case 1:
                                    return MuxCard.Card.ManualSetCardStatus(P);

                                case 2:
                                    return MuxCard.Card.ManualSetCardStatus(P + 48);

                                default:
                                    break;
                            }
                            break;

                        case 3:
                            if (P >= 25) return false;

                            switch (boardIndex)
                            {
                                case 1:
                                    return MuxCard.Card.ManualSetCardStatus(P);

                                case 2:
                                    return MuxCard.Card.ManualSetCardStatus(P + 48);

                                case 3:
                                    return MuxCard.Card.ManualSetCardStatus(P + 24);

                                default:
                                    break;
                            }
                            break;

                        case 4:
                            if (P >= 25) return false;

                            switch (boardIndex)
                            {
                                case 1:
                                    return MuxCard.Card.ManualSetCardStatus(P, N);

                                case 2:
                                    return MuxCard.Card.ManualSetCardStatus(P + 48);

                                case 3:
                                    return MuxCard.Card.ManualSetCardStatus(P + 24);

                                case 4:
                                    return MuxCard.Card.ManualSetCardStatus(P + 72);

                                default:
                                    break;
                            }
                            break;

                        default:
                            break;
                    }
                }
            }
            return false;
        }

        // Barcode
        public void CheckBarcodeReader(string COMPORT)
        {
            BarcodeReader.Port = new System.IO.Ports.SerialPort()
            {
                PortName = appSetting.Communication.ScannerPort,
                BaudRate = appSetting.Communication.Scan_Baudrate,
                DataBits = appSetting.Communication.Scan_Databit,
                Parity = appSetting.Communication.Scan_Parity
            };
            BarcodeReader.DeviceName = "SCANNER";
            BarcodeReader.PortName = appSetting.Communication.ScannerPort;
            BarcodeReader.SerialDataReciver -= BarcodeReader_SerialDataReciver;
            BarcodeReader.SerialDataReciver += BarcodeReader_SerialDataReciver;
            BarcodeReader.Port.ReadTimeout = 1000;
            BarcodeReader.Port.NewLine = "\r";
            try
            {
                BarcodeReader.Port.Open();
                BarcodeReader.OpenPort();
            }
            catch (Exception)
            {
            }
        }

        private void BarcodeReader_SerialDataReciver(object sender, EventArgs e)
        {
            string barcode = BarcodeReader.ReadLine();
            Console.WriteLine(barcode);
            BarcodeReader.Port.DiscardInBuffer();
            AcceptBarcode(barcode);
        }

        // Everything a scan does once the characters are in hand. Split out from the serial handler so the
        // fake-scan button drives the SAME path - a fake scan that took a shortcut would not be worth much.
        //
        // `fake` rides along with the barcode through both buffer slots so that a run started from a fake scan
        // can skip the .lgd export: those results are bench noise and must not land in the customer's log.
        public void AcceptBarcode(string barcode, bool fake = false)
        {
            if (!TestModel.BarcodeOption.BarcodeCheck(barcode))
            {
                Debug.Write(String.Format("Barcode invalid format:{0}", barcode), Debug.ContentType.Error);
                return;
            }

            // Two slots per site, run like a conveyor:
            //   Barcode     = the board being tested / ready to test
            //   BarcodeNext = the one scanned ahead, buffered for the next run
            // ONE rule, whether or not a test is running: fill the current slot, else buffer it. That is what lets
            // the operator scan ahead while sitting in READY - a scan there used to look for an empty Barcode slot,
            // find none, and get silently dropped. When a run ends, LoadScannedAheadBarcodes() shifts
            // BarcodeNext -> Barcode and empties the buffer, so the conveyor keeps moving.
            foreach (var item in Boards)
            {
                if (item.Skip) continue;

                // Slot 1 = Barcode (being tested / ready to test), slot 2 = BarcodeNext (scanned ahead).
                // Arrow points AT the slot that receives - "BUFFER 1 <- code" reads as "slot 1 takes this code".
                // Built from a code point so the source stays pure ASCII (same reason as Debug.cs).
                if (!item.BarcodeReady)
                {
                    Debug.Write(String.Format("SCANNER:BUFFER 1 {0} {1}", (char)0x2190, barcode), Debug.ContentType.Notify);
                    item.Barcode = barcode;
                    item.BarcodeIsFake = fake;
                    // This barcode is now the one to be tested (state goes READY next) - surface it like the shift path.
                    Debug.Write("SCANNER:CURRENT BARCODE  " + barcode, Debug.ContentType.Notify);
                    return;
                }
                if (item.BarcodeNext == "")
                {
                    Debug.Write(String.Format("SCANNER:BUFFER 2 {0} {1}", (char)0x2190, barcode), Debug.ContentType.Notify);
                    item.BarcodeNext = barcode;
                    item.BarcodeNextIsFake = fake;
                    return;
                }
            }

            // Both slots taken - the operator is already one ahead. Say so instead of dropping the scan on the floor.
            Debug.Write("SCANNER:BUFFER FULL!!!", Debug.ContentType.Warning);
        }

        private int _fakeScanSeq = 0;

        // Build a barcode the CURRENT model's own BarcodeCheck() accepts (right length, model code at the right
        // offset), so the fake scan is only faking the scanner - not the validation. Each call is unique, so two
        // fake scans fill both slots with distinct codes the way two real boards would.
        public string MakeFakeBarcode()
        {
            BarcodeOption opt = TestModel.BarcodeOption;

            // Head carries the model code at the offset BarcodeCheck looks for. Padded with '0', which cannot
            // contain the model code itself, so IndexOf finds it exactly at StartModelCodePosition.
            string head = "";
            if (opt.CompareModelCode)
                head = new string('0', Math.Max(0, opt.StartModelCodePosition)) + (opt.ModelCode ?? "");

            string body = "FAKE" + (++_fakeScanSeq).ToString("D4") + DateTime.Now.ToString("HHmmss");

            if (!opt.UseBarcodeLenghtFixed) return head + body;

            // Fixed length: the head is mandatory, so only the body may be trimmed or padded to fit.
            int room = opt.BarcodeLenght - head.Length;
            if (room < 0) return head;   // model code alone overflows the length - unbuildable, let the check reject it
            if (body.Length > room) body = body.Substring(0, room);
            return head + body.PadRight(room, '0');
        }

        public void CheckBoardReady()
        {
        }
    }
}