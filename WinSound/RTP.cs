using System;
using System.Linq;

namespace WinSound
{
    /// <summary>
    ///     RTPPacket
    /// </summary>
    public class RTPPacket
    {
        //Attribute
        public static int MinHeaderLength = 12;

        public int CSRCCount;
        public byte[] Data;
        public bool Extension;
        public ushort ExtensionHeaderId;
        public ushort ExtensionLengthAsCount;
        public int ExtensionLengthInBytes;
        public int HeaderLength = MinHeaderLength;
        public bool Marker;
        public bool Padding;
        public int PayloadType;
        public ushort SequenceNumber;
        public uint SourceId;
        public uint Timestamp;
        public int Version;

        /// <summary>
        ///     Konstruktor
        /// </summary>
        public RTPPacket()
        {
        }

        /// <summary>
        ///     Konstuktor
        /// </summary>
        /// <param name="_data"></param>
        public RTPPacket(byte[] data)
        {
            Parse(data);
        }

        /// <summary>
        ///     Parse
        /// </summary>
        /// <param name="linearData"></param>
        private void Parse(byte[] data)
        {
            if (data.Length >= MinHeaderLength)
            {
                Version = ValueFromByte(data[0], 6, 2);
                Padding = Convert.ToBoolean(ValueFromByte(data[0], 5, 1));
                Extension = Convert.ToBoolean(ValueFromByte(data[0], 4, 1));
                CSRCCount = ValueFromByte(data[0], 0, 4);
                Marker = Convert.ToBoolean(ValueFromByte(data[1], 7, 1));
                PayloadType = ValueFromByte(data[1], 0, 7);
                HeaderLength = MinHeaderLength + CSRCCount * 4;

                //Sequence Nummer
                var seqNum = new byte[2];
                seqNum[0] = data[3];
                seqNum[1] = data[2];
                SequenceNumber = BitConverter.ToUInt16(seqNum, 0);

                //TimeStamp
                var timeStmp = new byte[4];
                timeStmp[0] = data[7];
                timeStmp[1] = data[6];
                timeStmp[2] = data[5];
                timeStmp[3] = data[4];
                Timestamp = BitConverter.ToUInt32(timeStmp, 0);

                //SourceId
                var srcId = new byte[4];
                srcId[0] = data[8];
                srcId[1] = data[9];
                srcId[2] = data[10];
                srcId[3] = data[11];
                SourceId = BitConverter.ToUInt32(srcId, 0);

                //Wenn Extension Header
                if (Extension)
                {
                    //ExtensionHeaderId
                    var extHeaderId = new byte[2];
                    extHeaderId[1] = data[HeaderLength + 0];
                    extHeaderId[0] = data[HeaderLength + 1];
                    ExtensionHeaderId = BitConverter.ToUInt16(extHeaderId, 0);

                    //ExtensionHeaderLength
                    var extHeaderLength16 = new byte[2];
                    extHeaderLength16[1] = data[HeaderLength + 2];
                    extHeaderLength16[0] = data[HeaderLength + 3];
                    ExtensionLengthAsCount = BitConverter.ToUInt16(extHeaderLength16.ToArray(), 0);

                    //Header Länge anpassen (Länge mal 4 Bytes bzw. Int32)
                    ExtensionLengthInBytes = ExtensionLengthAsCount * 4;
                    HeaderLength += ExtensionLengthInBytes + 4;
                }

                //Daten kopieren
                Data = new byte[data.Length - HeaderLength];
                Array.Copy(data, HeaderLength, Data, 0, data.Length - HeaderLength);
            }
        }

        /// <summary>
        ///     GetValueFromByte
        /// </summary>
        /// <param name="value"></param>
        /// <param name="startPos"></param>
        /// <param name="length"></param>
        /// <returns></returns>
        private int ValueFromByte(byte value, int startPos, int length)
        {
            byte mask = 0;
            //Maske erstellen
            for (var i = 0; i < length; i++)
                mask = (byte) (mask | (0x1 << (startPos + i)));

            //Ergebnis
            var result = (byte) ((value & mask) >> startPos);
            //Fertig
            return Convert.ToInt32(result);
        }

        /// <summary>
        ///     ToBytes
        /// </summary>
        /// <returns></returns>
        public byte[] ToBytes()
        {
            //Ergebnis
            var bytes = new byte[HeaderLength + Data.Length];

            //Byte 0
            bytes[0] = (byte) (Version << 6);
            bytes[0] |= (byte) (Convert.ToInt32(Padding) << 5);
            bytes[0] |= (byte) (Convert.ToInt32(Extension) << 4);
            bytes[0] |= (byte) Convert.ToInt32(CSRCCount);

            //Byte 1
            bytes[1] = (byte) (Convert.ToInt32(Marker) << 7);
            bytes[1] |= (byte) Convert.ToInt32(PayloadType);

            //Byte 2 + 3
            var bytesSequenceNumber = BitConverter.GetBytes(SequenceNumber);
            bytes[2] = bytesSequenceNumber[1];
            bytes[3] = bytesSequenceNumber[0];

            //Byte 4 bis 7
            var bytesTimeStamp = BitConverter.GetBytes(Timestamp);
            bytes[4] = bytesTimeStamp[3];
            bytes[5] = bytesTimeStamp[2];
            bytes[6] = bytesTimeStamp[1];
            bytes[7] = bytesTimeStamp[0];

            //Byte 8 bis 11
            var bytesSourceId = BitConverter.GetBytes(SourceId);
            bytes[8] = bytesSourceId[3];
            bytes[9] = bytesSourceId[2];
            bytes[10] = bytesSourceId[1];
            bytes[11] = bytesSourceId[0];

            //Daten
            Array.Copy(Data, 0, bytes, HeaderLength, Data.Length);

            //Fertig
            return bytes;
        }
    }
}