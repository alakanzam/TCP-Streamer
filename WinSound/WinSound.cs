using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace WinSound
{
    /// <summary>
    ///     LockerClass
    /// </summary>
    internal class LockerClass
    {
    }

    /// <summary>
    ///     WinSound
    /// </summary>
    public class WinSound
    {
        /// <summary>
        ///     Alle Abspielgeräte anzeigen
        /// </summary>
        /// <returns></returns>
        public static List<string> GetPlaybackNames()
        {
            //Ergebnis
            var list = new List<string>();
            var waveOutCap = new Win32.WAVEOUTCAPS();

            //Anzahl Devices
            var num = Win32.waveOutGetNumDevs();
            for (var i = 0; i < num; i++)
            {
                var hr = Win32.waveOutGetDevCaps(i, ref waveOutCap, Marshal.SizeOf(typeof(Win32.WAVEOUTCAPS)));
                if (hr == (int) Win32.HRESULT.S_OK)
                    list.Add(waveOutCap.szPname);
            }

            //Fertig
            return list;
        }

        /// <summary>
        ///     Alle Aufnahmegeräte anzeigen
        /// </summary>
        /// <returns></returns>
        public static List<string> GetRecordingNames()
        {
            //Ergebnis
            var list = new List<string>();
            var waveInCap = new Win32.WAVEINCAPS();

            //Anzahl Devices
            var num = Win32.waveInGetNumDevs();
            for (var i = 0; i < num; i++)
            {
                var hr = Win32.waveInGetDevCaps(i, ref waveInCap, Marshal.SizeOf(typeof(Win32.WAVEINCAPS)));
                if (hr == (int) Win32.HRESULT.S_OK)
                    list.Add(waveInCap.szPname);
            }

            //Fertig
            return list;
        }

        /// <summary>
        ///     GetWaveInDeviceIdByName
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static int GetWaveInDeviceIdByName(string name)
        {
            //Anzahl Devices
            var num = Win32.waveInGetNumDevs();

            //WaveIn Struktur
            var caps = new Win32.WAVEINCAPS();
            for (var i = 0; i < num; i++)
            {
                var hr = (Win32.HRESULT) Win32.waveInGetDevCaps(i, ref caps, Marshal.SizeOf(typeof(Win32.WAVEINCAPS)));
                if (hr == Win32.HRESULT.S_OK)
                    if (caps.szPname == name)
                        return i;
            }

            //Nicht gefunden
            return Win32.WAVE_MAPPER;
        }

        /// <summary>
        ///     GetWaveOutDeviceIdByName
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static int GetWaveOutDeviceIdByName(string name)
        {
            //Anzahl Devices
            var num = Win32.waveOutGetNumDevs();

            //WaveIn Struktur
            var caps = new Win32.WAVEOUTCAPS();
            for (var i = 0; i < num; i++)
            {
                var hr = (Win32.HRESULT) Win32.waveOutGetDevCaps(i, ref caps,
                    Marshal.SizeOf(typeof(Win32.WAVEOUTCAPS)));
                if (hr == Win32.HRESULT.S_OK)
                    if (caps.szPname == name)
                        return i;
            }

            //Nicht gefunden
            return Win32.WAVE_MAPPER;
        }

        /// <summary>
        ///     FlagToString
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public static string FlagToString(Win32.WaveHdrFlags flag)
        {
            var sb = new StringBuilder();

            if ((flag & Win32.WaveHdrFlags.WHDR_PREPARED) > 0) sb.Append("PREPARED ");
            if ((flag & Win32.WaveHdrFlags.WHDR_BEGINLOOP) > 0) sb.Append("BEGINLOOP ");
            if ((flag & Win32.WaveHdrFlags.WHDR_ENDLOOP) > 0) sb.Append("ENDLOOP ");
            if ((flag & Win32.WaveHdrFlags.WHDR_INQUEUE) > 0) sb.Append("INQUEUE ");
            if ((flag & Win32.WaveHdrFlags.WHDR_DONE) > 0) sb.Append("DONE ");

            return sb.ToString();
        }
    }
}