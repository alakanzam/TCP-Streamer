using System;
using System.Runtime.InteropServices;

namespace WinSound
{
    /// <summary>
    ///     QueueTimer
    /// </summary>
    public class QueueTimer
    {
        public delegate void DelegateTimerTick();

        //Delegates bzw. Events
        private readonly Win32.DelegateTimerProc m_DelegateTimerProc;

        private GCHandle m_GCHandleTimer;
        private GCHandle m_GCHandleTimerQueue;
        private IntPtr m_HandleTimer = IntPtr.Zero;
        private IntPtr m_HandleTimerQueue;

        //Attribute

        /// <summary>
        ///     Konstruktor
        /// </summary>
        public QueueTimer()
        {
            m_DelegateTimerProc = OnTimer;
        }

        /// <summary>
        ///     IsRunning
        /// </summary>
        /// <returns></returns>
        public bool IsRunning { get; private set; }

        /// <summary>
        ///     Milliseconds
        /// </summary>
        public uint Milliseconds { get; private set; } = 20;

        /// <summary>
        ///     ResolutionInMilliseconds
        /// </summary>
        public uint ResolutionInMilliseconds { get; private set; }

        public event DelegateTimerTick TimerTick;

        /// <summary>
        ///     SetBestResolution
        /// </summary>
        public static void SetBestResolution()
        {
            //QueueTimer Auflösung ermitteln
            var tc = new Win32.TimeCaps();
            Win32.TimeGetDevCaps(ref tc, (uint) Marshal.SizeOf(typeof(Win32.TimeCaps)));
            var resolution = Math.Max(tc.wPeriodMin, 0);

            //QueueTimer Resolution setzen
            Win32.TimeBeginPeriod(resolution);
        }

        /// <summary>
        ///     ResetResolution
        /// </summary>
        public static void ResetResolution()
        {
            //QueueTimer Auflösung ermitteln
            var tc = new Win32.TimeCaps();
            Win32.TimeGetDevCaps(ref tc, (uint) Marshal.SizeOf(typeof(Win32.TimeCaps)));
            var resolution = Math.Max(tc.wPeriodMin, 0);

            //QueueTimer Resolution deaktivieren
            Win32.TimeBeginPeriod(resolution);
        }

        /// <summary>
        ///     Start
        /// </summary>
        /// <param name="milliseconds"></param>
        /// <param name="dueTimeInMilliseconds"></param>
        public void Start(uint milliseconds, uint dueTimeInMilliseconds)
        {
            //Werte übernehmen
            Milliseconds = milliseconds;

            //QueueTimer Auflösung ermitteln
            var tc = new Win32.TimeCaps();
            Win32.TimeGetDevCaps(ref tc, (uint) Marshal.SizeOf(typeof(Win32.TimeCaps)));
            ResolutionInMilliseconds = Math.Max(tc.wPeriodMin, 0);

            //QueueTimer Resolution setzen
            Win32.TimeBeginPeriod(ResolutionInMilliseconds);

            //QueueTimer Queue erstellen
            m_HandleTimerQueue = Win32.CreateTimerQueue();
            m_GCHandleTimerQueue = GCHandle.Alloc(m_HandleTimerQueue);

            //Versuche QueueTimer zu starten
            var resultCreate = Win32.CreateTimerQueueTimer(out m_HandleTimer, m_HandleTimerQueue, m_DelegateTimerProc,
                IntPtr.Zero, dueTimeInMilliseconds, Milliseconds, Win32.WT_EXECUTEINTIMERTHREAD);
            if (resultCreate)
            {
                //Handle im Speicher halten
                m_GCHandleTimer = GCHandle.Alloc(m_HandleTimer, GCHandleType.Pinned);
                //QueueTimer ist gestartet
                IsRunning = true;
            }
        }

        /// <summary>
        ///     Stop
        /// </summary>
        public void Stop()
        {
            if (m_HandleTimer != IntPtr.Zero)
            {
                //QueueTimer beenden
                Win32.DeleteTimerQueueTimer(IntPtr.Zero, m_HandleTimer, IntPtr.Zero);
                //QueueTimer Resolution beenden
                Win32.TimeEndPeriod(ResolutionInMilliseconds);

                //QueueTimer Queue löschen
                if (m_HandleTimerQueue != IntPtr.Zero)
                    Win32.DeleteTimerQueue(m_HandleTimerQueue);

                //Handles freigeben
                if (m_GCHandleTimer.IsAllocated)
                    m_GCHandleTimer.Free();
                if (m_GCHandleTimerQueue.IsAllocated)
                    m_GCHandleTimerQueue.Free();

                //Variablen setzen
                m_HandleTimer = IntPtr.Zero;
                m_HandleTimerQueue = IntPtr.Zero;
                IsRunning = false;
            }
        }

        /// <summary>
        ///     OnTimer
        /// </summary>
        /// <param name="lpParameter"></param>
        /// <param name="TimerOrWaitFired"></param>
        private void OnTimer(IntPtr lpParameter, bool TimerOrWaitFired)
        {
            if (TimerTick != null)
                TimerTick();
        }
    }

