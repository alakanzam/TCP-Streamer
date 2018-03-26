using System;
using System.IO;
using System.Linq;
using System.Text;

namespace WinSound
{
    /// <summary>
    ///     WaveFile
    /// </summary>
    public class WaveFile
    {
        //Attribute
        public const int WAVE_FORMAT_PCM = 1;

        /// <summary>
        ///     WriteNew
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="data"></param>
        public static void Create(string fileName, uint samplesPerSecond, short bitsPerSample, short channels,
            byte[] data)
        {
            //Bestehende Datei löschen
            if (File.Exists(fileName))
                File.Delete(fileName);

            //Header erstellen
            var header = CreateNewWaveFileHeader(samplesPerSecond, bitsPerSample, channels, (uint) data.Length,
                44 + data.Length);
            //Header schreiben
            WriteHeader(fileName, header);
            //Daten schreiben
            WriteData(fileName, header.DATAPos, data);
        }

        /// <summary>
        ///     AppendData
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="data"></param>
        public static void AppendData(string fileName, byte[] data)
        {
            AppendData(fileName, data, false);
        }

        /// <summary>
        ///     AppendData
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="data"></param>
        /// <param name="forceWriting"></param>
        public static void AppendData(string fileName, byte[] data, bool forceWriting)
        {
            //Header auslesen
            var header = ReadHeader(fileName);

            //Wenn Daten vorhanden
            if (header.DATASize > 0 || forceWriting)
            {
                //Daten anfügen
                WriteData(fileName, (int) (header.DATAPos + header.DATASize), data);

                //Header aktualisieren
                header.DATASize += (uint) data.Length;
                header.RiffSize += (uint) data.Length;

                //Header überschreiben
                WriteHeader(fileName, header);
            }
        }

        /// <summary>
        ///     Read
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static WaveFileHeader Read(string fileName)
        {
            //Header lesen
            var header = ReadHeader(fileName);

            //Fertig
            return header;
        }

        /// <summary>
        ///     CreateWaveFileHeader
        /// </summary>
        /// <param name="SamplesPerSecond"></param>
        /// <param name="BitsPerSample"></param>
        /// <param name="Channels"></param>
        /// <param name="dataSize"></param>
        /// <returns></returns>
        private static WaveFileHeader CreateNewWaveFileHeader(uint SamplesPerSecond, short BitsPerSample,
            short Channels, uint dataSize, long fileSize)
        {
            //Header erstellen
            var Header = new WaveFileHeader();

            //Werte setzen
            Array.Copy("RIFF".ToArray(), Header.RIFF, 4);
            Header.RiffSize = (uint) (fileSize - 8);
            Array.Copy("WAVE".ToArray(), Header.RiffFormat, 4);
            Array.Copy("fmt ".ToArray(), Header.FMT, 4);
            Header.FMTSize = 16;
            Header.AudioFormat = WAVE_FORMAT_PCM;
            Header.Channels = Channels;
            Header.SamplesPerSecond = SamplesPerSecond;
            Header.BitsPerSample = BitsPerSample;
            Header.BlockAlign = (short) ((BitsPerSample * Channels) >> 3);
            Header.BytesPerSecond = (uint) (Header.BlockAlign * Header.SamplesPerSecond);
            Array.Copy("data".ToArray(), Header.DATA, 4);
            Header.DATASize = dataSize;

            //Fertig
            return Header;
        }

        /// <summary>
        ///     ReadHeader
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private static WaveFileHeader ReadHeader(string fileName)
        {
            //Ergebnis
            var header = new WaveFileHeader();

            //Wenn die Datei existiert
            if (File.Exists(fileName))
            {
                //Datei öffnen
                var fs = new FileStream(fileName, FileMode.Open, FileAccess.Read);
                var rd = new BinaryReader(fs, Encoding.UTF8);

                //Lesen
                if (fs.CanRead)
                {
                    //Chunk 1
                    header.RIFF = rd.ReadChars(4);
                    header.RiffSize = (uint) rd.ReadInt32();
                    header.RiffFormat = rd.ReadChars(4);

                    //Chunk 2
                    header.FMT = rd.ReadChars(4);
                    header.FMTSize = (uint) rd.ReadInt32();
                    header.FMTPos = fs.Position;
                    header.AudioFormat = rd.ReadInt16();
                    header.Channels = rd.ReadInt16();
                    header.SamplesPerSecond = (uint) rd.ReadInt32();
                    header.BytesPerSecond = (uint) rd.ReadInt32();
                    header.BlockAlign = rd.ReadInt16();
                    header.BitsPerSample = rd.ReadInt16();

                    //Zu Beginn von Chunk3 gehen
                    fs.Seek(header.FMTPos + header.FMTSize, SeekOrigin.Begin);

                    //Chunk 3
                    header.DATA = rd.ReadChars(4);
                    header.DATASize = (uint) rd.ReadInt32();
                    header.DATAPos = (int) fs.Position;

                    //Wenn nicht DATA
                    if (new string(header.DATA).ToUpper() != "DATA")
                    {
                        var DataChunkSize = header.DATASize + 8;
                        fs.Seek(DataChunkSize, SeekOrigin.Current);
                        header.DATASize = (uint) (fs.Length - header.DATAPos - DataChunkSize);
                    }

                    //Payload einlesen
                    if (header.DATASize <= fs.Length - header.DATAPos)
                        header.Payload = rd.ReadBytes((int) header.DATASize);
                }

                //Schliessen
                rd.Close();
                fs.Close();
            }

            //Fertig
            return header;
        }

