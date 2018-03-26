using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Serialization;
using NF;
using TCPStreamer.Properties;
using WinSound;
using Timer = System.Windows.Forms.Timer;

namespace TCPStreamer
{
    public partial class FormMain : Form
    {
        private const int RecordingJitterBufferCount = 8;

        public static Dictionary<object, Queue<List<byte>>> DictionaryMixed =
            new Dictionary<object, Queue<List<byte>>>();

        private readonly object LockerDictionary = new object();

        private readonly string m_ConfigFileName =
            Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "config.xml");

        private readonly int m_CSRCCount = 0;

        private readonly Dictionary<ServerThread, ServerThreadData> m_DictionaryServerDatas =
            new Dictionary<ServerThread, ServerThreadData>();

        private readonly Encoding m_Encoding = Encoding.GetEncoding(1252);
        private readonly bool m_Extension = false;
        private readonly bool m_Marker = false;
        private readonly uint m_Milliseconds = 20;
        private readonly bool m_Padding = false;
        private readonly int m_PayloadType = 0;
        private readonly Protocol m_PrototolClient = new Protocol(ProtocolTypes.LH, Encoding.Default);
        private readonly uint m_RecorderFactor = 4;
        private readonly int m_SoundBufferCount = 8;
        private readonly uint m_SourceId = 0;
        private readonly int m_Version = 2;

        //Attribute
        private TCPClient m_Client;

        private Configuration m_Config = new Configuration();
        private WaveFileHeader m_FileHeader = new WaveFileHeader();
        private byte[] m_FilePayloadBuffer;
        private bool m_IsFormMain = true;
        private JitterBuffer m_JitterBufferClientPlaying;
        private JitterBuffer m_JitterBufferClientRecording;
        private JitterBuffer m_JitterBufferServerRecording;
        private Player m_PlayerClient;
        private Recorder m_Recorder_Client;
        private Recorder m_Recorder_Server;
        private int m_RTPPartsLength;
        private long m_SequenceNumber = 4596;
        private TCPServer m_Server;
        private Timer m_TimerDrawProgressBar;
        private EventTimer m_TimerMixed;
        private Timer m_TimerProgressBarFile = new Timer();
        private Timer m_TimerProgressBarPlayingClient = new Timer();
        private long m_TimeStamp;

        /// <summary>
        ///     Konstruktor
        /// </summary>
        public FormMain()
        {
            InitializeComponent();
            Init();
        }

        /// <summary>
        ///     UseJitterBuffer
        /// </summary>
        private bool UseJitterBufferServer => m_Config.JitterBufferCountServer >= 2;

        /// <summary>
        ///     UseJitterBuffer
        /// </summary>
        private bool UseJitterBufferClientRecording => m_Config.UseJitterBufferClientRecording;

        /// <summary>
        ///     UseJitterBufferServerRecording
        /// </summary>
        private bool UseJitterBufferServerRecording => m_Config.UseJitterBufferServerRecording;

        /// <summary>
        ///     IsRecorderStarted
        /// </summary>
        private bool IsRecorderFromSounddeviceStarted_Client
        {
            get
            {
                if (m_Recorder_Client != null)
                    return m_Recorder_Client.Started;
                return false;
            }
        }

        /// <summary>
        ///     IsRecorderFromSounddeviceStarted_Server
        /// </summary>
        private bool IsRecorderFromSounddeviceStarted_Server
        {
            get
            {
                if (m_Recorder_Server != null)
                    return m_Recorder_Server.Started;
                return false;
            }
        }

        /// <summary>
        ///     IsServerRunning
        /// </summary>
        private bool IsServerRunning
        {
            get
            {
                if (m_Server != null)
                    return m_Server.State == TCPServer.ListenerState.Started;
                return false;
            }
        }

        /// <summary>
        ///     IsClientConnected
        /// </summary>
        private bool IsClientConnected
        {
            get
            {
                if (m_Client != null)
                    return m_Client.Connected;
                return false;
            }
        }

        /// <summary>
        ///     IsPlayingToSoundDeviceWanted
        /// </summary>
        private bool IsPlayingToSoundDeviceWanted
        {
            get
            {
                if (ComboboxOutputSoundDeviceNameClient.SelectedIndex >= 1)
                    return true;
                return false;
            }
        }

