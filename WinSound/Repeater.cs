using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinSound
{
    public unsafe class Repeater
    {
        //Delegates bzw. Events
        public delegate void DelegateStopped();

        private readonly AutoResetEvent AutoResetEventDataRecorded = new AutoResetEvent(false);
        private readonly AutoResetEvent AutoResetEventThreadPlayWaveInEnd = new AutoResetEvent(false);
        private readonly Win32.DelegateWaveInProc delegateWaveInProc;
        private readonly Win32.DelegateWaveOutProc delegateWaveOutProc;

        //Attribute
        private readonly LockerClass Locker = new LockerClass();

        private int BitsPerSample = 16;
        private int BufferCount = 8;
        private int BufferSize = 1024;
        private int Channels = 1;
        private byte[] CopyDataBuffer;
        private Win32.WAVEHDR* CurrentRecordedHeader;
        private GCHandle GCCopyDataBuffer;
        private IntPtr hWaveIn = IntPtr.Zero;
        private IntPtr hWaveOut = IntPtr.Zero;
        private bool IsDataIncomming;
        private bool IsThreadPlayWaveInRunning;
        private bool IsWaveInOpened;
        private bool IsWaveInStarted;
        private bool IsWaveOutOpened;

        private LockerClass LockerCopy = new LockerClass();
        private int SamplesPerSecond = 8000;
        private bool Stopped;
        private Thread ThreadPlayWaveIn;
        private string WaveInDeviceName = "";
        private Win32.WAVEHDR*[] WaveInHeaders;
        private string WaveOutDeviceName = "";
        private Win32.WAVEHDR*[] WaveOutHeaders;

        /// <summary>
        ///     Konstruktor
        /// </summary>
        public Repeater()
        {
            delegateWaveInProc = waveInProc;
            delegateWaveOutProc = waveOutProc;
        }

        /// <summary>
        ///     Started
        /// </summary>
        public bool Started => IsWaveInStarted && IsWaveInOpened && IsWaveOutOpened && IsThreadPlayWaveInRunning;

        /// <summary>
        ///     IsMute
        /// </summary>
        public bool Mute { get; set; } = false;

        public event DelegateStopped RepeaterStopped;

        /// <summary>
        ///     CreateWaveInHeaders
        /// </summary>
        /// <param name="count"></param>
        /// <param name="bufferSize"></param>
        /// <returns></returns>
        private bool CreateWaveInHeaders()
        {
            //Buffer anlegen
            WaveInHeaders = new Win32.WAVEHDR*[BufferCount];
            var createdHeaders = 0;

            //Für jeden Buffer
            for (var i = 0; i < BufferCount; i++)
            {
                //Header allokieren
                WaveInHeaders[i] = (Win32.WAVEHDR*) Marshal.AllocHGlobal(sizeof(Win32.WAVEHDR));

                //Header setzen
                WaveInHeaders[i]->dwLoops = 0;
                WaveInHeaders[i]->dwUser = IntPtr.Zero;
                WaveInHeaders[i]->lpNext = IntPtr.Zero;
                WaveInHeaders[i]->reserved = IntPtr.Zero;
                WaveInHeaders[i]->lpData = Marshal.AllocHGlobal(BufferSize);
                WaveInHeaders[i]->dwBufferLength = (uint) BufferSize;
                WaveInHeaders[i]->dwBytesRecorded = 0;
                WaveInHeaders[i]->dwFlags = 0;

                //Wenn der Buffer vorbereitet werden konnte
                var hr = Win32.waveInPrepareHeader(hWaveIn, WaveInHeaders[i], sizeof(Win32.WAVEHDR));
                if (hr == Win32.MMRESULT.MMSYSERR_NOERROR)
                {
                    //Ersten Header zur Aufnahme hinzufügen
                    if (i == 0)
                        hr = Win32.waveInAddBuffer(hWaveIn, WaveInHeaders[i], sizeof(Win32.WAVEHDR));
                    createdHeaders++;
                }
            }

            //Fertig
            return createdHeaders == BufferCount;
        }

        /// <summary>
        ///     CreateWaveOutHeaders
        /// </summary>
        /// <returns></returns>
        private bool CreateWaveOutHeaders()
        {
            //Buffer anlegen
            WaveOutHeaders = new Win32.WAVEHDR*[BufferCount];
            var createdHeaders = 0;

            //Für jeden Buffer
            for (var i = 0; i < BufferCount; i++)
            {
                //Header allokieren
                WaveOutHeaders[i] = (Win32.WAVEHDR*) Marshal.AllocHGlobal(sizeof(Win32.WAVEHDR));

                //Header setzen
                WaveOutHeaders[i]->dwLoops = 0;
                WaveOutHeaders[i]->dwUser = IntPtr.Zero;
                WaveOutHeaders[i]->lpNext = IntPtr.Zero;
                WaveOutHeaders[i]->reserved = IntPtr.Zero;
                WaveOutHeaders[i]->lpData = Marshal.AllocHGlobal(BufferSize);
                WaveOutHeaders[i]->dwBufferLength = (uint) BufferSize;
                WaveOutHeaders[i]->dwBytesRecorded = 0;
                WaveOutHeaders[i]->dwFlags = 0;

                //Wenn der Buffer vorbereitet werden konnte
                var hr = Win32.waveOutPrepareHeader(hWaveOut, WaveOutHeaders[i], sizeof(Win32.WAVEHDR));
                if (hr == Win32.MMRESULT.MMSYSERR_NOERROR)
                    createdHeaders++;
            }

            //Fertig
            return createdHeaders == BufferCount;
        }

        /// <summary>
        ///     FreeWaveInHeaders
        /// </summary>
        private void FreeWaveInHeaders()
        {
            try
            {
                if (WaveInHeaders != null)
                    for (var i = 0; i < WaveInHeaders.Length; i++)
                    {
                        //Handle freigeben
                        var hr = Win32.waveInUnprepareHeader(hWaveIn, WaveInHeaders[i], sizeof(Win32.WAVEHDR));

                        //Warten bis fertig
                        var count = 0;
                        while (count <= 100 && (WaveInHeaders[i]->dwFlags & Win32.WaveHdrFlags.WHDR_INQUEUE) ==
                               Win32.WaveHdrFlags.WHDR_INQUEUE)
                        {
                            Thread.Sleep(20);
                            count++;
                        }

                        //Wenn Daten nicht mehr in Queue
                        if ((WaveInHeaders[i]->dwFlags & Win32.WaveHdrFlags.WHDR_INQUEUE) !=
                            Win32.WaveHdrFlags.WHDR_INQUEUE)
                            if (WaveInHeaders[i]->lpData != IntPtr.Zero)
                            {
                                Marshal.FreeHGlobal(WaveInHeaders[i]->lpData);
                                WaveInHeaders[i]->lpData = IntPtr.Zero;
                            }
                    }
            }
            catch (Exception ex)
            {
                Debug.Write(ex.Message);
            }
        }

        /// <summary>
        ///     FreeWaveOutHeaders
        /// </summary>
        private void FreeWaveOutHeaders()
        {
            try
            {
                if (WaveOutHeaders != null)
                    for (var i = 0; i < WaveOutHeaders.Length; i++)
                    {
                        //Handles freigeben
                        var hr = Win32.waveOutUnprepareHeader(hWaveOut, WaveOutHeaders[i], sizeof(Win32.WAVEHDR));

                        //Warten bis fertig abgespielt
                        var count = 0;
                        while (count <= 100 && (WaveOutHeaders[i]->dwFlags & Win32.WaveHdrFlags.WHDR_INQUEUE) ==
                               Win32.WaveHdrFlags.WHDR_INQUEUE)
                        {
                            Thread.Sleep(20);
                            count++;
                        }

                        //Wenn Daten abgespielt  
                        if ((WaveOutHeaders[i]->dwFlags & Win32.WaveHdrFlags.WHDR_INQUEUE) !=
                            Win32.WaveHdrFlags.WHDR_INQUEUE)
                            if (WaveOutHeaders[i]->lpData != IntPtr.Zero)
                            {
                                Marshal.FreeHGlobal(WaveOutHeaders[i]->lpData);
                                WaveOutHeaders[i]->lpData = IntPtr.Zero;
                            }
                    }
            }
            catch (Exception ex)
            {
                Debug.Write(ex.Message);
            }
        }

        /// <summary>
        ///     StartThreadRecording
        /// </summary>
        private void StartThreadPlayWaveIn()
        {
            if (Started == false)
            {
                ThreadPlayWaveIn = new Thread(OnThreadPlayWaveIn);
                IsThreadPlayWaveInRunning = true;
                ThreadPlayWaveIn.Name = "PlayWaveIn";
                ThreadPlayWaveIn.Priority = ThreadPriority.Highest;
                ThreadPlayWaveIn.Start();
            }
        }

        /// <summary>
        ///     OpenWaveIn
        /// </summary>
        /// <returns></returns>
        private bool OpenWaveIn()
        {
            if (hWaveIn == IntPtr.Zero)
                if (IsWaveInOpened == false)
                {
                    //Format bestimmen
                    var waveFormatEx = new Win32.WAVEFORMATEX();
                    waveFormatEx.wFormatTag = (ushort) Win32.WaveFormatFlags.WAVE_FORMAT_PCM;
                    waveFormatEx.nChannels = (ushort) Channels;
                    waveFormatEx.nSamplesPerSec = (ushort) SamplesPerSecond;
                    waveFormatEx.wBitsPerSample = (ushort) BitsPerSample;
                    waveFormatEx.nBlockAlign = (ushort) ((waveFormatEx.wBitsPerSample * waveFormatEx.nChannels) >> 3);
                    waveFormatEx.nAvgBytesPerSec = waveFormatEx.nBlockAlign * waveFormatEx.nSamplesPerSec;

                    //WaveIn Gerät ermitteln
                    var deviceId = WinSound.GetWaveInDeviceIdByName(WaveInDeviceName);
                    //WaveIn Gerät öffnen
                    var hr = Win32.waveInOpen(ref hWaveIn, deviceId, ref waveFormatEx, delegateWaveInProc, 0,
                        (int) Win32.WaveProcFlags.CALLBACK_FUNCTION);

                    //Wenn nicht erfolgreich
                    if (hWaveIn == IntPtr.Zero)
                    {
                        IsWaveInOpened = false;
                        return false;
                    }

                    //Handle sperren
                    GCHandle.Alloc(hWaveIn, GCHandleType.Pinned);
                }

            IsWaveInOpened = true;
            return true;
        }

        /// <summary>
        ///     OpenWaveOuz
        /// </summary>
        /// <returns></returns>
        private bool OpenWaveOut()
        {
            if (hWaveOut == IntPtr.Zero)
                if (IsWaveOutOpened == false)
                {
                    //Format bestimmen
                    var waveFormatEx = new Win32.WAVEFORMATEX();
                    waveFormatEx.wFormatTag = (ushort) Win32.WaveFormatFlags.WAVE_FORMAT_PCM;
                    waveFormatEx.nChannels = (ushort) Channels;
                    waveFormatEx.nSamplesPerSec = (ushort) SamplesPerSecond;
                    waveFormatEx.wBitsPerSample = (ushort) BitsPerSample;
                    waveFormatEx.nBlockAlign = (ushort) ((waveFormatEx.wBitsPerSample * waveFormatEx.nChannels) >> 3);
                    waveFormatEx.nAvgBytesPerSec = waveFormatEx.nBlockAlign * waveFormatEx.nSamplesPerSec;

                    //WaveOut Gerät ermitteln
                    var deviceId = WinSound.GetWaveOutDeviceIdByName(WaveOutDeviceName);
                    //WaveIn Gerät öffnen
                    var hr = Win32.waveOutOpen(ref hWaveOut, deviceId, ref waveFormatEx, delegateWaveOutProc, 0,
                        (int) Win32.WaveProcFlags.CALLBACK_FUNCTION);

                    //Wenn nicht erfolgreich
                    if (hr != Win32.MMRESULT.MMSYSERR_NOERROR)
                    {
                        IsWaveOutOpened = false;
                        return false;
                    }

                    //Handle sperren
                    GCHandle.Alloc(hWaveOut, GCHandleType.Pinned);
                }

            IsWaveOutOpened = true;
            return true;
        }

        /// <summary>
        ///     Start
        /// </summary>
        /// <param name="waveInDeviceName"></param>
        /// <param name="waveOutDeviceName"></param>
        /// <param name="samplesPerSecond"></param>
        /// <param name="bitsPerSample"></param>
        /// <param name="channels"></param>
        /// <returns></returns>
        public bool Start(string waveInDeviceName, string waveOutDeviceName, int samplesPerSecond, int bitsPerSample,
            int channels, int bufferCount, int bufferSize)
        {
            try
            {
                lock (Locker)
                {
                    //Wenn der Thread noch läuft
                    if (IsThreadPlayWaveInRunning)
                    {
                        //Warten bis Thread beendet ist
                        IsThreadPlayWaveInRunning = false;
                        AutoResetEventDataRecorded.Set();
                        AutoResetEventThreadPlayWaveInEnd.WaitOne(5000);
                    }

                    //Wenn nicht schon gestartet
                    if (Started == false)
                    {
                        //Daten übernehmen
                        WaveInDeviceName = waveInDeviceName;
                        WaveOutDeviceName = waveOutDeviceName;
                        SamplesPerSecond = samplesPerSecond;
                        BitsPerSample = bitsPerSample;
                        Channels = channels;
                        BufferCount = bufferCount;
                        BufferSize = bufferSize;
                        CopyDataBuffer = new byte[BufferSize];
                        GCCopyDataBuffer = GCHandle.Alloc(CopyDataBuffer, GCHandleType.Pinned);

                        //WaveOut
                        if (StartWaveOut())
                            return StartWaveIn();
                        //Fehler beim Öffnen von WaveOut
                        return false;
                    }

                    //Repeater läuft bereits
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Start | {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        ///     ChangeWaveIn
        /// </summary>
        /// <param name="waveInDeviceName"></param>
        /// <returns></returns>
        public bool ChangeWaveIn(string waveInDeviceName)
        {
            try
            {
                //Ändern
                WaveInDeviceName = waveInDeviceName;

                //Neustart
                if (Started)
                {
                    CloseWaveIn();
                    return StartWaveIn();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("ChangeWaveIn() | {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        ///     ChangeWaveOut
        /// </summary>
        /// <param name="waveOutDeviceName"></param>
        /// <returns></returns>
        public bool ChangeWaveOut(string waveOutDeviceName)
        {
            try
            {
                //Ändern
                WaveOutDeviceName = waveOutDeviceName;
                //Neustart
                if (Started)
                {
                    CloseWaveOut();
                    return StartWaveOut();
                }
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("ChangeWaveOut() | {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        ///     StartWaveIn
        /// </summary>
        /// <returns></returns>
        private bool StartWaveIn()
        {
            //Wenn WaveIn werden konnte
            if (OpenWaveIn())
                if (CreateWaveInHeaders())
                {
                    //Wenn die Aufnahme gestartet werden konnte
                    var hr = Win32.waveInStart(hWaveIn);
                    if (hr == Win32.MMRESULT.MMSYSERR_NOERROR)
                    {
                        IsWaveInStarted = true;
                        Stopped = false;
                        //Thread starten
                        StartThreadPlayWaveIn();
                        return true;
                    }
                    //Fehler beim Starten
                    return false;
                }
            //WaveIn konnte nicht geöffnet werden
            return false;
        }

        /// <summary>
        ///     OpenWaveOut
        /// </summary>
        /// <returns></returns>
        private bool StartWaveOut()
        {
            if (OpenWaveOut())
                return CreateWaveOutHeaders();
            return false;
        }

        /// <summary>
        ///     Stop
        /// </summary>
        /// <returns></returns>
        public bool Stop()
        {
            try
            {
                lock (Locker)
                {
                    //Wenn gestartet
                    if (GCCopyDataBuffer.IsAllocated)
                    {
                        //WaveIn beenden
                        CloseWaveIn();
                        //WaveOut beenden
                        CloseWaveOut();
                        //Speicher freigeben
                        GCCopyDataBuffer.Free();
                        //Fertig
                        return true;
                    }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(string.Format("Stop | {0}", ex.Message));
                return false;
            }
        }

        /// <summary>
        ///     CloseWaveIn
        /// </summary>
        /// <returns></returns>
        private void CloseWaveIn()
        {
            //Als manuell beendet setzen
            Stopped = true;
            IsThreadPlayWaveInRunning = false;
            AutoResetEventDataRecorded.Set();

            //WaveIn stoppen
            var hResult = Win32.waveInStop(hWaveIn);

            //Buffer als abgearbeitet setzen
            var resetCount = 0;
            while (IsAnyWaveInHeaderInState(Win32.WaveHdrFlags.WHDR_INQUEUE) & (resetCount < 20))
            {
                var hr = Win32.waveInReset(hWaveIn);
                Thread.Sleep(50);
                resetCount++;
            }

            //Header Handles freigeben (vor waveInClose)
            FreeWaveInHeaders();

            //Schliessen
            while (Win32.waveInClose(hWaveIn) == Win32.MMRESULT.WAVERR_STILLPLAYING)
                Thread.Sleep(50);
        }

        /// <summary>
        ///     CloseWaveOut
        /// </summary>
        /// <returns></returns>
        private void CloseWaveOut()
        {
            //Anhalten
            IsWaveOutOpened = false;
            var hr = Win32.waveOutReset(hWaveOut);

            //Warten bis alles abgespielt
            while (IsAnyWaveOutHeaderInState(Win32.WaveHdrFlags.WHDR_INQUEUE))
                Thread.Sleep(50);

            //Header Handles freigeben
            FreeWaveOutHeaders();
            //Schliessen
            hr = Win32.waveOutClose(hWaveOut);
        }

        /// <summary>
        ///     IsAnyWaveInHeaderInState
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        private bool IsAnyWaveInHeaderInState(Win32.WaveHdrFlags state)
        {
            for (var i = 0; i < WaveInHeaders.Length; i++)
                if ((WaveInHeaders[i]->dwFlags & state) == state)
                    return true;
            return false;
        }

        /// <summary>
        ///     IsAnyWaveOutHeaderInState
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        private bool IsAnyWaveOutHeaderInState(Win32.WaveHdrFlags state)
        {
            for (var i = 0; i < WaveOutHeaders.Length; i++)
                if ((WaveOutHeaders[i]->dwFlags & state) == state)
                    return true;
            return false;
        }

        /// <summary>
        ///     waveOutProc
        /// </summary>
        /// <param name="hWaveOut"></param>
        /// <param name="msg"></param>
        /// <param name="dwInstance"></param>
        /// <param name="wParam"></param>
        /// <param name="lParam"></param>
        private void waveOutProc(IntPtr hWaveOut, Win32.WOM_Messages msg, IntPtr dwInstance, Win32.WAVEHDR* pWaveHeader,
            IntPtr lParam)
        {
            switch (msg)
            {
                //Open
                case Win32.WOM_Messages.OPEN:
                    break;

                //Close
                case Win32.WOM_Messages.CLOSE:
                    IsWaveOutOpened = false;
                    AutoResetEventDataRecorded.Set();
                    this.hWaveOut = IntPtr.Zero;
                    break;
            }
        }

        /// <summary>
        ///     waveInProc
        /// </summary>
        /// <param name="hWaveIn"></param>
        /// <param name="msg"></param>
        /// <param name="dwInstance"></param>
        /// <param name="wParam"></param>
        /// <param name="lParam"></param>
        private void waveInProc(IntPtr hWaveIn, Win32.WIM_Messages msg, IntPtr dwInstance, Win32.WAVEHDR* waveHeader,
            IntPtr lParam)
        {
            switch (msg)
            {
                //Open
                case Win32.WIM_Messages.OPEN:
                    break;

                //Data
                case Win32.WIM_Messages.DATA:
                    //Daten sind angekommen
                    IsDataIncomming = true;
                    //Aufgenommenen Buffer merken
                    CurrentRecordedHeader = waveHeader;
                    //Event setzen
                    AutoResetEventDataRecorded.Set();
                    break;

                //Close
                case Win32.WIM_Messages.CLOSE:
                    IsDataIncomming = false;
                    IsWaveInOpened = false;
                    Stopped = true;
                    AutoResetEventDataRecorded.Set();
                    this.hWaveIn = IntPtr.Zero;
                    break;
            }
        }

        /// <summary>
        ///     OnThreadRecording
        /// </summary>
        private void OnThreadPlayWaveIn()
        {
            while (IsThreadPlayWaveInRunning && !Stopped)
            {
                //Warten bis Aufnahme beendet
                AutoResetEventDataRecorded.WaitOne();

                try
                {
                    if (IsThreadPlayWaveInRunning && IsDataIncomming && IsWaveOutOpened && Mute == false)
                        for (var i = 0; i < WaveOutHeaders.Length; i++)
                            if ((WaveOutHeaders[i]->dwFlags & Win32.WaveHdrFlags.WHDR_INQUEUE) == 0)
                                try
                                {
                                    //Daten in Abspielbuffer kopieren
                                    Marshal.Copy(CurrentRecordedHeader->lpData, CopyDataBuffer, 0,
                                        CopyDataBuffer.Length);
                                    Marshal.Copy(CopyDataBuffer, 0, WaveOutHeaders[i]->lpData, CopyDataBuffer.Length);
                                    //Daten Abspielen
                                    var hr = Win32.waveOutWrite(hWaveOut, WaveOutHeaders[i], sizeof(Win32.WAVEHDR));
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(ex.Message);
                                }

                    if (IsThreadPlayWaveInRunning && !Stopped)
                        for (var i = 0; i < WaveInHeaders.Length; i++)
                            if ((WaveInHeaders[i]->dwFlags & Win32.WaveHdrFlags.WHDR_INQUEUE) == 0)
                            {
                                var hr = Win32.waveInAddBuffer(hWaveIn, WaveInHeaders[i], sizeof(Win32.WAVEHDR));
                            }

                    ////Playing
                    //StringBuilder play = new StringBuilder();
                    //play.AppendLine("");
                    //play.AppendLine("Playing:");
                    //for (int i = 0; i < WaveOutHeaders.Length; i++)
                    //{
                    //    play.AppendLine(String.Format("{0} {1}", i, WinSound.FlagToString(WaveOutHeaders[i]->dwFlags)));
                    //}
                    //play.AppendLine("");
                    //System.Diagnostics.Debug.WriteLine(play.ToString());

                    ////Recording
                    //StringBuilder rec = new StringBuilder();
                    //rec.AppendLine("");
                    //rec.AppendLine("Recording:");
                    //for (int i = 0; i < WaveInHeaders.Length; i++)
                    //{
                    //    rec.AppendLine(String.Format("{0} {1}", i, WinSound.FlagToString(WaveInHeaders[i]->dwFlags)));

                    //}
                    //rec.AppendLine("");
                    //System.Diagnostics.Debug.WriteLine(rec.ToString());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }


            //Variablen setzen
            IsWaveInStarted = false;
            IsThreadPlayWaveInRunning = false;
            AutoResetEventThreadPlayWaveInEnd.Set();

            //Ereignis aussenden
            if (RepeaterStopped != null)
                try
                {
                    RepeaterStopped();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format("Repeater Stopped | {0}", ex.Message));
                }
        }
    }
}