        /// <summary>
        ///     WriteHeader
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="header"></param>
        /// <param name="dataSize"></param>
        public static void WriteHeader(string fileName, WaveFileHeader header)
        {
            //Datei öffnen
            var fs = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            var wr = new BinaryWriter(fs, Encoding.UTF8);

            //Chunk 1
            wr.Write(header.RIFF);
            wr.Write(Int32ToBytes((int) header.RiffSize));
            wr.Write(header.RiffFormat);

            //Chunk 2
            wr.Write(header.FMT);
            wr.Write(Int32ToBytes((int) header.FMTSize));
            wr.Write(Int16ToBytes(header.AudioFormat));
            wr.Write(Int16ToBytes(header.Channels));
            wr.Write(Int32ToBytes((int) header.SamplesPerSecond));
            wr.Write(Int32ToBytes((int) header.BytesPerSecond));
            wr.Write(Int16ToBytes(header.BlockAlign));
            wr.Write(Int16ToBytes(header.BitsPerSample));

            //Chunk 3
            wr.Write(header.DATA);
            wr.Write(Int32ToBytes((int) header.DATASize));

            //Datei schliessen
            wr.Close();
            fs.Close();
        }

        /// <summary>
        ///     WriteData
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="pos"></param>
        public static void WriteData(string fileName, int pos, byte[] data)
        {
            //Datei öffnen
            var fs = new FileStream(fileName, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            var wr = new BinaryWriter(fs, Encoding.UTF8);

            //An Schreibposition gehen
            wr.Seek(pos, SeekOrigin.Begin);
            //Daten schreiben
            wr.Write(data);
            //Fertig
            wr.Close();
            fs.Close();
        }

        //--------------------------------------------------------------------------------------------
        // BytesToInt32
        //--------------------------------------------------------------------------------------------
        private static int BytesToInt32(ref byte[] bytes)
        {
            var Int32 = 0;
            Int32 = (Int32 << 8) + bytes[3];
            Int32 = (Int32 << 8) + bytes[2];
            Int32 = (Int32 << 8) + bytes[1];
            Int32 = (Int32 << 8) + bytes[0];
            return Int32;
        }

        //--------------------------------------------------------------------------------------------
        // BytesToInt16
        //--------------------------------------------------------------------------------------------
        private static short BytesToInt16(ref byte[] bytes)
        {
            short Int16 = 0;
            Int16 = (short) ((Int16 << 8) + bytes[1]);
            Int16 = (short) ((Int16 << 8) + bytes[0]);
            return Int16;
        }

        //--------------------------------------------------------------------------------------------
        // Int32ToByte
        //--------------------------------------------------------------------------------------------
        private static byte[] Int32ToBytes(int value)
        {
            var bytes = new byte[4];
            bytes[0] = (byte) (value & 0xFF);
            bytes[1] = (byte) ((value >> 8) & 0xFF);
            bytes[2] = (byte) ((value >> 16) & 0xFF);
            bytes[3] = (byte) ((value >> 24) & 0xFF);
            return bytes;
        }

        //--------------------------------------------------------------------------------------------
        // Int16ToBytes
        //--------------------------------------------------------------------------------------------
        private static byte[] Int16ToBytes(short value)
        {
            var bytes = new byte[2];
            bytes[0] = (byte) (value & 0xFF);
            bytes[1] = (byte) ((value >> 8) & 0xFF);
            return bytes;
        }
    }

    /// <summary>
    ///     WaveFileHeader
    /// </summary>
    public class WaveFileHeader
    {
        public short AudioFormat;
        public short BitsPerSample;
        public short BlockAlign;
        public uint BytesPerSecond;
        public short Channels;

        //Chunk 3
        public char[] DATA = new char[4];

        //HeaderLength
        public int DATAPos = 44;

        public uint DATASize;

        //Chunk 2
        public char[] FMT = new char[4];

        //Position FormatSize
        public long FMTPos = 20;

        public uint FMTSize = 16;

        //Data
        public byte[] Payload = new byte[0];

        //Chunk 1
        public char[] RIFF = new char[4];

        public char[] RiffFormat = new char[4];
        public uint RiffSize = 8;
        public uint SamplesPerSecond;

        /// <summary>
        ///     Duration
        /// </summary>
        /// <param name="header"></param>
        /// <returns></returns>
        public TimeSpan Duration
        {
            get
            {
                var blockAlign = (BitsPerSample * Channels) >> 3;
                var bytesPerSec = (int) (blockAlign * SamplesPerSecond);
                var value = Payload.Length / (double) bytesPerSec;

                //Fertig
                return new TimeSpan(0, 0, (int) value);
            }
        }
    }
}