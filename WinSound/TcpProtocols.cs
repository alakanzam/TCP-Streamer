using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WinSound
{
    /// <summary>
    ///     ProtocolTypes
    /// </summary>
    public enum ProtocolTypes
    {
        LH
    }

    /// <summary>
    ///     Protocol
    /// </summary>
    public class Protocol
    {
        //Delegates bzw. Events
        public delegate void DelegateDataComplete(object sender, byte[] data);

        public delegate void DelegateExceptionAppeared(object sender, Exception ex);

        private const int m_MaxBufferLength = 10000;

        //Attribute
        private readonly List<byte> m_DataBuffer = new List<byte>();

        private Encoding m_Encoding = Encoding.Default;
        public object m_LockerReceive = new object();
        private ProtocolTypes m_ProtocolType = ProtocolTypes.LH;

        /// <summary>
        ///     Konstruktor
        /// </summary>
        /// <param name="type"></param>
        public Protocol(ProtocolTypes type, Encoding encoding)
        {
            m_ProtocolType = type;
            m_Encoding = encoding;
        }

        public event DelegateDataComplete DataComplete;
        public event DelegateExceptionAppeared ExceptionAppeared;


        /// <summary>
        ///     ToBytes
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public byte[] ToBytes(byte[] data)
        {
            try
            {
                //Bytes Länge
                var bytesLength = BitConverter.GetBytes(data.Length);

                //Alles zusammenfassen
                var allBytes = new byte[bytesLength.Length + data.Length];
                Array.Copy(bytesLength, allBytes, bytesLength.Length);
                Array.Copy(data, 0, allBytes, bytesLength.Length, data.Length);

                //Fertig
                return allBytes;
            }
            catch (Exception ex)
            {
                ExceptionAppeared(null, ex);
            }

            //Fehler
            return data;
        }

        /// <summary>
        ///     Receive_LH_STX_ETX
        /// </summary>
        /// <param name="data"></param>
        public void Receive_LH(object sender, byte[] data)
        {
            lock (m_LockerReceive)
            {
                try
                {
                    //Daten an Puffer anhängen
                    m_DataBuffer.AddRange(data);

                    //Pufferüberlauf verhindern
                    if (m_DataBuffer.Count > m_MaxBufferLength)
                        m_DataBuffer.Clear();

                    //Bytes auslesen
                    var bytes = m_DataBuffer.Take(4).ToArray();
                    //Länge ermitteln
                    var length = BitConverter.ToInt32(bytes.ToArray(), 0);

                    //Maximale Länge sicherstellen
                    if (length > m_MaxBufferLength)
                        m_DataBuffer.Clear();

                    //So lange wie Daten vorhanden sind
                    while (m_DataBuffer.Count >= length + 4)
                    {
                        //Daten extrahieren
                        var message = m_DataBuffer.Skip(4).Take(length).ToArray();

                        //Benachrichtigung über vollständige Daten
                        if (DataComplete != null)
                            DataComplete(sender, message);
                        //Daten aus Puffer entfernen
                        m_DataBuffer.RemoveRange(0, length + 4);

                        //Wenn weitere Daten vorhanden
                        if (m_DataBuffer.Count > 4)
                        {
                            //Neue Länge berechnen
                            bytes = m_DataBuffer.Take(4).ToArray();
                            length = BitConverter.ToInt32(bytes.ToArray(), 0);
                        }
                    }
                }
                catch (Exception ex)
                {
                    //Puffer leeren
                    m_DataBuffer.Clear();
                    ExceptionAppeared(null, ex);
                }
            }
        }
    }
}