        /// <summary>
        ///     Init
        /// </summary>
        private void Init()
        {
            try
            {
                CreateHandle();
                InitComboboxes();
                LoadConfig();
                InitJitterBufferClientRecording();
                InitJitterBufferClientPlaying();
                InitJitterBufferServerRecording();
                InitTimerShowProgressBarPlayingClient();
                InitProtocolClient();
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     InitProtocolClient
        /// </summary>
        private void InitProtocolClient()
        {
            if (m_PrototolClient != null)
                m_PrototolClient.DataComplete += OnProtocolClient_DataComplete;
        }

        /// <summary>
        ///     FillRTPBufferWithPayloadData
        /// </summary>
        /// <param name="header"></param>
        private void FillRTPBufferWithPayloadData(WaveFileHeader header)
        {
            m_RTPPartsLength =
                Utils.GetBytesPerInterval(header.SamplesPerSecond, header.BitsPerSample, header.Channels);
            m_FilePayloadBuffer = header.Payload;
        }

        /// <summary>
        ///     InitTimerShowProgressBarPlayingClient
        /// </summary>
        private void InitTimerShowProgressBarPlayingClient()
        {
            m_TimerProgressBarPlayingClient = new Timer();
            m_TimerProgressBarPlayingClient.Interval = 60;
            m_TimerProgressBarPlayingClient.Tick += OnTimerProgressPlayingClient;
        }

        /// <summary>
        ///     OnTimerProgressPlayingClient
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="e"></param>
        private void OnTimerProgressPlayingClient(object obj, EventArgs e)
        {
            try
            {
                if (m_PlayerClient != null)
                    ProgressBarPlayingClient.Value = Math.Min(m_JitterBufferClientPlaying.Length,
                        ProgressBarPlayingClient.Maximum);
            }
            catch (Exception ex)
            {
                Console.WriteLine("FormMain.cs | OnTimerProgressPlayingClient() | {0}", ex.Message);
                m_TimerProgressBarPlayingClient.Stop();
            }
        }

        /// <summary>
        ///     OnTimerSendMixedDataToAllClients
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="e"></param>
        private void OnTimerSendMixedDataToAllClients()
        {
            try
            {
                //Liste mit allen Sprachdaten (eigene + Clients)
                var dic = new Dictionary<object, List<byte>>();
                var listlist = new List<List<byte>>();
                var copy = new Dictionary<object, Queue<List<byte>>>(DictionaryMixed);
                {
                    Queue<List<byte>> q = null;
                    foreach (var obj in copy.Keys)
                    {
                        q = copy[obj];

                        //Wenn Daten vorhanden
                        if (q.Count > 0)
                        {
                            dic[obj] = q.Dequeue();
                            listlist.Add(dic[obj]);
                        }
                    }
                }

                if (listlist.Count > 0)
                {
                    //Gemischte Sprachdaten
                    var mixedBytes = Mixer.MixBytes(listlist, m_Config.BitsPerSampleServer).ToArray();
                    var listMixed = new List<byte>(mixedBytes);

                    //Für alle Clients
                    foreach (var client in m_Server.Clients)
                        //Wenn nicht stumm
                        if (client.IsMute == false)
                        {
                            //Gemixte Sprache für Client
                            var mixedBytesClient = mixedBytes;

                            if (dic.ContainsKey(client))
                            {
                                //Sprache des Clients ermitteln
                                var listClient = dic[client];

                                //Sprache des Clients aus Mix subtrahieren
                                mixedBytesClient = Mixer.SubsctractBytes_16Bit(listMixed, listClient).ToArray();
                            }

                            //RTP Packet erstellen
                            var rtp = ToRTPPacket(mixedBytesClient, m_Config.BitsPerSampleServer,
                                m_Config.ChannelsServer);
                            var rtpBytes = rtp.ToBytes();

                            //Absenden
                            client.Send(m_PrototolClient.ToBytes(rtpBytes));
                        }
                }
            }

            catch (Exception ex)
            {
                Console.WriteLine("FormMain.cs | OnTimerSendMixedDataToAllClients() | {0}", ex.Message);
                m_TimerProgressBarPlayingClient.Stop();
            }
        }

        /// <summary>
        ///     InitJitterBufferClientRecording
        /// </summary>
        private void InitJitterBufferClientRecording()
        {
            //Wenn vorhanden
            if (m_JitterBufferClientRecording != null)
                m_JitterBufferClientRecording.DataAvailable -= OnJitterBufferClientDataAvailableRecording;

            //Neu erstellen
            m_JitterBufferClientRecording = new JitterBuffer(null, RecordingJitterBufferCount, 20);
            m_JitterBufferClientRecording.DataAvailable += OnJitterBufferClientDataAvailableRecording;
        }

        /// <summary>
        ///     InitJitterBufferClientPlaying
        /// </summary>
        private void InitJitterBufferClientPlaying()
        {
            //Wenn vorhanden
            if (m_JitterBufferClientPlaying != null)
                m_JitterBufferClientPlaying.DataAvailable -= OnJitterBufferClientDataAvailablePlaying;

            //Neu erstellen
            m_JitterBufferClientPlaying = new JitterBuffer(null, m_Config.JitterBufferCountClient, 20);
            m_JitterBufferClientPlaying.DataAvailable += OnJitterBufferClientDataAvailablePlaying;
        }

        /// <summary>
        ///     InitJitterBuffer
        /// </summary>
        private void InitJitterBufferServerRecording()
        {
            //Wenn vorhanden
            if (m_JitterBufferServerRecording != null)
                m_JitterBufferServerRecording.DataAvailable -= OnJitterBufferServerDataAvailable;

            //Neu erstellen
            m_JitterBufferServerRecording = new JitterBuffer(null, RecordingJitterBufferCount, 20);
            m_JitterBufferServerRecording.DataAvailable += OnJitterBufferServerDataAvailable;
        }

        /// <summary>
        ///     StartRecordingFromSounddevice_Client
        /// </summary>
        private void StartRecordingFromSounddevice_Client()
        {
            try
            {
                if (IsRecorderFromSounddeviceStarted_Client == false)
                {
                    //Buffer Grösse berechnen
                    var bufferSize = 0;
                    if (UseJitterBufferClientRecording)
                        bufferSize = Utils.GetBytesPerInterval((uint) m_Config.SamplesPerSecondClient,
                                         m_Config.BitsPerSampleClient, m_Config.ChannelsClient) *
                                     (int) m_RecorderFactor;
                    else
                        bufferSize = Utils.GetBytesPerInterval((uint) m_Config.SamplesPerSecondClient,
                            m_Config.BitsPerSampleClient, m_Config.ChannelsClient);

                    //Wenn Buffer korrekt
                    if (bufferSize > 0)
                    {
                        //Recorder erstellen
                        m_Recorder_Client = new Recorder();

                        //Events hinzufügen
                        m_Recorder_Client.DataRecorded += OnDataReceivedFromSoundcard_Client;
                        m_Recorder_Client.RecordingStopped += OnRecordingStopped_Client;

                        //Recorder starten
                        if (m_Recorder_Client.Start(m_Config.SoundInputDeviceNameClient,
                            m_Config.SamplesPerSecondClient, m_Config.BitsPerSampleClient, m_Config.ChannelsClient,
                            m_SoundBufferCount, bufferSize))
                        {
                            //Anzeigen
                            ShowStreamingFromSounddeviceStarted_Client();

                            //Wenn JitterBuffer
                            if (UseJitterBufferClientRecording)
                                m_JitterBufferClientRecording.Start();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     StartRecordingFromSounddevice_Server
        /// </summary>
        private void StartRecordingFromSounddevice_Server()
        {
            try
            {
                if (IsRecorderFromSounddeviceStarted_Server == false)
                {
                    //Buffer Grösse berechnen
                    var bufferSize = 0;
                    if (UseJitterBufferServerRecording)
                        bufferSize = Utils.GetBytesPerInterval((uint) m_Config.SamplesPerSecondServer,
                                         m_Config.BitsPerSampleServer, m_Config.ChannelsServer) *
                                     (int) m_RecorderFactor;
                    else
                        bufferSize = Utils.GetBytesPerInterval((uint) m_Config.SamplesPerSecondServer,
                            m_Config.BitsPerSampleServer, m_Config.ChannelsServer);

                    //Wenn Buffer korrekt
                    if (bufferSize > 0)
                    {
                        //Recorder erstellen
                        m_Recorder_Server = new Recorder();

                        //Events hinzufügen
                        m_Recorder_Server.DataRecorded += OnDataReceivedFromSoundcard_Server;
                        m_Recorder_Server.RecordingStopped += OnRecordingStopped_Server;

                        //Recorder starten
                        if (m_Recorder_Server.Start(m_Config.SoundInputDeviceNameServer,
                            m_Config.SamplesPerSecondServer, m_Config.BitsPerSampleServer, m_Config.ChannelsServer,
                            m_SoundBufferCount, bufferSize))
                        {
                            //Anzeigen
                            ShowStreamingFromSounddeviceStarted_Server();

                            //Zu Mixer hinzufügen
                            DictionaryMixed[this] = new Queue<List<byte>>();

                            //JitterBuffer starten
                            m_JitterBufferServerRecording.Start();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     StopRecordingFromSounddevice_Client
        /// </summary>
        private void StopRecordingFromSounddevice_Client()
        {
            try
            {
                if (IsRecorderFromSounddeviceStarted_Client)
                {
                    //Stoppen
                    m_Recorder_Client.Stop();

                    //Events entfernen
                    m_Recorder_Client.DataRecorded -= OnDataReceivedFromSoundcard_Client;
                    m_Recorder_Client.RecordingStopped -= OnRecordingStopped_Client;
                    m_Recorder_Client = null;

                    //Wenn JitterBuffer
                    if (UseJitterBufferClientRecording)
                        m_JitterBufferClientRecording.Stop();

                    //Anzeigen
                    ShowStreamingFromSounddeviceStopped_Client();
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     StopRecordingFromSounddevice_Server
        /// </summary>
        private void StopRecordingFromSounddevice_Server()
        {
            try
            {
                if (IsRecorderFromSounddeviceStarted_Server)
                {
                    //Stoppen
                    m_Recorder_Server.Stop();

                    //Events entfernen
                    m_Recorder_Server.DataRecorded -= OnDataReceivedFromSoundcard_Server;
                    m_Recorder_Server.RecordingStopped -= OnRecordingStopped_Server;
                    m_Recorder_Server = null;

                    //JitterBuffer beenden
                    m_JitterBufferServerRecording.Stop();

                    //Anzeigen
                    ShowStreamingFromSounddeviceStopped_Server();
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     OnRecordingStopped
        /// </summary>
        private void OnRecordingStopped_Client()
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    //Anzeigen
                    ShowStreamingFromSounddeviceStopped_Client();
                }));
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     OnRecordingStopped_Server
        /// </summary>
        private void OnRecordingStopped_Server()
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    //Anzeigen
                    ShowStreamingFromSounddeviceStopped_Server();
                }));
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     OnDataReceivedFromSoundcard_Client
        /// </summary>
        /// <param name="linearData"></param>
        private void OnDataReceivedFromSoundcard_Client(byte[] data)
        {
            try
            {
                lock (this)
                {
                    if (IsClientConnected)
                        if (m_Config.ClientNoSpeakAll == false)
                        {
                            //Sounddaten in kleinere Einzelteile zerlegen
                            var bytesPerInterval = Utils.GetBytesPerInterval((uint) m_Config.SamplesPerSecondClient,
                                m_Config.BitsPerSampleClient, m_Config.ChannelsClient);
                            var count = data.Length / bytesPerInterval;
                            var currentPos = 0;
                            for (var i = 0; i < count; i++)
                            {
                                //Teilstück in RTP Packet umwandeln
                                var partBytes = new byte[bytesPerInterval];
                                Array.Copy(data, currentPos, partBytes, 0, bytesPerInterval);
                                currentPos += bytesPerInterval;
                                var rtp = ToRTPPacket(partBytes, m_Config.BitsPerSampleClient, m_Config.ChannelsClient);

                                //Wenn JitterBuffer
                                if (UseJitterBufferClientRecording)
                                {
                                    //In Buffer legen
                                    m_JitterBufferClientRecording.AddData(rtp);
                                }
                                else
                                {
                                    //Alles in RTP Packet umwandeln
                                    var rtpBytes = ToRTPData(data, m_Config.BitsPerSampleClient,
                                        m_Config.ChannelsClient);
                                    //Absenden
                                    m_Client.Send(m_PrototolClient.ToBytes(rtpBytes));
                                }
                            }
                        }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     OnDataReceivedFromSoundcard_Server
        /// </summary>
        /// <param name="data"></param>
        private void OnDataReceivedFromSoundcard_Server(byte[] data)
        {
            try
            {
                lock (this)
                {
                    if (IsServerRunning)
                        if (m_IsFormMain)
                            if (m_Config.ServerNoSpeakAll == false)
                            {
                                //Sounddaten in kleinere Einzelteile zerlegen
                                var bytesPerInterval = Utils.GetBytesPerInterval((uint) m_Config.SamplesPerSecondServer,
                                    m_Config.BitsPerSampleServer, m_Config.ChannelsServer);
                                var count = data.Length / bytesPerInterval;
                                var currentPos = 0;
                                for (var i = 0; i < count; i++)
                                {
                                    //Teilstück in RTP Packet umwandeln
                                    var partBytes = new byte[bytesPerInterval];
                                    Array.Copy(data, currentPos, partBytes, 0, bytesPerInterval);
                                    currentPos += bytesPerInterval;

                                    //Wenn Buffer nicht zu gross
                                    var q = DictionaryMixed[this];
                                    if (q.Count < 10)
                                        q.Enqueue(new List<byte>(partBytes));
                                }
                            }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     OnJitterBufferClientDataAvailable
        /// </summary>
        /// <param name="rtp"></param>
        private void OnJitterBufferClientDataAvailableRecording(object sender, RTPPacket rtp)
        {
            try
            {
                //Prüfen
                if (rtp != null && m_Client != null && rtp.Data != null && rtp.Data.Length > 0)
                    if (IsClientConnected)
                        if (m_IsFormMain)
                        {
                            //RTP Packet in Bytes umwandeln
                            var rtpBytes = rtp.ToBytes();
                            //Absenden
                            m_Client.Send(m_PrototolClient.ToBytes(rtpBytes));
                        }
            }
            catch (Exception ex)
            {
                var sf = new StackFrame(true);
                ShowError(LabelClient,
                    string.Format("Exception: {0} StackTrace: {1}. FileName: {2} Method: {3} Line: {4}", ex.Message,
                        ex.StackTrace, sf.GetFileName(), sf.GetMethod(), sf.GetFileLineNumber()));
            }
        }

        /// <summary>
        ///     OnJitterBufferClientDataAvailablePlaying
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="rtp"></param>
        private void OnJitterBufferClientDataAvailablePlaying(object sender, RTPPacket rtp)
        {
            try
            {
                if (m_PlayerClient != null)
                    if (m_PlayerClient.Opened)
                        if (m_IsFormMain)
                            if (m_Config.MuteClientPlaying == false)
                            {
                                //Nach Linear umwandeln
                                var linearBytes = Utils.MuLawToLinear(rtp.Data, m_Config.BitsPerSampleClient,
                                    m_Config.ChannelsClient);
                                //Abspielen
                                m_PlayerClient.PlayData(linearBytes, false);
                            }
            }
            catch (Exception ex)
            {
                var sf = new StackFrame(true);
                ShowError(LabelClient,
                    string.Format("Exception: {0} StackTrace: {1}. FileName: {2} Method: {3} Line: {4}", ex.Message,
                        ex.StackTrace, sf.GetFileName(), sf.GetMethod(), sf.GetFileLineNumber()));
            }
        }

        /// <summary>
        ///     OnJitterBufferServerDataAvailable
        /// </summary>
        /// <param name="rtp"></param>
        private void OnJitterBufferServerDataAvailable(object sender, RTPPacket rtp)
        {
            try
            {
                if (IsServerRunning)
                    if (m_IsFormMain)
                    {
                        //RTP Packet in Bytes umwandeln
                        var rtpBytes = rtp.ToBytes();

                        //Für alle Clients
                        var list = new List<ServerThread>(m_Server.Clients);
                        foreach (var client in list)
                            //Wenn nicht Mute
                            if (client.IsMute == false)
                                try
                                {
                                    //Absenden
                                    client.Send(m_PrototolClient.ToBytes(rtpBytes));
                                }
                                catch (Exception)
                                {
                                    //Eintrag löschen
                                    RemoveControlInAllFlowLayoutPanelsByServerThread(client);
                                }
                    }
            }
            catch (Exception ex)
            {
                var sf = new StackFrame(true);
                ShowError(LabelServer,
                    string.Format("Exception: {0} StackTrace: {1}. FileName: {2} Method: {3} Line: {4}", ex.Message,
                        ex.StackTrace, sf.GetFileName(), sf.GetMethod(), sf.GetFileLineNumber()));
            }
        }

        /// <summary>
        ///     ToRTPData
        /// </summary>
        /// <param name="linearData"></param>
        /// <param name="bitsPerSample"></param>
        /// <param name="channels"></param>
        /// <returns></returns>
        private byte[] ToRTPData(byte[] data, int bitsPerSample, int channels)
        {
            //Neues RTP Packet erstellen
            var rtp = ToRTPPacket(data, bitsPerSample, channels);
            //RTPHeader in Bytes erstellen
            var rtpBytes = rtp.ToBytes();
            //Fertig
            return rtpBytes;
        }

        /// <summary>
        ///     ToRTPPacket
        /// </summary>
        /// <param name="linearData"></param>
        /// <param name="bitsPerSample"></param>
        /// <param name="channels"></param>
        /// <returns></returns>
        private RTPPacket ToRTPPacket(byte[] linearData, int bitsPerSample, int channels)
        {
            //Daten Nach MuLaw umwandeln
            var mulaws = Utils.LinearToMulaw(linearData, bitsPerSample, channels);

            //Neues RTP Packet erstellen
            var rtp = new RTPPacket();

            //Werte übernehmen
            rtp.Data = mulaws;
            rtp.CSRCCount = m_CSRCCount;
            rtp.Extension = m_Extension;
            rtp.HeaderLength = RTPPacket.MinHeaderLength;
            rtp.Marker = m_Marker;
            rtp.Padding = m_Padding;
            rtp.PayloadType = m_PayloadType;
            rtp.Version = m_Version;
            rtp.SourceId = m_SourceId;

            //RTP Header aktualisieren
            try
            {
                rtp.SequenceNumber = Convert.ToUInt16(m_SequenceNumber);
                m_SequenceNumber++;
            }
            catch (Exception)
            {
                m_SequenceNumber = 0;
            }
            try
            {
                rtp.Timestamp = Convert.ToUInt32(m_TimeStamp);
                m_TimeStamp += mulaws.Length;
            }
            catch (Exception)
            {
                m_TimeStamp = 0;
            }

            //Fertig
            return rtp;
        }

        /// <summary>
        ///     InitComboboxes
        /// </summary>
        private void InitComboboxes()
        {
            InitComboboxesClient();
            InitComboboxesServer();
        }

        /// <summary>
        ///     InitComboboxesClient
        /// </summary>
        private void InitComboboxesClient()
        {
            ComboboxOutputSoundDeviceNameClient.Items.Clear();
            ComboboxInputSoundDeviceNameClient.Items.Clear();
            var playbackNames = WinSound.WinSound.GetPlaybackNames();
            var recordingNames = WinSound.WinSound.GetRecordingNames();

            //Output
            ComboboxOutputSoundDeviceNameClient.Items.Add("None");
            foreach (var name in playbackNames.Where(x => x != null))
                ComboboxOutputSoundDeviceNameClient.Items.Add(name);
            //Input
            foreach (var name in recordingNames.Where(x => x != null))
                ComboboxInputSoundDeviceNameClient.Items.Add(name);

            //Output
            if (ComboboxOutputSoundDeviceNameClient.Items.Count > 0)
                ComboboxOutputSoundDeviceNameClient.SelectedIndex = 0;
            //Input
            if (ComboboxInputSoundDeviceNameClient.Items.Count > 0)
                ComboboxInputSoundDeviceNameClient.SelectedIndex = 0;
        }

        /// <summary>
        ///     InitComboboxesServer
        /// </summary>
        private void InitComboboxesServer()
        {
            ComboboxOutputSoundDeviceNameServer.Items.Clear();
            ComboboxInputSoundDeviceNameServer.Items.Clear();
            var playbackNames = WinSound.WinSound.GetPlaybackNames();
            var recordingNames = WinSound.WinSound.GetRecordingNames();

            //Output
            foreach (var name in playbackNames.Where(x => x != null))
                ComboboxOutputSoundDeviceNameServer.Items.Add(name);
            //Input
            foreach (var name in recordingNames.Where(x => x != null))
                ComboboxInputSoundDeviceNameServer.Items.Add(name);

            //Output
            if (ComboboxOutputSoundDeviceNameServer.Items.Count > 0)
                ComboboxOutputSoundDeviceNameServer.SelectedIndex = 0;
            //Input
            if (ComboboxInputSoundDeviceNameServer.Items.Count > 0)
                ComboboxInputSoundDeviceNameServer.SelectedIndex = 0;
        }

        /// <summary>
        ///     ConnectClient
        /// </summary>
        private void ConnectClient()
        {
            try
            {
                if (IsClientConnected == false)
                    if (m_Config.IpAddressClient.Length > 0 && m_Config.PortClient > 0)
                    {
                        m_Client = new TCPClient(m_Config.IpAddressClient, m_Config.PortClient);
                        m_Client.ClientConnected += OnClientConnected;
                        m_Client.ClientDisconnected += OnClientDisconnected;
                        m_Client.ExceptionAppeared += OnClientExceptionAppeared;
                        m_Client.DataReceived += OnClientDataReceived;
                        m_Client.Connect();
                    }
            }
            catch (Exception ex)
            {
                m_Client = null;
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     DisconnectClient
        /// </summary>
        private void DisconnectClient()
        {
            try
            {
                //Aufnahme beenden
                StopRecordingFromSounddevice_Client();

                if (m_Client != null)
                {
                    //Client beenden
                    m_Client.Disconnect();
                    m_Client.ClientConnected -= OnClientConnected;
                    m_Client.ClientDisconnected -= OnClientDisconnected;
                    m_Client.ExceptionAppeared -= OnClientExceptionAppeared;
                    m_Client.DataReceived -= OnClientDataReceived;
                    m_Client = null;
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     StartServer
        /// </summary>
        private void StartServer()
        {
            try
            {
                if (IsServerRunning == false)
                    if (m_Config.IPAddressServer.Length > 0 && m_Config.PortServer > 0)
                    {
                        m_Server = new TCPServer();
                        m_Server.ClientConnected += OnServerClientConnected;
                        m_Server.ClientDisconnected += OnServerClientDisconnected;
                        m_Server.DataReceived += OnServerDataReceived;
                        m_Server.Start(m_Config.IPAddressServer, m_Config.PortServer);

                        //Je nach Server Status
                        if (m_Server.State == TCPServer.ListenerState.Started)
                            ShowServerStarted();
                        else
                            ShowServerStopped();
                    }
            }
            catch (Exception ex)
            {
                ShowError(LabelServer, ex.Message);
            }
        }

        /// <summary>
        ///     StopServer
        /// </summary>
        private void StopServer()
        {
            try
            {
                if (IsServerRunning)
                {
                    //Player beenden
                    DeleteAllServerThreadDatas();

                    //Server beenden
                    m_Server.Stop();
                    m_Server.ClientConnected -= OnServerClientConnected;
                    m_Server.ClientDisconnected -= OnServerClientDisconnected;
                    m_Server.DataReceived -= OnServerDataReceived;
                }

                //Je nach Server Status
                if (m_Server != null)
                    if (m_Server.State == TCPServer.ListenerState.Started)
                        ShowServerStarted();
                    else
                        ShowServerStopped();

                //Fertig
                m_Server = null;
            }
            catch (Exception ex)
            {
                ShowError(LabelServer, ex.Message);
            }
        }

        /// <summary>
        ///     OnClientConnected
        /// </summary>
        /// <param name="client"></param>
        /// <param name="info"></param>
        private void OnClientConnected(TCPClient client, string info)
        {
            ShowMessage(LabelClient, string.Format("Client connected {0}", ""));
            ShowClientConnected();
        }

        /// <summary>
        ///     OnClientDisconnected
        /// </summary>
        /// <param name="client"></param>
        /// <param name="info"></param>
        private void OnClientDisconnected(TCPClient client, string info)
        {
            //Abspielen beenden
            StopPlayingToSounddevice_Client();
            //Streamen von Sounddevice beenden
            StopRecordingFromSounddevice_Client();

            if (m_Client != null)
            {
                m_Client.ClientConnected -= OnClientConnected;
                m_Client.ClientDisconnected -= OnClientDisconnected;
                m_Client.ExceptionAppeared -= OnClientExceptionAppeared;
                m_Client.DataReceived -= OnClientDataReceived;
                ShowMessage(LabelClient, string.Format("Client disconnected {0}", ""));
            }

            ShowClientDisconnected();
        }

        /// <summary>
        ///     OnClientExceptionAppeared
        /// </summary>
        /// <param name="client"></param>
        /// <param name="ex"></param>
        private void OnClientExceptionAppeared(TCPClient client, Exception ex)
        {
            DisconnectClient();
            ShowError(LabelClient, ex.Message);
        }

        /// <summary>
        ///     OnClientDataReceived
        /// </summary>
        /// <param name="client"></param>
        /// <param name="bytes"></param>
        private void OnClientDataReceived(TCPClient client, byte[] bytes)
        {
            try
            {
                if (m_PrototolClient != null)
                    m_PrototolClient.Receive_LH(client, bytes);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     OnProtocolClient_DataComplete
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="data"></param>
        private void OnProtocolClient_DataComplete(object sender, byte[] data)
        {
            try
            {
                //Wenn der Player gestartet wurde
                if (m_PlayerClient != null)
                {
                    if (m_PlayerClient.Opened)
                    {
                        //RTP Header auslesen
                        var rtp = new RTPPacket(data);

                        //Wenn Header korrekt
                        if (rtp.Data != null)
                            if (m_JitterBufferClientPlaying != null)
                                m_JitterBufferClientPlaying.AddData(rtp);
                    }
                }
                else
                {
                    //Konfigurationsdaten erhalten
                    OnClientConfigReceived(sender, data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     OnClientConfigReceived
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="data"></param>
        private void OnClientConfigReceived(object sender, byte[] data)
        {
            try
            {
                var msg = m_Encoding.GetString(data);
                if (msg.Length > 0)
                {
                    //Parsen
                    var values = msg.Split(':');
                    var cmd = values[0];

                    //Je nach Kommando
                    switch (cmd.ToUpper())
                    {
                        case "SAMPLESPERSECOND":
                            var samplePerSecond = Convert.ToInt32(values[1]);
                            m_Config.SamplesPerSecondClient = samplePerSecond;

                            Invoke(new MethodInvoker(delegate
                            {
                                //Aufnahme starten
                                StartPlayingToSounddevice_Client();
                                StartRecordingFromSounddevice_Client();
                            }));
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     OnServerClientConnected
        /// </summary>
        /// <param name="st"></param>
        private void OnServerClientConnected(ServerThread st)
        {
            try
            {
                //ServerThread Daten erstellen
                var data = new ServerThreadData();
                //Initialisieren
                data.Init(st, m_Config.SoundOutputDeviceNameServer, m_Config.SamplesPerSecondServer,
                    m_Config.BitsPerSampleServer, m_Config.ChannelsServer, m_SoundBufferCount,
                    m_Config.JitterBufferCountServer, m_Milliseconds);
                //Hinzufügen
                m_DictionaryServerDatas[st] = data;
                //Zu FlowLayoutPanels hinzufügen
                AddServerClientToFlowLayoutPanel_ServerClient(st);
                AddServerClientToFlowLayoutPanel_ServerProgressBars(data);
                AddServerClientToFlowLayoutPanel_ServerListenButtons(data);
                AddServerClientToFlowLayoutPanel_ServerSpeakButtons(data);

                //Konfiguration senden
                SendConfigurationToClient(data);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     SendConfigurationToClient
        /// </summary>
        /// <param name="st"></param>
        private void SendConfigurationToClient(ServerThreadData data)
        {
            var bytesConfig =
                m_Encoding.GetBytes(string.Format("SamplesPerSecond:{0}", m_Config.SamplesPerSecondServer));
            data.ServerThread.Send(m_PrototolClient.ToBytes(bytesConfig));
        }

        /// <summary>
        ///     OnServerClientDisconnected
        /// </summary>
        /// <param name="st"></param>
        /// <param name="info"></param>
        private void OnServerClientDisconnected(ServerThread st, string info)
        {
            try
            {
                //Wenn vorhanden
                if (m_DictionaryServerDatas.ContainsKey(st))
                {
                    //Alle Daten freigeben
                    var data = m_DictionaryServerDatas[st];
                    data.Dispose();
                    lock (LockerDictionary)
                    {
                        //Entfernen
                        m_DictionaryServerDatas.Remove(st);
                    }
                    //Aus FlowLayoutPanels entfernen
                    RemoveServerClientToFlowLayoutPanel_ServerClient(st);
                    RemoveServerClientToFlowLayoutPanel_ServerProgressBar(data);
                    RemoveServerClientToFlowLayoutPanel_ButtonListen(data);
                    RemoveServerClientToFlowLayoutPanel_ButtonSpeak(data);
                }

                //Aus Mixdaten entfernen
                DictionaryMixed.Remove(st);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     StartTimerMixed
        /// </summary>
        private void StartTimerMixed()
        {
            if (m_TimerMixed == null)
            {
                m_TimerMixed = new EventTimer();
                m_TimerMixed.TimerTick += OnTimerSendMixedDataToAllClients;
                m_TimerMixed.Start(20, 0);
            }
        }

        /// <summary>
        ///     StopTimerMixed
        /// </summary>
        private void StopTimerMixed()
        {
            if (m_TimerMixed != null)
            {
                m_TimerMixed.Stop();
                m_TimerMixed.TimerTick -= OnTimerSendMixedDataToAllClients;
                m_TimerMixed = null;
            }
        }

        /// <summary>
        ///     StartTimerDrawProgressBar
        /// </summary>
        private void StartTimerDrawProgressBar()
        {
            if (m_TimerDrawProgressBar == null)
            {
                m_TimerDrawProgressBar = new Timer();
                m_TimerDrawProgressBar.Tick += OnTimerDrawServerClientsProgressBars;
                m_TimerDrawProgressBar.Interval = 100;
                m_TimerDrawProgressBar.Start();
            }
        }

        /// <summary>
        ///     StopTimerDrawCurve
        /// </summary>
        private void StopTimerDrawProgressBar()
        {
            try
            {
                if (m_TimerDrawProgressBar != null)
                {
                    m_TimerDrawProgressBar.Stop();
                    m_TimerDrawProgressBar = null;

                    //Für jede ProgressBar
                    foreach (ProgressBar prog in FlowLayoutPanelServerProgressBars.Controls)
                        if (prog.Tag != null)
                        {
                            //Daten ermitteln
                            var stData = (ServerThreadData) prog.Tag;

                            //Wenn ein JitterBuffer vorhanden
                            if (stData.JitterBuffer != null)
                                prog.Value = 0;
                        }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     OnTimerDrawServerClientsProgressBars
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="e"></param>
        private void OnTimerDrawServerClientsProgressBars(object obj, EventArgs e)
        {
            try
            {
                //Für jede ProgressBar
                foreach (ProgressBar prog in FlowLayoutPanelServerProgressBars.Controls)
                    if (prog.Tag != null)
                    {
                        //Daten ermitteln
                        var stData = (ServerThreadData) prog.Tag;

                        //Wenn ein JitterBuffer vorhanden
                        if (stData.JitterBuffer != null)
                            prog.Value = stData.JitterBuffer.Length;
                    }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     RemoveControlByTag
        /// </summary>
        /// <param name="controls"></param>
        /// <param name="tag"></param>
        private void RemoveControlByTag(Control.ControlCollection controls, object tag)
        {
            Invoke(new MethodInvoker(delegate
            {
                //Control anhand Tag ermitteln
                Control existing = null;
                foreach (Control ctrl in controls)
                    if (ctrl.Tag == tag)
                    {
                        existing = ctrl;
                        break;
                    }

                //Wenn vorhanden
                if (existing != null)
                    controls.Remove(existing);
            }));
        }

        /// <summary>
        ///     RemoveControlInAllFlowLayoutPanelsByServerThread
        /// </summary>
        /// <param name="st"></param>
        private void RemoveControlInAllFlowLayoutPanelsByServerThread(ServerThread st)
        {
            Invoke(new MethodInvoker(delegate
            {
                //Label
                Control ctrlLabel = null;
                foreach (Control ctrl in FlowLayoutPanelServerClients.Controls)
                {
                    var thread = (ServerThread) ctrl.Tag;
                    if (thread == st)
                    {
                        ctrlLabel = ctrl;
                        break;
                    }
                }
                if (ctrlLabel != null)
                    FlowLayoutPanelServerClients.Controls.Remove(ctrlLabel);

                //ProgressBar
                Control ctrlProgress = null;
                foreach (Control ctrl in FlowLayoutPanelServerProgressBars.Controls)
                {
                    var data = (ServerThreadData) ctrl.Tag;
                    if (data.ServerThread == st)
                    {
                        ctrlProgress = ctrl;
                        break;
                    }
                }
                if (ctrlProgress != null)
                    FlowLayoutPanelServerProgressBars.Controls.Remove(ctrlProgress);

                //ListenButton
                Control ctrlListen = null;
                foreach (Control ctrl in FlowLayoutPanelServerListen.Controls)
                {
                    var data = (ServerThreadData) ctrl.Tag;
                    if (data.ServerThread == st)
                    {
                        ctrlListen = ctrl;
                        break;
                    }
                }
                if (ctrlListen != null)
                    FlowLayoutPanelServerListen.Controls.Remove(ctrlListen);

                //SpeakButton
                Control ctrlSpeak = null;
                foreach (Control ctrl in FlowLayoutPanelServerSpeak.Controls)
                {
                    var data = (ServerThreadData) ctrl.Tag;
                    if (data.ServerThread == st)
                    {
                        ctrlSpeak = ctrl;
                        break;
                    }
                }
                if (ctrlSpeak != null)
                    FlowLayoutPanelServerSpeak.Controls.Remove(ctrlSpeak);
            }));
        }

        /// <summary>
        ///     RemoveServerClientToFlowLayoutPanel_ServerClient
        /// </summary>
        /// <param name="st"></param>
        private void RemoveServerClientToFlowLayoutPanel_ServerClient(ServerThread st)
        {
            try
            {
                FlowLayoutPanelServerClients.Invoke(new MethodInvoker(delegate
                {
                    //Label löschen
                    RemoveControlByTag(FlowLayoutPanelServerClients.Controls, st);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     RemoveServerClientToFlowLayoutPanel_ButtonListen
        /// </summary>
        /// <param name="st"></param>
        private void RemoveServerClientToFlowLayoutPanel_ButtonListen(ServerThreadData data)
        {
            try
            {
                FlowLayoutPanelServerListen.Invoke(new MethodInvoker(delegate
                {
                    //Button löschen
                    RemoveControlByTag(FlowLayoutPanelServerListen.Controls, data);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     RemoveServerClientToFlowLayoutPanel_ButtonSpeak
        /// </summary>
        /// <param name="data"></param>
        private void RemoveServerClientToFlowLayoutPanel_ButtonSpeak(ServerThreadData data)
        {
            try
            {
                FlowLayoutPanelServerSpeak.Invoke(new MethodInvoker(delegate
                {
                    //Button löschen
                    RemoveControlByTag(FlowLayoutPanelServerSpeak.Controls, data);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     AddServerClientToFlowLayoutPanel_ServerClient
        /// </summary>
        /// <param name="st"></param>
        private void AddServerClientToFlowLayoutPanel_ServerClient(ServerThread st)
        {
            try
            {
                FlowLayoutPanelServerClients.Invoke(new MethodInvoker(delegate
                {
                    //Label erstellen
                    var lab = new Label();
                    lab.AutoSize = false;
                    lab.BackColor = Color.DimGray;
                    lab.ForeColor = Color.White;
                    lab.Font = new Font(lab.Font, FontStyle.Bold);
                    lab.Margin = new Padding(5, FlowLayoutPanelServerClients.Controls.Count > 0 ? 5 : 10, 0, 5);
                    lab.TextAlign = ContentAlignment.MiddleCenter;
                    lab.Width = FlowLayoutPanelServerClients.Width - 10;
                    lab.Text = st.Client.Client.RemoteEndPoint.ToString();
                    lab.Tag = st;

                    //Hinzufügen
                    FlowLayoutPanelServerClients.Controls.Add(lab);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     AddServerClientToFlowLayoutPanel_ServerProgressBars
        /// </summary>
        /// <param name="st"></param>
        private void AddServerClientToFlowLayoutPanel_ServerProgressBars(ServerThreadData stData)
        {
            try
            {
                FlowLayoutPanelServerProgressBars.Invoke(new MethodInvoker(delegate
                {
                    //ProgressBar erstellen
                    var prog = new ProgressBar();
                    prog.AutoSize = false;
                    prog.Margin = new Padding(5, FlowLayoutPanelServerProgressBars.Controls.Count > 0 ? 5 : 10, 0, 5);
                    prog.Width = FlowLayoutPanelServerProgressBars.Width - 20;
                    prog.BackColor = Color.White;
                    prog.Maximum = (int) stData.JitterBuffer.Maximum;
                    prog.Tag = stData;

                    //Hinzufügen
                    FlowLayoutPanelServerProgressBars.Controls.Add(prog);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     AddServerClientToFlowLayoutPanel_ServerListenButtons
        /// </summary>
        /// <param name="stData"></param>
        private void AddServerClientToFlowLayoutPanel_ServerListenButtons(ServerThreadData stData)
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    //Button Listen erstellen
                    var btnListen = new Button();
                    btnListen.Width = 26;
                    btnListen.Height = 27;
                    btnListen.Margin = new Padding(0, FlowLayoutPanelServerListen.Controls.Count > 0 ? 3 : 8, 0, 3);
                    btnListen.Tag = stData;
                    btnListen.BackColor = Color.LightGray;
                    btnListen.ImageAlign = ContentAlignment.MiddleCenter;
                    btnListen.Image = Resources.Listen_On_Small;
                    btnListen.Tag = stData;
                    btnListen.MouseClick += OnButtonServerThreadListenClick;

                    //Hinzufügen
                    FlowLayoutPanelServerListen.Controls.Add(btnListen);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     AddServerClientToFlowLayoutPanel_ServerSpeakButtons
        /// </summary>
        /// <param name="stData"></param>
        private void AddServerClientToFlowLayoutPanel_ServerSpeakButtons(ServerThreadData stData)
        {
            try
            {
                FlowLayoutPanelServerSpeak.Invoke(new MethodInvoker(delegate
                {
                    //Button Listen erstellen
                    var btnSpeak = new Button();
                    btnSpeak.Width = 26;
                    btnSpeak.Height = 27;
                    btnSpeak.Margin = new Padding(0, FlowLayoutPanelServerSpeak.Controls.Count > 0 ? 3 : 8, 0, 3);
                    btnSpeak.Tag = stData;
                    btnSpeak.ImageAlign = ContentAlignment.MiddleCenter;
                    btnSpeak.BackColor = Color.LightGray;
                    btnSpeak.Image = Resources.Speak_On_Small;
                    btnSpeak.Tag = stData;
                    btnSpeak.MouseClick += OnButtonServerThreadSpeakClick;

                    //Hinzufügen
                    FlowLayoutPanelServerSpeak.Controls.Add(btnSpeak);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     OnButtonServerThreadListenClick
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnButtonServerThreadListenClick(object sender, MouseEventArgs e)
        {
            try
            {
                var btn = (Button) sender;
                if (btn.Tag != null)
                {
                    var data = (ServerThreadData) btn.Tag;
                    //Mute toggeln
                    data.IsMute = !data.IsMute;

                    //Anzeigen
                    if (data.IsMute)
                        btn.Image = Resources.Listen_Off_Small;
                    else
                        btn.Image = Resources.Listen_On_Small;
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelServer, ex.Message);
            }
        }

        /// <summary>
        ///     OnButtonServerThreadSpeakClick
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnButtonServerThreadSpeakClick(object sender, MouseEventArgs e)
        {
            try
            {
                var btn = (Button) sender;
                if (btn.Tag != null)
                {
                    var data = (ServerThreadData) btn.Tag;
                    //Mute toggeln
                    data.ServerThread.IsMute = !data.ServerThread.IsMute;

                    //Anzeigen
                    if (data.ServerThread.IsMute)
                        btn.Image = Resources.Speak_Off_Small;
                    else
                        btn.Image = Resources.Speak_On_Small;
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelServer, ex.Message);
            }
        }

        /// <summary>
        ///     RemoveServerClientToFlowLayoutPanel_ServerProgressBar
        /// </summary>
        /// <param name="st"></param>
        private void RemoveServerClientToFlowLayoutPanel_ServerProgressBar(ServerThreadData data)
        {
            try
            {
                FlowLayoutPanelServerProgressBars.Invoke(new MethodInvoker(delegate
                {
                    //ProgressBar löschen
                    RemoveControlByTag(FlowLayoutPanelServerProgressBars.Controls, data);
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     OnServerDataReceived
        /// </summary>
        /// <param name="st"></param>
        /// <param name="data"></param>
        private void OnServerDataReceived(ServerThread st, byte[] data)
        {
            //Wenn vorhanden
            if (m_DictionaryServerDatas.ContainsKey(st))
            {
                //Wenn Protocol
                var stData = m_DictionaryServerDatas[st];
                if (stData.Protocol != null)
                    stData.Protocol.Receive_LH(st, data);
            }
        }

        /// <summary>
        ///     DeleteAllServerThreadDatas
        /// </summary>
        private void DeleteAllServerThreadDatas()
        {
            lock (LockerDictionary)
            {
                try
                {
                    foreach (var info in m_DictionaryServerDatas.Values)
                        info.Dispose();
                    m_DictionaryServerDatas.Clear();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        /// <summary>
        ///     ShowClientConnected
        /// </summary>
        private void ShowClientConnected()
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    ButtonClient.BackColor = Color.DarkGreen;
                    ButtonClient.ForeColor = Color.White;
                    TextBoxClientAddress.Enabled = false;
                    TextBoxClientPort.Enabled = false;
                    NumericUpDownJitterBufferClient.Enabled = false;
                    ComboboxOutputSoundDeviceNameClient.Enabled = false;
                    ComboboxInputSoundDeviceNameClient.Enabled = false;
                    ProgressBarPlayingClient.Visible = true;
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     ShowClientDisconnected
        /// </summary>
        private void ShowClientDisconnected()
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    ButtonClient.BackColor = Color.Gray;
                    ButtonClient.ForeColor = Color.Black;
                    TextBoxClientAddress.Enabled = true;
                    TextBoxClientPort.Enabled = true;
                    NumericUpDownJitterBufferClient.Enabled = true;
                    ComboboxOutputSoundDeviceNameClient.Enabled = true;
                    ComboboxInputSoundDeviceNameClient.Enabled = true;
                    ProgressBarPlayingClient.Visible = false;
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     ShowServerStarted
        /// </summary>
        private void ShowServerStarted()
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    ButtonServer.BackColor = Color.DarkGreen;
                    ButtonServer.ForeColor = Color.White;
                    NumericUpDownJitterBufferServer.Enabled = false;
                    ComboboxOutputSoundDeviceNameServer.Enabled = false;
                    ComboboxInputSoundDeviceNameServer.Enabled = false;
                    TextBoxServerAddress.Enabled = false;
                    TextBoxServerPort.Enabled = false;
                    ComboboxSamplesPerSecondServer.Enabled = false;
                    StartTimerDrawProgressBar();
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     ShowServerStopped
        /// </summary>
        private void ShowServerStopped()
        {
            try
            {
                Invoke(new MethodInvoker(delegate
                {
                    ButtonServer.BackColor = Color.Gray;
                    ButtonServer.ForeColor = Color.Black;
                    StopTimerDrawProgressBar();
                    FlowLayoutPanelServerClients.Controls.Clear();
                    FlowLayoutPanelServerProgressBars.Controls.Clear();
                    FlowLayoutPanelServerListen.Controls.Clear();
                    FlowLayoutPanelServerSpeak.Controls.Clear();
                    NumericUpDownJitterBufferServer.Enabled = true;
                    ComboboxOutputSoundDeviceNameServer.Enabled = true;
                    ComboboxInputSoundDeviceNameServer.Enabled = true;
                    TextBoxServerAddress.Enabled = true;
                    TextBoxServerPort.Enabled = true;
                    ComboboxSamplesPerSecondServer.Enabled = true;
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     ShowStreamingFromSounddeviceStarted
        /// </summary>
        private void ShowStreamingFromSounddeviceStarted_Client()
        {
            try
            {
                if (InvokeRequired)
                    Invoke(new MethodInvoker(delegate { ShowStreamingFromSounddeviceStarted_Client(); }));
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     ShowStreamingFromSounddeviceStopped_Client
        /// </summary>
        private void ShowStreamingFromSounddeviceStopped_Client()
        {
            try
            {
                if (InvokeRequired)
                    Invoke(new MethodInvoker(delegate { ShowStreamingFromSounddeviceStopped_Client(); }));
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     ShowStreamingFromSounddeviceStarted_Server
        /// </summary>
        private void ShowStreamingFromSounddeviceStarted_Server()
        {
            try
            {
                if (InvokeRequired)
                    Invoke(new MethodInvoker(delegate { ShowStreamingFromSounddeviceStarted_Server(); }));
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     ShowStreamingFromSounddeviceStopped_Server
        /// </summary>
        private void ShowStreamingFromSounddeviceStopped_Server()
        {
            try
            {
                if (InvokeRequired)
                    Invoke(new MethodInvoker(delegate { ShowStreamingFromSounddeviceStopped_Server(); }));
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     ShowStreamingFromFileStarted
        /// </summary>
        private void ShowStreamingFromFileStarted()
        {
            try
            {
                if (InvokeRequired)
                {
                    Invoke(new MethodInvoker(delegate { ShowStreamingFromFileStarted(); }));
                }
                else
                {
                    ComboboxInputSoundDeviceNameClient.Enabled = false;
                    ProgressBarPlayingClient.Visible = true;
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     ShowStreamingFromFileStopped
        /// </summary>
        private void ShowStreamingFromFileStopped()
        {
            try
            {
                if (InvokeRequired)
                {
                    Invoke(new MethodInvoker(delegate { ShowStreamingFromFileStopped(); }));
                }
                else
                {
                    ComboboxInputSoundDeviceNameClient.Enabled = true;
                    ProgressBarPlayingClient.Visible = false;
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     ShowError
        /// </summary>
        /// <param name="lb"></param>
        /// <param name="text"></param>
        private void ShowError(Label lb, string text)
        {
            try
            {
                lb.Invoke(new MethodInvoker(delegate
                {
                    lb.Text = text;
                    lb.ForeColor = Color.Red;

                    //Je nach Quelle
                    if (lb == LabelClient)
                        ButtonClient.BackColor = Color.Red;
                    else if (lb == LabelServer)
                        ButtonServer.BackColor = Color.Red;
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     ShowInfo
        /// </summary>
        /// <param name="lb"></param>
        /// <param name="text"></param>
        private void ShowMessage(Label lb, string text)
        {
            try
            {
                lb.Invoke(new MethodInvoker(delegate
                {
                    lb.Text = text;
                    lb.ForeColor = Color.Black;
                }));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     FormToConfig
        /// </summary>
        /// <returns></returns>
        private bool FormToConfig()
        {
            try
            {
                m_Config.IpAddressClient = TextBoxClientAddress.Text;
                m_Config.IPAddressServer = TextBoxServerAddress.Text;
                m_Config.PortClient = Convert.ToInt32(TextBoxClientPort.Text);
                m_Config.PortServer = Convert.ToInt32(TextBoxServerPort.Text);
                m_Config.SoundInputDeviceNameClient = ComboboxInputSoundDeviceNameClient.SelectedIndex >= 0
                    ? ComboboxInputSoundDeviceNameClient.SelectedItem.ToString()
                    : "";
                m_Config.SoundOutputDeviceNameClient = ComboboxOutputSoundDeviceNameClient.SelectedIndex >= 0
                    ? ComboboxOutputSoundDeviceNameClient.SelectedItem.ToString()
                    : "";
                m_Config.SoundInputDeviceNameServer = ComboboxInputSoundDeviceNameServer.SelectedIndex >= 0
                    ? ComboboxInputSoundDeviceNameServer.SelectedItem.ToString()
                    : "";
                m_Config.SoundOutputDeviceNameServer = ComboboxOutputSoundDeviceNameServer.SelectedIndex >= 0
                    ? ComboboxOutputSoundDeviceNameServer.SelectedItem.ToString()
                    : "";
                m_Config.JitterBufferCountServer = (uint) NumericUpDownJitterBufferServer.Value;
                m_Config.JitterBufferCountClient = (uint) NumericUpDownJitterBufferClient.Value;
                m_Config.SamplesPerSecondServer = ComboboxSamplesPerSecondServer.SelectedIndex >= 0
                    ? Convert.ToInt32(ComboboxSamplesPerSecondServer.SelectedItem.ToString())
                    : 8000;
                m_Config.BitsPerSampleServer = 16;
                m_Config.BitsPerSampleClient = 16;
                m_Config.ChannelsServer = 1;
                m_Config.ChannelsClient = 1;
                m_Config.UseJitterBufferClientRecording = true;
                m_Config.UseJitterBufferServerRecording = true;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Fehler bei der Eingabe", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        /// <summary>
        ///     ConfigToForm
        /// </summary>
        /// <returns></returns>
        private bool ConfigToForm()
        {
            try
            {
                TextBoxClientAddress.Text = m_Config.IpAddressClient;
                TextBoxServerAddress.Text = m_Config.IPAddressServer;
                TextBoxClientPort.Text = m_Config.PortClient.ToString();
                TextBoxServerPort.Text = m_Config.PortServer.ToString();
                ComboboxInputSoundDeviceNameClient.SelectedIndex =
                    ComboboxInputSoundDeviceNameClient.FindString(m_Config.SoundInputDeviceNameClient);
                ComboboxOutputSoundDeviceNameClient.SelectedIndex =
                    ComboboxOutputSoundDeviceNameClient.FindString(m_Config.SoundOutputDeviceNameClient);
                ComboboxInputSoundDeviceNameServer.SelectedIndex =
                    ComboboxInputSoundDeviceNameServer.FindString(m_Config.SoundInputDeviceNameServer);
                ComboboxOutputSoundDeviceNameServer.SelectedIndex =
                    ComboboxOutputSoundDeviceNameServer.FindString(m_Config.SoundOutputDeviceNameServer);
                NumericUpDownJitterBufferServer.Value = m_Config.JitterBufferCountServer;
                NumericUpDownJitterBufferClient.Value = m_Config.JitterBufferCountClient;
                ComboboxSamplesPerSecondServer.SelectedIndex =
                    ComboboxSamplesPerSecondServer.FindString(m_Config.SamplesPerSecondServer.ToString());

                //Sonstiges
                ShowButtonServerSpeak();
                ShowButtonClientListen();
                ShowButtonServerListen();
                ShowButtonClientSpeak();

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return false;
            }
        }

        //----------------------------------------------------------------
        //Daten schreiben
        //----------------------------------------------------------------
        private void SaveConfig()
        {
            try
            {
                FormToConfig();
                var ser = new XmlSerializer(typeof(Configuration));
                var stream = new FileStream(m_ConfigFileName, FileMode.Create);
                ser.Serialize(stream, m_Config);
                stream.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        //----------------------------------------------------------------
        //Daten lesen
        //---------------------------------------------------------------- 
        private void LoadConfig()
        {
            try
            {
                //Wenn die Datei existiert
                if (File.Exists(m_ConfigFileName))
                {
                    var ser = new XmlSerializer(typeof(Configuration));
                    var sr = new StreamReader(m_ConfigFileName);
                    m_Config = (Configuration) ser.Deserialize(sr);
                    sr.Close();
                }

                //Daten anzeigen
                ConfigToForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        /// <summary>
        ///     FormMain_FormClosing
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                //Form ist geschlossen
                m_IsFormMain = false;

                //Aufnahme beenden
                StopRecordingFromSounddevice_Server();
                //Streamen von Sounddevice beenden
                StopRecordingFromSounddevice_Client();
                //Client beenden
                DisconnectClient();
                //Server beenden
                StopServer();

                //Speichern
                SaveConfig();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }

        /// <summary>
        ///     ButtonClient_Click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonClient_Click(object sender, EventArgs e)
        {
            try
            {
                //Daten holen
                FormToConfig();

                if (IsClientConnected)
                {
                    DisconnectClient();
                    StopRecordingFromSounddevice_Client();
                }
                else
                {
                    ConnectClient();
                }

                //Kurz warten
                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }

        /// <summary>
        ///     ButtonServer_Click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonServer_Click(object sender, EventArgs e)
        {
            try
            {
                //Daten holen
                FormToConfig();

                if (IsServerRunning)
                {
                    StopServer();
                    StopRecordingFromSounddevice_Server();
                    StopTimerMixed();
                }
                else
                {
                    StartServer();

                    //Wenn aktiv
                    if (m_Config.ServerNoSpeakAll == false)
                        StartRecordingFromSounddevice_Server();

                    StartTimerMixed();
                }
            }
            catch (Exception ex)
            {
                ShowError(LabelServer, ex.Message);
            }
        }

        /// <summary>
        ///     NumericUpDownJitterBufferServer_ValueChanged
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NumericUpDownJitterBufferServer_ValueChanged(object sender, EventArgs e)
        {
            m_Config.JitterBufferCountServer = (uint) NumericUpDownJitterBufferServer.Value;
        }

        /// <summary>
        ///     NumericUpDownJitterBufferClient_ValueChanged
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void NumericUpDownJitterBufferClient_ValueChanged(object sender, EventArgs e)
        {
            m_Config.JitterBufferCountClient = (uint) NumericUpDownJitterBufferClient.Value;
        }

        /// <summary>
        ///     StopStreamSounddevice_Client
        /// </summary>
        private void StopStreamSounddevice_Client()
        {
            StopRecordingFromSounddevice_Client();
        }

        /// <summary>
        ///     StartPlayingToSounddevice_Client
        /// </summary>
        private void StartPlayingToSounddevice_Client()
        {
            //Wenn gewünscht
            if (IsPlayingToSoundDeviceWanted)
            {
                //JitterBuffer starten
                if (m_JitterBufferClientPlaying != null)
                {
                    InitJitterBufferClientPlaying();
                    m_JitterBufferClientPlaying.Start();
                }

                if (m_PlayerClient == null)
                {
                    m_PlayerClient = new Player();
                    m_PlayerClient.Open(m_Config.SoundOutputDeviceNameClient, m_Config.SamplesPerSecondClient,
                        m_Config.BitsPerSampleClient, m_Config.ChannelsClient, (int) m_Config.JitterBufferCountClient);
                }

                //Timer starten
                m_TimerProgressBarPlayingClient.Start();
            }

            //Anzeigen
            ComboboxOutputSoundDeviceNameClient.Invoke(new MethodInvoker(delegate
            {
                ComboboxOutputSoundDeviceNameClient.Enabled = false;
                NumericUpDownJitterBufferClient.Enabled = false;
                ProgressBarPlayingClient.Maximum = (int) m_JitterBufferClientPlaying.Maximum;
            }));
        }

        /// <summary>
        ///     StopPlayingToSounddevice_Client
        /// </summary>
        private void StopPlayingToSounddevice_Client()
        {
            if (m_PlayerClient != null)
            {
                m_PlayerClient.Close();
                m_PlayerClient = null;
            }

            //JitterBuffer beenden
            if (m_JitterBufferClientPlaying != null)
                m_JitterBufferClientPlaying.Stop();

            //Timer beenden
            m_TimerProgressBarPlayingClient.Stop();

            //Anzeigen
            Invoke(new MethodInvoker(delegate
            {
                ComboboxOutputSoundDeviceNameClient.Enabled = true;
                NumericUpDownJitterBufferClient.Enabled = true;
                ProgressBarPlayingClient.Value = 0;
            }));
        }

        /// <summary>
        ///     ButtonServerSpeak_Click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonServerSpeak_Click(object sender, EventArgs e)
        {
            //Toggeln
            m_Config.ServerNoSpeakAll = !m_Config.ServerNoSpeakAll;

            //Je nach Zustand
            if (m_Config.ServerNoSpeakAll)
                StopRecordingFromSounddevice_Server();
            else
                StartRecordingFromSounddevice_Server();

            //Anzeigen
            ShowButtonServerSpeak();
        }

        /// <summary>
        ///     ShowButtonServerSpeak
        /// </summary>
        private void ShowButtonServerSpeak()
        {
            if (m_Config.ServerNoSpeakAll)
                ButtonServerSpeak.Image = Resources.Speak_Off;
            else
                ButtonServerSpeak.Image = Resources.Speak_On;
        }

        /// <summary>
        ///     ShowButtonClientSpeak
        /// </summary>
        private void ShowButtonClientSpeak()
        {
            if (m_Config.ClientNoSpeakAll)
                ButtonClientSpeak.Image = Resources.Speak_Off;
            else
                ButtonClientSpeak.Image = Resources.Speak_On;
        }

        /// <summary>
        ///     ButtonClientListen_Click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonClientListen_Click(object sender, EventArgs e)
        {
            //Toggeln
            m_Config.MuteClientPlaying = !m_Config.MuteClientPlaying;
            //Anzeigen
            ShowButtonClientListen();
        }

        /// <summary>
        ///     ShowButtonServerSpeak
        /// </summary>
        private void ShowButtonClientListen()
        {
            if (m_Config.MuteClientPlaying)
                ButtonClientListen.Image = Resources.Listen_Off;
            else
                ButtonClientListen.Image = Resources.Listen_On;
        }

        /// <summary>
        ///     ButtonServerListen_Click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonServerListen_Click(object sender, EventArgs e)
        {
            //Toggeln
            m_Config.MuteServerListen = !m_Config.MuteServerListen;

            //Anzeigen
            ShowButtonServerListen();
        }

        /// <summary>
        ///     ShowButtonServerListen
        /// </summary>
        private void ShowButtonServerListen()
        {
            if (m_Config.MuteServerListen)
                ButtonServerListen.Image = Resources.Listen_Off;
            else
                ButtonServerListen.Image = Resources.Listen_On;

            //Speichern
            ServerThreadData.IsMuteAll = m_Config.MuteServerListen;
        }

        /// <summary>
        ///     ButtonClientSpeak_Click
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ButtonClientSpeak_Click(object sender, EventArgs e)
        {
            try
            {
                //Toggeln
                m_Config.ClientNoSpeakAll = !m_Config.ClientNoSpeakAll;
                //Anzeigen
                ShowButtonClientSpeak();
            }
            catch (Exception ex)
            {
                ShowError(LabelClient, ex.Message);
            }
        }
    }

    /// <summary>
    ///     Config
    /// </summary>
    public class Configuration
    {
        public int BitsPerSampleClient = 16;
        public int BitsPerSampleServer = 16;
        public int ChannelsClient = 1;
        public int ChannelsServer = 1;
        public bool ClientNoSpeakAll;
        public string FileName = "";

        //Attribute
        public string IpAddressClient = "";

        public string IPAddressServer = "";
        public uint JitterBufferCountClient = 20;
        public uint JitterBufferCountServer = 20;
        public bool LoopFile = false;
        public bool MuteClientPlaying;
        public bool MuteServerListen;
        public int PortClient;
        public int PortServer;
        public int SamplesPerSecondClient = 8000;
        public int SamplesPerSecondServer = 8000;
        public bool ServerNoSpeakAll;
        public string SoundInputDeviceNameClient = "";
        public string SoundInputDeviceNameServer = "";
        public string SoundOutputDeviceNameClient = "";
        public string SoundOutputDeviceNameServer = "";
        public bool UseJitterBufferClientRecording = true;
        public bool UseJitterBufferServerRecording = true;
    }

    /// <summary>
    ///     ServerThreadData
    /// </summary>
    public class ServerThreadData
    {
        public static bool IsMuteAll;
        public int BitsPerSample = 16;
        public int Channels = 1;
        private bool IsInitialized;
        public bool IsMute;
        public JitterBuffer JitterBuffer;
        public uint JitterBufferCount = 20;
        public uint JitterBufferMilliseconds = 20;
        public Player Player;
        public Protocol Protocol;
        public int SamplesPerSecond = 8000;

        //Attribute
        public ServerThread ServerThread;

        public int SoundBufferCount = 8;

        /// <summary>
        ///     Init
        /// </summary>
        /// <param name="bitsPerSample"></param>
        /// <param name="channels"></param>
        public void Init(ServerThread st, string soundDeviceName, int samplesPerSecond, int bitsPerSample, int channels,
            int soundBufferCount, uint jitterBufferCount, uint jitterBufferMilliseconds)
        {
            //Werte übernehmen
            ServerThread = st;
            SamplesPerSecond = samplesPerSecond;
            BitsPerSample = bitsPerSample;
            Channels = channels;
            SoundBufferCount = soundBufferCount;
            JitterBufferCount = jitterBufferCount;
            JitterBufferMilliseconds = jitterBufferMilliseconds;

            //Player
            Player = new Player();
            Player.Open(soundDeviceName, samplesPerSecond, bitsPerSample, channels, soundBufferCount);

            //Wenn ein JitterBuffer verwendet werden soll
            if (jitterBufferCount >= 2)
            {
                //Neuen JitterBuffer erstellen
                JitterBuffer = new JitterBuffer(st, jitterBufferCount, jitterBufferMilliseconds);
                JitterBuffer.DataAvailable += OnJitterBufferDataAvailable;
                JitterBuffer.Start();
            }

            //Protocol
            Protocol = new Protocol(ProtocolTypes.LH, Encoding.Default);
            Protocol.DataComplete += OnProtocolDataComplete;

            //Zu Mixer hinzufügen
            FormMain.DictionaryMixed[st] = new Queue<List<byte>>();

            //Initialisiert
            IsInitialized = true;
        }

        /// <summary>
        ///     Dispose
        /// </summary>
        public void Dispose()
        {
            //Protocol
            if (Protocol != null)
            {
                Protocol.DataComplete -= OnProtocolDataComplete;
                Protocol = null;
            }

            //JitterBuffer
            if (JitterBuffer != null)
            {
                JitterBuffer.Stop();
                JitterBuffer.DataAvailable -= OnJitterBufferDataAvailable;
                JitterBuffer = null;
            }

            //Player
            if (Player != null)
            {
                Player.Close();
                Player = null;
            }

            //Nicht initialisiert
            IsInitialized = false;
        }

        /// <summary>
        ///     OnProtocolDataComplete
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="data"></param>
        private void OnProtocolDataComplete(object sender, byte[] bytes)
        {
            //Wenn initialisiert
            if (IsInitialized)
                if (ServerThread != null && Player != null)
                    try
                    {
                        //Wenn der Player gestartet wurde
                        if (Player.Opened)
                        {
                            //RTP Header auslesen
                            var rtp = new RTPPacket(bytes);

                            //Wenn Header korrekt
                            if (rtp.Data != null)
                                if (JitterBuffer != null && JitterBuffer.Maximum >= 2)
                                {
                                    JitterBuffer.AddData(rtp);
                                }
                                else
                                {
                                    //Wenn kein Mute
                                    if (IsMuteAll == false && IsMute == false)
                                    {
                                        //Nach Linear umwandeln
                                        var linearBytes = Utils.MuLawToLinear(rtp.Data, BitsPerSample, Channels);
                                        //Abspielen
                                        Player.PlayData(linearBytes, false);
                                    }
                                }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                        IsInitialized = false;
                    }
        }

        /// <summary>
        ///     OnJitterBufferDataAvailable
        /// </summary>
        /// <param name="packet"></param>
        private void OnJitterBufferDataAvailable(object sender, RTPPacket rtp)
        {
            try
            {
                if (Player != null)
                {
                    //Nach Linear umwandeln
                    var linearBytes = Utils.MuLawToLinear(rtp.Data, BitsPerSample, Channels);

                    //Wenn kein Mute
                    if (IsMuteAll == false && IsMute == false)
                        Player.PlayData(linearBytes, false);

                    //Wenn Buffer nicht zu gross
                    var q = FormMain.DictionaryMixed[sender];
                    if (q.Count < 10)
                        FormMain.DictionaryMixed[sender].Enqueue(new List<byte>(linearBytes));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FormMain.cs | OnJitterBufferDataAvailable() | {0}", ex.Message);
            }
        }
    }
}