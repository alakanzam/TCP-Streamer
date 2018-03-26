using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace WinSound
{
    public unsafe class Recorder
    {
        public delegate void DelegateDataRecorded(byte[] bytes);

        //Delegates bzw. Events
        public delegate void DelegateStopped();

        private readonly AutoResetEvent AutoResetEventDataRecorded = new AutoResetEvent(false);
        private readonly Win32.DelegateWaveInProc delegateWaveInProc;

        //Attribute
        private readonly LockerClass Locker = new LockerClass();

        private int BitsPerSample = 16;
        private int BufferCount = 8;
        private int BufferSize = 1024;
        private int Channels = 1;
        private Win32.WAVEHDR* CurrentRecordedHeader;
        private IntPtr hWaveIn = IntPtr.Zero;
        private bool IsDataIncomming;
        private bool IsThreadRecordingRunning;
        private bool IsWaveInOpened;
        private bool IsWaveInStarted;

        private LockerClass LockerCopy = new LockerClass();
        private int SamplesPerSecond = 8000;
        private bool Stopped;
        private Thread ThreadRecording;
        private string WaveInDeviceName = "";
        private Win32.WAVEHDR*[] WaveInHeaders;

        /// <summary>
        ///     Konstruktor
        /// </summary>
        public Recorder()
        {
            delegateWaveInProc = waveInProc;
        }

        /// <summary>
        ///     Started
        /// </summary>
        public bool Started => IsWaveInStarted && IsWaveInOpened && IsThreadRecordingRunning;

        public event DelegateStopped RecordingStopped;
        public event DelegateDataRecorded DataRecorded;

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
        ///     StartThreadRecording
        /// </summary>
        private void StartThreadRecording()
        {
            if (Started == false)
            {
                ThreadRecording = new Thread(OnThreadRecording);
                IsThreadRecordingRunning = true;
                ThreadRecording.Name = "Recording";
                ThreadRecording.Priority = ThreadPriority.Highest;
                ThreadRecording.Start();
            }
        }

        /// <summary>
        ///     StartWaveIn
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
        ///     Start
        /// </summary>
        /// <param name="waveInDeviceName"></param>
        /// <param name="waveOutDeviceName"></param>
        /// <param name="samplesPerSecond"></param>
        /// <param name="bitsPerSample"></param>
        /// <param name="channels"></param>
        /// <returns></returns>
        public bool Start(string waveInDeviceName, int samplesPerSecond, int bitsPerSample, int channels,
            int bufferCount, int bufferSize)
        {
            try
            {
                lock (Locker)
                {
                    //Wenn nicht schon gestartet
                    if (Started == false)
                    {
                        //Daten übernehmen
                        WaveInDeviceName = waveInDeviceName;
                        SamplesPerSecond = samplesPerSecond;
                        BitsPerSample = bitsPerSample;
                        Channels = channels;
                        BufferCount = bufferCount;
                        BufferSize = bufferSize;

                        //Wenn WaveIn geöffnet werden konnte
                        if (OpenWaveIn())
                            if (CreateWaveInHeaders())
                            {
                                //Wenn die Aufnahme gestartet werden konnte
                                var hr = Win32.waveInStart(hWaveIn);
                                if (hr == Win32.MMRESULT.MMSYSERR_NOERROR)
                                {
                                    IsWaveInStarted = true;
                                    //Thread starten
                                    StartThreadRecording();
                                    Stopped = false;
                                    return true;
                                }
                                //Fehler beim Starten
                                return false;
                            }
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
                    if (Started)
                    {
                        //Als manuel beendet setzen
                        Stopped = true;
                        IsThreadRecordingRunning = false;

                        //WaveIn schliessen
                        CloseWaveIn();

                        //Variablen setzen
                        AutoResetEventDataRecorded.Set();
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
            //Buffer als abgearbeitet setzen
            var hr = Win32.waveInStop(hWaveIn);

            var resetCount = 0;
            while (IsAnyWaveInHeaderInState(Win32.WaveHdrFlags.WHDR_INQUEUE) & (resetCount < 20))
            {
                hr = Win32.waveInReset(hWaveIn);
                Thread.Sleep(50);
                resetCount++;
            }

            //Header Handles freigeben (vor waveInClose)
            FreeWaveInHeaders();
            //Schliessen
            hr = Win32.waveInClose(hWaveIn);
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
        ///     waveInProc
        /// </summary>
        /// <param name="hWaveIn"></param>
        /// <param name="msg"></param>
        /// <param name="dwInstance"></param>
        /// <param name="wParam"></param>
        /// <param name="lParam"></param>
        private void waveInProc(IntPtr hWaveIn, Win32.WIM_Messages msg, IntPtr dwInstance, Win32.WAVEHDR* pWaveHdr,
            IntPtr lParam)
        {
            switch (msg)
            {
                //Open
                case Win32.WIM_Messages.OPEN:
                    break;

                //Data
                case Win32.WIM_Messages.DATA:
                    //Ankommende Daten vermerken
                    IsDataIncomming = true;
                    //Aufgenommenen Buffer merken
                    CurrentRecordedHeader = pWaveHdr;
                    //Event setzen
                    AutoResetEventDataRecorded.Set();
                    break;

                //Close
                case Win32.WIM_Messages.CLOSE:
                    IsDataIncomming = false;
                    IsWaveInOpened = false;
                    AutoResetEventDataRecorded.Set();
                    this.hWaveIn = IntPtr.Zero;
                    break;
            }
        }

        /// <summary>
        ///     OnThreadRecording
        /// </summary>
        private void OnThreadRecording()
        {
            while (Started && !Stopped)
            {
                //Warten bis Aufnahme beendet
                AutoResetEventDataRecorded.WaitOne();

                try
                {
                    //Wenn aktiv
                    if (Started && !Stopped)
                        if (CurrentRecordedHeader->dwBytesRecorded > 0)
                        {
                            //Wenn Daten abgefragt werden
                            if (DataRecorded != null && IsDataIncomming)
                                try
                                {
                                    //Daten kopieren
                                    var bytes = new byte[CurrentRecordedHeader->dwBytesRecorded];
                                    Marshal.Copy(CurrentRecordedHeader->lpData, bytes, 0,
                                        (int) CurrentRecordedHeader->dwBytesRecorded);

                                    //Event abschicken
                                    DataRecorded(bytes);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine(
                                        string.Format("Recorder.cs | OnThreadRecording() | {0}", ex.Message));
                                }

                            //Weiter Aufnehmen
                            for (var i = 0; i < WaveInHeaders.Length; i++)
                                if ((WaveInHeaders[i]->dwFlags & Win32.WaveHdrFlags.WHDR_INQUEUE) == 0)
                                {
                                    var hr = Win32.waveInAddBuffer(hWaveIn, WaveInHeaders[i], sizeof(Win32.WAVEHDR));
                                }
                        }

                    ////Recording
                    //StringBuilder rec = new StringBuilder();
                    //rec.AppendLine("");
                    //rec.AppendLine("Recording:");
                    //for (int i = 0; i < WaveInHeaders.Length; i++)
                    //{
                    //  rec.AppendLine(String.Format("{0} {1}", i, WinSound.FlagToString(WaveInHeaders[i].dwFlags)));

                    //}
                    //rec.AppendLine("");
                    //System.Diagnostics.Debug.WriteLine(rec.ToString());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }


            lock (Locker)
            {
                //Variablen setzen
                IsWaveInStarted = false;
                IsThreadRecordingRunning = false;
            }

            //Ereignis aussenden
            if (RecordingStopped != null)
                try
                {
                    RecordingStopped();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(string.Format("Recording Stopped | {0}", ex.Message));
                }
        }
    }
}