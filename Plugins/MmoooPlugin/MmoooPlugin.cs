using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using DarkRift;
using DarkRift.Server;

namespace MmoooPlugin
{
    public class Server : Plugin
    {
        public Server Instance;
        public uint ServerTick;

        public override bool ThreadSafe => false;

        public override Version Version => new Version(1, 0, 0);

        private Dictionary<ushort, PlayerConnection> Players = new Dictionary<ushort, PlayerConnection>();
        private Dictionary<string, PlayerConnection> PlayersByName = new Dictionary<string, PlayerConnection>();

        public Server(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            Instance = this;
            ClientManager.ClientConnected += Connected;
            ClientManager.ClientDisconnected += Disconnected;

            new GameLogic(this);
        }

        void Connected(object sender, ClientConnectedEventArgs args)
        {
            Logger.Info($"{ClientManager.Count} client connections total.");
            args.Client.MessageReceived += OnMessage;
        }

        void Disconnected(object sender, ClientDisconnectedEventArgs args)
        {
            IClient client = args.Client;
            PlayerConnection playerConnection;
            if (Players.TryGetValue(client.ID, out playerConnection))
            {
                Logger.Info($"{playerConnection.Name} disconnected.");
                RemovePlayer(args.Client.ID, playerConnection.Name);
                
                //TODO is this the proper way to deallocate?
                playerConnection = null;
            }

            args.Client.MessageReceived -= OnMessage;
        }

        private void OnMessage(object sender, MessageReceivedEventArgs e)
        {
            IClient client = (IClient) sender;
            using (Message message = e.GetMessage())
            {
                switch ((NetworkingData.Tags) message.Tag)
                {
                    case NetworkingData.Tags.LoginRequest:
                        OnClientLogin(client, message.Deserialize<NetworkingData.LoginRequestData>());
                        break;
                    default:
                        Logger.Info($"Unhandled tag: {message.Tag}");
                        break;
                }
            }
        }

        private void OnClientLogin(IClient client, NetworkingData.LoginRequestData data)
        {
            Logger.Info($"Login request from ({data.Name}).");
            if (PlayersByName.ContainsKey(data.Name))
            {
                using (Message message = Message.CreateEmpty((ushort) NetworkingData.Tags.LoginRequestDenied))
                {
                    Logger.Info($"{data.Name} is already logged in!");
                    client.SendMessage(message, SendMode.Reliable);
                }

                return;
            }
            
            client.MessageReceived -= OnMessage;
            
            PlayerConnection pc = new PlayerConnection(client, data, Instance);
            AddPlayer(client.ID, data.Name, pc);
        }

        private void AddPlayer(ushort id, string name, PlayerConnection pc)
        {
            Players.Add(id, pc);
            PlayersByName.Add(name, pc);
            Logger.Info($"{Players.Count} players online.");
        }

        public void RemovePlayer(ushort id, String name)
        {
            Players.Remove(id);
            PlayersByName.Remove(name);
            Logger.Info($"{Players.Count} players online.");
        }
    }

    public class PlayerConnection
    {
        public string Name { get; }
        public IClient Client { get; }
        
        public Server ServerInstance;
        
        private Logger logger;

        private bool playerReady = false;

        public PlayerConnection(IClient client, NetworkingData.LoginRequestData data, Server serverInstance)
        {
            Client = client;
            Name = data.Name;
            ServerInstance = serverInstance;
            logger = ServerInstance.LogManager.GetLoggerFor("PlayerConnection");
            client.MessageReceived += OnPlayerMessage;

            logger.Info($"Connection for {Name} configured, sending login accept.");
            
            using (Message m = Message.Create((ushort) NetworkingData.Tags.LoginRequestAccepted,
                new NetworkingData.LoginInfoData(client.ID)))
            {
                client.SendMessage(m, SendMode.Reliable);
            }
        }

        private void OnPlayerMessage(object sender, MessageReceivedEventArgs args)
        {
            IClient client = (IClient) sender;
            using (Message message = args.GetMessage())
            {
                switch ((NetworkingData.Tags) message.Tag)
                {
                    case NetworkingData.Tags.PlayerReady:
                        SendGameStart(client);
                        break;
                    case NetworkingData.Tags.GamePlayerInput:
                        logger.Info("player movement");
                        break;
                    default:
                        logger.Info($"PlayerConnection Unhandled tag: {message.Tag}");
                        break;
                }
            }
        }

        private void SendGameStart(IClient client)
        {   
            NetworkingData.PlayerSpawnData[] players = new NetworkingData.PlayerSpawnData[2];
            players[0] = new NetworkingData.PlayerSpawnData(client.ID, Name, new Vector2(0, 0));
            players[1] = new NetworkingData.PlayerSpawnData(99, Name, new Vector2(50, 50));
            
            using (Message m = Message.Create((ushort) NetworkingData.Tags.GameStartData,
                new NetworkingData.GameStartData(players, ServerInstance.ServerTick)))
            {
                logger.Info("Sending Game Start Data");
                
                client.SendMessage(m, SendMode.Reliable);
            }
        }
    }
    
    public class GameLogic
    {
        private Logger logger;
        
        public GameLogic(Server s)
        {
            logger = s.LogManager.GetLoggerFor("GameLogic");
            Thread t = new Thread(()=>GameLoop(s, logger));
            t.Start();
        }
        
        static void GameLoop(Server server, Logger logger)
        {
            while (true)
            {
                server.ServerTick++; 
                //logger.Info($"Tick: {server.ServerTick}");
                long startTimeMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                
                //do game stuff, eg. RunAILogic() or RunCollisionManager()
                
                long currentTimeMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                long processingTimeMS = currentTimeMS - startTimeMS;
                if (processingTimeMS > 1000)
                {
                    logger.Error($"Time to optimize some game logic or get better hardware! Update took: {processingTimeMS} ms");
                }
                
                Thread.Sleep(1000 - (int) processingTimeMS);
            }
        }
    }
}