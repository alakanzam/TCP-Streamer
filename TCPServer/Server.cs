using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NF
{
    /// <summary>
    ///     TCPServer
    /// </summary>
    public class TCPServer
    {
        //Delegates
        public delegate void DelegateClientConnected(ServerThread st);

        public delegate void DelegateClientDisconnected(ServerThread st, string info);

        public delegate void DelegateDataReceived(ServerThread st, byte[] data);

        /// <summary>
        ///     TCPServer Stati
        /// </summary>
        public enum ListenerState
        {
            None,
            Started,
            Stopped,
            Error
        }

        //Attribute
        private IPEndPoint m_endpoint;

        private Thread m_ThreadMainServer;


        //Die Liste der laufenden TCPServer-Threads

        /// <summary>
        ///     Alle Aktuellen Clients des Servers
        /// </summary>
        public List<ServerThread> Clients { get; } = new List<ServerThread>();

        /// <summary>
        ///     Connected
        /// </summary>
        public ListenerState State { get; private set; }

        /// <summary>
        ///     Gibt den inneren TcpListener des Servers zurück
        /// </summary>
        public TcpListener Listener { get; private set; }

        //Events
        public event DelegateClientConnected ClientConnected;

        public event DelegateClientDisconnected ClientDisconnected;
        public event DelegateDataReceived DataReceived;

        /// <summary>
        ///     Starten des Servers
        /// </summary>
        public void Start(string strIPAdress, int Port)
        {
            //Endpoint und Listener bestimmen
            m_endpoint = new IPEndPoint(IPAddress.Parse(strIPAdress), Port);
            Listener = new TcpListener(m_endpoint);

            if (Listener == null) return;

            try
            {
                Listener.Start();

                // Haupt-TCPServer-Thread initialisieren und starten
                m_ThreadMainServer = new Thread(Run);
                m_ThreadMainServer.Start();

                //State setzen
                State = ListenerState.Started;
            }
            catch (Exception ex)
            {
                //Beenden
                Listener.Stop();
                State = ListenerState.Error;

                //Exception werfen
                throw ex;
            }
        }

        /// <summary>
        ///     Run
        /// </summary>
        private void Run()
        {
            while (true)
            {
                //Wartet auf eingehenden Verbindungswunsch
                var client = Listener.AcceptTcpClient();
                //Initialisiert und startet einen TCPServer-Thread
                //und fügt ihn zur Liste der TCPServer-Threads hinzu
                var st = new ServerThread(client);

                //Events hinzufügen
                st.DataReceived += OnDataReceived;
                st.ClientDisconnected += OnClientDisconnected;

                //Weitere Arbeiten
                OnClientConnected(st);

                try
                {
                    //Beginnen zu lesen
                    client.Client.BeginReceive(st.ReadBuffer, 0, st.ReadBuffer.Length, SocketFlags.None, st.Receive,
                        client.Client);
                }
                catch (Exception ex)
                {
                    //Verbindung fehlerhaft
                    Console.WriteLine(ex.Message);
                }
            }
        }

        /// <summary>
        ///     Nachricht an alle verbundenen Clients senden. Gibt die Anzahl der vorhandenen Clients zurück
        /// </summary>
        /// <param name="Message"></param>
        public int Send(byte[] data)
        {
            //Für jede Verbindung
            var list = new List<ServerThread>(Clients);
            foreach (var sv in list)
                try
                {
                    //Senden
                    if (data.Length > 0)
                        sv.Send(data);
                }
                catch (Exception)
                {
                }
            //Anzahl zurückgeben
            return Clients.Count;
        }

        /// <summary>
        ///     Wird ausgeführt wenn Daten angekommen sind
        /// </summary>
        /// <param name="Data"></param>
        private void OnDataReceived(ServerThread st, byte[] data)
        {
            //Event abschicken bzw. weiterleiten
            if (DataReceived != null)
                DataReceived(st, data);
        }

        /// <summary>
        ///     Wird aufgerufen wenn sich ein Client beendet
        /// </summary>
        /// <param name="st"></param>
        private void OnClientDisconnected(ServerThread st, string info)
        {
            //Aus Liste entfernen
            Clients.Remove(st);

            //Event abschicken bzw. weiterleiten
            if (ClientDisconnected != null)
                ClientDisconnected(st, info);
        }

        /// <summary>
        ///     Wird aufgerufen wenn sich ein Client verbindet
        /// </summary>
        /// <param name="st"></param>
        private void OnClientConnected(ServerThread st)
        {
            //Wenn nicht vorhanden
            if (!Clients.Contains(st))
                Clients.Add(st);

            //Event abschicken bzw. weiterleiten
            if (ClientConnected != null)
                ClientConnected(st);
        }

        /// <summary>
        ///     Beenden des Servers
        /// </summary>
        public void Stop()
        {
            try
            {
                if (m_ThreadMainServer != null)
                {
                    // Haupt-TCPServer-Thread stoppen
                    m_ThreadMainServer.Abort();
                    Thread.Sleep(100);
                }

                // Alle TCPServer-Threads stoppen
                for (IEnumerator en = Clients.GetEnumerator(); en.MoveNext();)
                {
                    //Nächsten TCPServer-Thread holen
                    var st = (ServerThread) en.Current;
                    //und stoppen
                    st.Stop();

                    //Event abschicken
                    if (ClientDisconnected != null)
                        ClientDisconnected(st, "Verbindung wurde beendet");
                }

                if (Listener != null)
                {
                    //Listener stoppen
                    Listener.Stop();
                    Listener.Server.Close();
                }

                //Liste leeren
                Clients.Clear();
                //Status vermerken
                State = ListenerState.Stopped;
            }
            catch (Exception)
            {
                State = ListenerState.Error;
            }
        }
    }

    /// <summary>
    ///     ServerThread eines Servers
    /// </summary>
    public class ServerThread
    {
        public delegate void DelegateClientDisconnected(ServerThread sv, string info);

        public delegate void DelegateDataReceived(ServerThread st, byte[] data);

        //Mute
        public bool IsMute = false;

        // Die Verbindung zum Client

        // Stop-Flag

        //Name
        public string Name = "";

        //Lesepuffer
        public byte[] ReadBuffer = new byte[1024];

        // Speichert die Verbindung zum Client und startet den Thread
        public ServerThread(TcpClient connection)
        {
            // Speichert die Verbindung zu Client,
            // um sie später schließen zu können
            Client = connection;
        }

        /// <summary>
        ///     Inneren Client
        /// </summary>
        public TcpClient Client { get; }

        /// <summary>
        ///     Verbindung ist beendet
        /// </summary>
        public bool IsStopped { get; private set; }

        public event DelegateDataReceived DataReceived;
        public event DelegateClientDisconnected ClientDisconnected;

        /// <summary>
        ///     Nachrichten lesen
        /// </summary>
        /// <param name="ar"></param>
        public void Receive(IAsyncResult ar)
        {
            try
            {
                //Wenn nicht mehr verbunden
                if (Client.Client.Connected == false)
                    return;

                if (ar.IsCompleted)
                {
                    //Lesen
                    var bytesRead = Client.Client.EndReceive(ar);

                    //Wenn Daten vorhanden
                    if (bytesRead > 0)
                    {
                        //Nur gelesene Bytes ermitteln
                        var data = new byte[bytesRead];
                        Array.Copy(ReadBuffer, 0, data, 0, bytesRead);

                        //Event abschicken
                        DataReceived(this, data);
                        //Weiter lesen
                        Client.Client.BeginReceive(ReadBuffer, 0, ReadBuffer.Length, SocketFlags.None, Receive,
                            Client.Client);
                    }
                    else
                    {
                        //Verbindung getrennt
                        HandleDisconnection("Verbindung wurde beendet");
                    }
                }
            }
            catch (Exception ex)
            {
                //Verbindung getrennt
                HandleDisconnection(ex.Message);
            }
        }

        /// <summary>
        ///     Alles nötige bei einem Verbindungsabbruch unternehmen
        /// </summary>
        public void HandleDisconnection(string reason)
        {
            //Clientverbindung ist beendet
            IsStopped = true;

            //Event abschicken
            if (ClientDisconnected != null)
                ClientDisconnected(this, reason);
        }

        /// <summary>
        ///     Senden von Nachrichten
        /// </summary>
        /// <param name="strMessage"></param>
        public void Send(byte[] data)
        {
            try
            {
                //Wenn die Verbindung noch besteht
                if (IsStopped == false)
                {
                    //Hole den Stream für's schreiben
                    var ns = Client.GetStream();

                    lock (ns)
                    {
                        // Sende den kodierten string an den TCPServer
                        ns.Write(data, 0, data.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                //Verbindung schliessen
                Client.Close();
                //Verbindung beenden
                IsStopped = true;

                //Event abschicken
                if (ClientDisconnected != null)
                    ClientDisconnected(this, ex.Message);

                //Exception weiterschicken
                throw ex;
            }
        }

        /// <summary>
        ///     Thread anhalten
        /// </summary>
        public void Stop()
        {
            //Wenn ein Client noch verbunden ist
            if (Client.Client.Connected)
                Client.Client.Disconnect(false);

            IsStopped = true;
        }
    }
}