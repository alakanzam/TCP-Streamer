using System;
using System.Collections.Generic;

namespace WinSound
{
    /// <summary>
    ///     Mixer
    /// </summary>
    public class Mixer
    {
        /// <summary>
        ///     MixBytes
        /// </summary>
        /// <param name="?"></param>
        /// <param name="maxListCount"></param>
        /// <param name="BitsPerSample"></param>
        /// <returns></returns>
        public static List<byte> MixBytes(List<List<byte>> listList, int BitsPerSample)
        {
            //Ergebnis
            var list16 = new List<int>();
            var list16Abs = new List<int>();
            var maximum = 0;

            //Fertig
            return MixBytes_Intern(listList, BitsPerSample, out list16, out list16Abs, out maximum);
        }

        /// <summary>
        ///     MixBytes
        /// </summary>
        /// <param name="listList"></param>
        /// <param name="BitsPerSample"></param>
        /// <param name="listLinear"></param>
        /// <returns></returns>
        public static List<byte> MixBytes(List<List<byte>> listList, int BitsPerSample, out List<int> listLinear,
            out List<int> listLinearAbs, out int maximum)
        {
            //Fertig
            return MixBytes_Intern(listList, BitsPerSample, out listLinear, out listLinearAbs, out maximum);
        }

        /// <summary>
        ///     MixBytes_Intern
        /// </summary>
        /// <param name="listList"></param>
        /// <param name="BitsPerSample"></param>
        /// <param name="listLinear"></param>
        /// <returns></returns>
        private static List<byte> MixBytes_Intern(List<List<byte>> listList, int BitsPerSample,
            out List<int> listLinear, out List<int> listLinearAbs, out int maximum)
        {
            //Defaultwert setzen
            listLinear = new List<int>();
            listLinearAbs = new List<int>();
            maximum = 0;

            //Maximale Anzahl Bytes zum Mischen ermitteln
            var maxBytesCount = 0;
            foreach (var l in listList)
                if (l.Count > maxBytesCount)
                    maxBytesCount = l.Count;

            //Wenn Daten vorhanden
            if (listList.Count > 0 && maxBytesCount > 0)
                switch (BitsPerSample)
                {
                    //8
                    case 8:
                        return MixBytes_8Bit(listList, maxBytesCount, out listLinear, out listLinearAbs, out maximum);

                    //16
                    case 16:
                        return MixBytes_16Bit(listList, maxBytesCount, out listLinear, out listLinearAbs, out maximum);
                }

            //Fehler
            return new List<byte>();
        }

        /// <summary>
        ///     MixBytes_16Bit
        /// </summary>
        /// <param name="listList"></param>
        /// <returns></returns>
        private static List<byte> MixBytes_16Bit(List<List<byte>> listList, int maxBytesCount, out List<int> listLinear,
            out List<int> listLinearAbs, out int maximum)
        {
            //Ergebnis
            maximum = 0;

            //Array mit linearen und Byte Werten erstellen 
            var linearCount = maxBytesCount / 2;
            var bytesLinear = new int[linearCount];
            var bytesLinearAbs = new int[linearCount];
            var bytesRaw = new byte[maxBytesCount];

            //Für jede ByteListe
            for (var v = 0; v < listList.Count; v++)
            {
                //In Array umwandeln
                var bytes = listList[v].ToArray();

                //Für jeden 16Bit Wert
                for (int i = 0, a = 0; i < linearCount; i++, a += 2)
                    //Wenn Werte zum Mischen vorhanden
                    if (i < bytes.Length && a < bytes.Length - 1)
                    {
                        //Wert ermitteln
                        var value16 = BitConverter.ToInt16(bytes, a);
                        var value32 = bytesLinear[i] + value16;

                        //Wert addieren	(Überläufe abfangen)
                        if (value32 < short.MinValue)
                            value32 = short.MinValue;
                        else if (value32 > short.MaxValue)
                            value32 = short.MaxValue;

                        //Werte setzen
                        bytesLinear[i] = value32;
                        bytesLinearAbs[i] = Math.Abs(value32);
                        var mixed16 = Convert.ToInt16(value32);
                        Array.Copy(BitConverter.GetBytes(mixed16), 0, bytesRaw, a, 2);

                        //Maximum berechnen
                        if (value32 > maximum)
                            maximum = value32;
                    }
            }

            //Out Ergebnis
            listLinear = new List<int>(bytesLinear);
            listLinearAbs = new List<int>(bytesLinearAbs);

            //Fertig
            return new List<byte>(bytesRaw);
        }