    /// <summary>
    ///     QueueTimer
    /// </summary>
    public class EventTimer
    {
        public delegate void DelegateTimerTick();

        //Delegates bzw. Events
        private readonly Win32.TimerEventHandler m_DelegateTimeEvent;

        private GCHandle m_GCHandleTimer;

        //Attribute

        private uint m_TimerId;
        private uint m_UserData;

        /// <summary>
        ///     Konstruktor
        /// </summary>
        public EventTimer()
        {
            m_DelegateTimeEvent = OnTimer;
        }

        /// <summary>
        ///     IsRunning
        /// </summary>
        /// <returns></returns>
        public bool IsRunning { get; private set; }

        /// <summary>
        ///     Milliseconds
        /// </summary>
        public uint Milliseconds { get; private set; } = 20;

        /// <summary>
        ///     ResolutionInMilliseconds
        /// </summary>
        public uint ResolutionInMilliseconds { get; private set; }

        public event DelegateTimerTick TimerTick;

        /// <summary>
        ///     SetBestResolution
        /// </summary>
        public static void SetBestResolution()
        {
            //QueueTimer Auflösung ermitteln
            var tc = new Win32.TimeCaps();
            Win32.TimeGetDevCaps(ref tc, (uint) Marshal.SizeOf(typeof(Win32.TimeCaps)));
            var resolution = Math.Max(tc.wPeriodMin, 0);

            //QueueTimer Resolution setzen
            Win32.TimeBeginPeriod(resolution);
        }

        /// <summary>
        ///     ResetResolution
        /// </summary>
        public static void ResetResolution()
        {
            //QueueTimer Auflösung ermitteln
            var tc = new Win32.TimeCaps();
            Win32.TimeGetDevCaps(ref tc, (uint) Marshal.SizeOf(typeof(Win32.TimeCaps)));
            var resolution = Math.Max(tc.wPeriodMin, 0);

            //QueueTimer Resolution deaktivieren
            Win32.TimeEndPeriod(resolution);
        }

        /// <summary>
        ///     Start
        /// </summary>
        /// <param name="milliseconds"></param>
        /// <param name="dueTimeInMilliseconds"></param>
        public void Start(uint milliseconds, uint dueTimeInMilliseconds)
        {
            //Werte übernehmen
            Milliseconds = milliseconds;

            //Timer Auflösung ermitteln
            var tc = new Win32.TimeCaps();
            Win32.TimeGetDevCaps(ref tc, (uint) Marshal.SizeOf(typeof(Win32.TimeCaps)));
            ResolutionInMilliseconds = Math.Max(tc.wPeriodMin, 0);

            //Timer Resolution setzen
            Win32.TimeBeginPeriod(ResolutionInMilliseconds);

            //Versuche EventTimer zu starten
            m_TimerId = Win32.TimeSetEvent(Milliseconds, ResolutionInMilliseconds, m_DelegateTimeEvent, ref m_UserData,
                Win32.TIME_PERIODIC);
            if (m_TimerId > 0)
            {
                //Handle im Speicher halten
                m_GCHandleTimer = GCHandle.Alloc(m_TimerId, GCHandleType.Pinned);
                //QueueTimer ist gestartet
                IsRunning = true;
            }
        }

        /// <summary>
        ///     Stop
        /// </summary>
        public void Stop()
        {
            if (m_TimerId > 0)
            {
                //Timer beenden
                Win32.TimeKillEvent(m_TimerId);
                //Timer Resolution beenden
                Win32.TimeEndPeriod(ResolutionInMilliseconds);

                //Handles freigeben
                if (m_GCHandleTimer.IsAllocated)
                    m_GCHandleTimer.Free();

                //Variablen setzen
                m_TimerId = 0;
                IsRunning = false;
            }
        }

        /// <summary>
        ///     OnTimer
        /// </summary>
        /// <param name="lpParameter"></param>
        /// <param name="TimerOrWaitFired"></param>
        private void OnTimer(uint id, uint msg, ref uint userCtx, uint rsv1, uint rsv2)
        {
            if (TimerTick != null)
                TimerTick();
        }
    }

    /// <summary>
    ///     Stopwatch
    /// </summary>
    public class Stopwatch
    {
        private readonly long m_Frequency;
        private long m_DurationTime;

        //Attribute
        private long m_StartTime;

        /// <summary>
        ///     Stopwatch
        /// </summary>
        public Stopwatch()
        {
            //Prüfen
            if (Win32.QueryPerformanceFrequency(out m_Frequency) == false)
                throw new Exception("High Performance counter not supported");
        }

        /// <summary>
        ///     ElapsedMilliseconds
        /// </summary>
        public double ElapsedMilliseconds
        {
            get
            {
                Win32.QueryPerformanceCounter(out m_DurationTime);
                return (m_DurationTime - m_StartTime) / (double) m_Frequency * 1000;
            }
        }

        /// <summary>
        ///     Start
        /// </summary>
        public void Start()
        {
            Win32.QueryPerformanceCounter(out m_StartTime);
            m_DurationTime = m_StartTime;
        }
    }
}