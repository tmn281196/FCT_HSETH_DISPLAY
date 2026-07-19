using Microsoft.Win32;
using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Xml.Serialization;

namespace VTMUtility
{
    // Writes a System.Windows.Rect as just {X,Y,Width,Height} instead of the 15 computed properties STJ emits by
    // default (IsEmpty, Location, Size, Left, Top, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight, ...).
    // Measured on real models a Rect was ~280-420 bytes and there are thousands per file - ~68% of a .vmdl.
    //
    // Backward compatible: Read pulls X/Y/Width/Height and skips everything else, so it loads BOTH the old
    // 15-property form (which also contains X/Y/Width/Height) and the new compact one. Only the WRITE shape
    // changes - old app versions cannot read new files, but this app reads both.
    public sealed class RectJsonConverter : JsonConverter<System.Windows.Rect>
    {
        public override System.Windows.Rect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return new System.Windows.Rect();
            if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("Rect must be an object");

            double x = 0, y = 0, w = 0, h = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject) break;
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                string name = reader.GetString();
                reader.Read();   // move onto the value
                switch (name)
                {
                    case "X": x = ReadNumber(ref reader); break;
                    case "Y": y = ReadNumber(ref reader); break;
                    case "Width": w = ReadNumber(ref reader); break;
                    case "Height": h = ReadNumber(ref reader); break;
                    default: reader.Skip(); break;   // Location / Size / Left / TopLeft / ... (old form): ignore
                }
            }
            if (w < 0) w = 0;   // Rect ctor rejects negative extents
            if (h < 0) h = 0;
            return new System.Windows.Rect(x, y, w, h);
        }

        private static double ReadNumber(ref Utf8JsonReader reader)
        {
            return reader.TokenType == JsonTokenType.Number ? reader.GetDouble() : 0;
        }

        public override void Write(Utf8JsonWriter writer, System.Windows.Rect value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("X", value.X);
            writer.WriteNumber("Y", value.Y);
            writer.WriteNumber("Width", value.Width);
            writer.WriteNumber("Height", value.Height);
            writer.WriteEndObject();
        }
    }

    public static class Extensions
    {
        // One shared instance - stateless, so safe to reuse across every options bag.
        private static readonly RectJsonConverter RectConverter = new RectJsonConverter();
        public const string LogDefine = "Dev Program Log File";
        public const string LogExt = ".elog";
        private static string LogFile = "LOG_PROGRAM" + DateTime.Now.ToString("ddMMyyyy") + ".elog";


        // JSON clone oject
        public static T Clone<T>(this T source)
        {
            var serialized = JsonSerializer.Serialize(source);
            //Console.WriteLine(serialized);
            return JsonSerializer.Deserialize<T>(serialized);
        }

        public static T DeepCopyXML<T>(this T input)
        {
            var stream = new MemoryStream();

            var serializer = new XmlSerializer(typeof(T));
            serializer.Serialize(stream, input);
            stream.Position = 0;
            return (T)serializer.Deserialize(stream);
        }

        // JSON convert opject to String
        public static string ConvertToJson<T>(this T source)
        {
            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                MaxDepth = 1024,
                //WriteIndented = true
            };
            options.Converters.Add(RectConverter);
            return JsonSerializer.Serialize(source, options);
        }
        public static string ConvertToJsonViewwer<T>(this T source)
        {
            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                MaxDepth = 1024,
                WriteIndented = true
            };
            return JsonSerializer.Serialize(source, options);
        }

        public static T ConvertFromJson<T>(string jsonStr)
        {
            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                MaxDepth = 1024,
                //WriteIndented = true
            };
            options.Converters.Add(RectConverter);
            return JsonSerializer.Deserialize<T>(jsonStr, options);
        }

        //Encoder string
        public static string Encoder(string plainText, Encoding encodingCode)
        {
            var plainTextBytes = encodingCode.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        //Decoder string
        public static string Decoder(string base64EncodedData, Encoding encodingCode)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return encodingCode.GetString(base64EncodedBytes);
        }

        public static T OpenFromFile<T>(string FileName)
        {
            if (File.Exists(FileName))
            {
                try
                {
                    JsonSerializerOptions options = new JsonSerializerOptions()
                    {
                        MaxDepth = 1024,
                        WriteIndented = true
                    };
                    options.Converters.Add(RectConverter);

                    var serialized = File.ReadAllText(FileName);
                    serialized = Decoder(serialized, Encoding.UTF7);
                    File.WriteAllText(Environment.CurrentDirectory + "\\temp.txt", serialized);
                    //Console.WriteLine(serialized);
                    LogErr( DateTime.Now.ToString() + "Extension : Open from file SUCCESS " + FileName + Environment.NewLine + serialized + Environment.NewLine);
                    return JsonSerializer.Deserialize<T>(serialized, options);

                }
                catch (Exception err)
                {
                    MessageBox.Show(err.StackTrace);
                    LogErr(DateTime.Now.ToString() + "Extension : Open from file FAIL - " + FileName + err.Message + Environment.NewLine);
                }
            }
            else
            {
                //MessageBox.Show( Resource.ProgramContext_en_US.FileNotFound + ": " + FileName);
            }
            return default;
        }

        public static bool SaveToFile<T>(this T source, string FileName)
        {
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions()
                {
                    MaxDepth = 1024,
                    WriteIndented = true
                };
                options.Converters.Add(RectConverter);

                var strToSave = JsonSerializer.Serialize(source, options);

                // Readable sidecar next to the model. A .vmdl is Base64-of-UTF7, so this is the only way to see
                // or diff what was actually written. Only for models: this method also saves Config.cfg and the
                // printer configs, which do not need one.
                // Replaces a dump to "temp.txt" in the process CWD that EVERY save of EVERY type overwrote with
                // itself, plus a Console.WriteLine of the whole string - which for a real model is 8 MB per save.
                if (FileName != null && FileName.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
                {
                    // Never let the convenience copy fail the real save.
                    try { File.WriteAllText(Path.ChangeExtension(FileName, ".json"), strToSave); }
                    catch { }
                }

                strToSave = Encoder(strToSave, Encoding.UTF7);
                File.WriteAllText(FileName, strToSave);
                return true;
            }
            catch (Exception err)
            {
                LogErr(DateTime.Now.ToString() + " Extension : Save to file FAIL -" + err.Message + Environment.NewLine);
                return false;
            }

        }

        public static void LogErr(string errMessage)
        {
            if (!Directory.Exists(Environment.CurrentDirectory + @"\log\"))
            {
                Directory.CreateDirectory(Environment.CurrentDirectory + @"\log\");
            }
            File.AppendAllText(Environment.CurrentDirectory + @"\log\" + LogFile, DateTime.Now.ToString() + " Extension : " + errMessage + Environment.NewLine);
        }

        public static void DataGrid2CSV(DataGrid comparisonGrid, string Title, string FileExit, string FileType)
        {
            SaveFileDialog saveDlg = new SaveFileDialog();
            saveDlg.Filter = FileType + " (*." + FileExit + "|*." + FileExit;
            saveDlg.FilterIndex = 0;
            saveDlg.RestoreDirectory = true;
            saveDlg.Title = Title;
            if ((bool)saveDlg.ShowDialog())
            {
                string CsvFpath = saveDlg.FileName;
                System.IO.StreamWriter csvFileWriter = new StreamWriter(CsvFpath, false);
                string columnHeaderText = "";

                int countColumn = comparisonGrid.Columns.Count - 1;
                if (countColumn >= 0)
                {
                    columnHeaderText = comparisonGrid.Columns[0].Header.ToString();
                }

                // Writing column headers
                for (int i = 1; i <= countColumn; i++)
                {
                    columnHeaderText = columnHeaderText + ',' + (comparisonGrid.Columns[i].Header).ToString();
                }
                csvFileWriter.WriteLine(columnHeaderText);

                // Writing values row by row
                for (int i = 0; i <= comparisonGrid.Items.Count - 2; i++)
                {
                    string dataFromGrid = "";
                    for (int j = 0; j <= comparisonGrid.Columns.Count - 1; j++)
                    {
                        if (j == 0)
                        {
                            dataFromGrid = ((DataRowView)comparisonGrid.Items[i]).Row.ItemArray[j].ToString();
                        }
                        else
                        {
                            dataFromGrid = dataFromGrid + ',' + ((DataRowView)comparisonGrid.Items[i]).Row.ItemArray[j].ToString();
                        }
                    }
                    csvFileWriter.WriteLine(dataFromGrid);
                }
                csvFileWriter.Flush();
                csvFileWriter.Close();
            }
        }

        public static string ToEnumString<T>(T type)
        {
            var enumType = typeof(T);
            var name = Enum.GetName(enumType, type);
            var enumMemberAttribute = ((EnumMemberAttribute[])enumType.GetField(name).GetCustomAttributes(typeof(EnumMemberAttribute), true)).Single();
            return enumMemberAttribute.Value;
        }

        public static T ToEnum<T>(string str)
        {
            var enumType = typeof(T);
            foreach (var name in Enum.GetNames(enumType))
            {
                var enumMemberAttribute = ((EnumMemberAttribute[])enumType.GetField(name).GetCustomAttributes(typeof(EnumMemberAttribute), true)).Single();
                if (enumMemberAttribute.Value == str) return (T)Enum.Parse(enumType, name);
            }
            //throw exception or whatever handling you want or
            return default(T);
        }
    }
}
