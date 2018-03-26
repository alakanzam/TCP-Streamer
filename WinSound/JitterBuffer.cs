using System;
using System.Collections.Generic;

namespace WinSound
{
    /// <summary>
    ///     JitterBuffer
    /// </summary>
    public class JitterBuffer
    {
        //Delegates bzw. Event
        public delegate void DelegateDataAvailable(object sender, RTPPacket packet);

        private readonly Queue<RTPPacket> m_Buffer = new Queue<RTPPacket>();

        //Attribute
        private readonly object m_Sender;

        private readonly EventTimer m_Timer = new EventTimer();
        private RTPPacket m_LastRTPPacket = new RTPPacket();
        private bool m_Overflow;
        private bool m_Underflow = true;

        /// <summary>
        ///     Konstruktor
        /// </summary>
        public JitterBuffer(object sender, uint maxRTPPackets, uint timerIntervalInMilliseconds)
        {
            //Mindestanzahl einhalten
            if (maxRTPPackets < 2)
                throw new Exception("Wrong Arguments. Minimum maxRTPPackets is 2");

            m_Sender = sender;
            Maximum = maxRTPPackets;
            IntervalInMilliseconds = timerIntervalInMilliseconds;

            Init();
        }

        /// <summary>
        ///     Anzahl Packete im Buffer
        /// </summary>
        public int Length => m_Buffer.Count;

        /// <summary>
        ///     Maximale Anzahl an RTP Packeten
        /// </summary>
        public uint Maximum { get; } = 10;

        /// <summary>
        ///     IntervalInMilliseconds
        /// </summary>
        public uint IntervalInMilliseconds { get; } = 20;

        public event DelegateDataAvailable DataAvailable;

        /// <summary>
        ///     Init
        /// </summary>
        private void Init()
        {
            InitTimer();
        }

        /// <summary>
        ///     InitTimer
        /// </summary>
        private void InitTimer()
        {
            m_Timer.TimerTick += OnTimerTick;
        }

        /// <summary>
        ///     Start
        /// </summary>
        public void Start()
        {
            m_Timer.Start(IntervalInMilliseconds, 0);
            m_Underflow = true;
        }

        /// <summary>
        ///     Stop
        /// </summary>
        public void Stop()
        {
            m_Timer.Stop();
            m_Buffer.Clear();
        }

        /// <summary>
        ///     OnTimerTick
        /// </summary>
        private void OnTimerTick()
        {
            try
            {
                if (DataAvailable != null)
                    if (m_Buffer.Count > 0)
                    {
                        //Wenn Überlauf
                        if (m_Overflow)
                            if (m_Buffer.Count <= Maximum / 2)
                                m_Overflow = false;

                        //Wenn Underflow
                        if (m_Underflow)
                            if (m_Buffer.Count < Maximum / 2)
                                return;
                            else
                                m_Underflow = false;

                        //Daten schicken
                        m_LastRTPPacket = m_Buffer.Dequeue();
                        DataAvailable(m_Sender, m_LastRTPPacket);
                    }
                    else
                    {
                        //Kein Overflow
                        m_Overflow = false;

                        //Wenn Buffer leer
                        if (m_LastRTPPacket != null && m_Underflow == false)
                            if (m_LastRTPPacket.Data != null)
                                m_Underflow = true;
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine("JitterBuffer.cs | OnTimerTick() | {0}", ex.Message);
            }
        }

        /// <summary>
        ///     AddData
        /// </summary>
        /// <param name="data"></param>
        public void AddData(RTPPacket packet)
        {
            try
            {
                //Wenn kein Überlauf
                if (m_Overflow == false)
                    if (m_Buffer.Count <= Maximum)
                        m_Buffer.Enqueue(packet);
                    else
                        m_Overflow = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("JitterBuffer.cs | AddData() | {0}", ex.Message);
            }
        }
    }
}