        /// <summary>
        ///     MixBytes_8Bit
        /// </summary>
        /// <param name="listList"></param>
        /// <param name="maxBytesCount"></param>
        /// <param name="listLinear"></param>
        /// <param name="listLinearAbs"></param>
        /// <param name="maximum"></param>
        /// <returns></returns>
        private static List<byte> MixBytes_8Bit(List<List<byte>> listList, int maxBytesCount, out List<int> listLinear,
            out List<int> listLinearAbs, out int maximum)
        {
            //Ergebnis
            maximum = 0;

            //Array mit linearen und Byte Werten erstellen 
            var linearCount = maxBytesCount;
            var bytesLinear = new int[linearCount];
            var bytesRaw = new byte[maxBytesCount];

            //Für jede ByteListe
            for (var v = 0; v < listList.Count; v++)
            {
                //In Array umwandeln
                var bytes = listList[v].ToArray();

                //Für jeden 8 Bit Wert
                for (var i = 0; i < linearCount; i++)
                    //Wenn Werte zum Mischen vorhanden
                    if (i < bytes.Length)
                    {
                        //Wert ermitteln
                        var value8 = bytes[i];
                        var value32 = bytesLinear[i] + value8;

                        //Wert addieren	(Überläufe abfangen)
                        if (value32 < byte.MinValue)
                            value32 = byte.MinValue;
                        else if (value32 > byte.MaxValue)
                            value32 = byte.MaxValue;

                        //Werte setzen
                        bytesLinear[i] = value32;
                        bytesRaw[i] = BitConverter.GetBytes(value32)[0];

                        //Maximum berechnen
                        if (value32 > maximum)
                            maximum = value32;
                    }
            }

            //Out Ergebnisse
            listLinear = new List<int>(bytesLinear);
            listLinearAbs = new List<int>(bytesLinear);

            //Fertig
            return new List<byte>(bytesRaw);
        }

        /// <summary>
        ///     SubsctractBytes_16Bit
        /// </summary>
        /// <param name="listList"></param>
        /// <param name="maxBytesCount"></param>
        /// <returns></returns>
        public static List<byte> SubsctractBytes_16Bit(List<byte> listSource, List<byte> listToSubstract)
        {
            //Ergebnis
            var list = new List<byte>(listSource.Count);

            //Array mit linearen Werten erstellen (16Bit)
            var value16Count = listSource.Count / 2;
            var list16Mixed = new List<short>(new short[value16Count]);

            //In Array umwandeln
            var bytesSource = listSource.ToArray();
            var bytesSubstract = listToSubstract.ToArray();

            //Für jeden 16Bit Wert
            for (int i = 0, a = 0; i < value16Count; i++, a += 2)
                //Wenn Werte vorhanden
                if (i < bytesSource.Length && a < bytesSource.Length - 1)
                {
                    //Werte ermitteln
                    var value16Source = BitConverter.ToInt16(bytesSource, a);
                    var value16Substract = BitConverter.ToInt16(bytesSubstract, a);
                    var value32 = value16Source - value16Substract;

                    //Wert addieren	(Überläufe abfangen)
                    if (value32 < short.MinValue)
                        value32 = short.MinValue;
                    else if (value32 > short.MaxValue)
                        value32 = short.MaxValue;

                    //Wert setzen
                    list16Mixed[i] = Convert.ToInt16(value32);
                }

            //Für jeden Wert
            foreach (var v16 in list16Mixed)
            {
                //Integer nach Bytes umwandeln
                var bytes = BitConverter.GetBytes(v16);
                list.AddRange(bytes);
            }

            //Fertig
            return list;
        }
    }
}