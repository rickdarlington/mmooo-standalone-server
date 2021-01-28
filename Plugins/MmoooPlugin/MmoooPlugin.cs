using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using DarkRift;
using DarkRift.Server;
using Shared;

namespace MmoooPlugin
{
    public class Server : Plugin
    {
        public Server Instance;
        public uint ServerTick;

        public override bool ThreadSafe => false;

        public override Version Version => new Version(1, 0, 0);

        public Dictionary<ushort, PlayerConnection> Players = new Dictionary<ushort, PlayerConnection>();
        public Dictionary<string, PlayerConnection> PlayersByName = new Dictionary<string, PlayerConnection>();

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

        public Vector2 ServerPosition = Vector2.Zero;

        public uint InputTick;

        public Buffer<NetworkingData.PlayerInputData> inputBuffer = new Buffer<NetworkingData.PlayerInputData>(1, 2);    
        
        public List<NetworkingData.PlayerStateData> PlayerStateDataHistory { get; } = new List<NetworkingData.PlayerStateData>();
        
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
                        inputBuffer.Add(message.Deserialize<NetworkingData.PlayerInputData>());
                        break;
                    default:
                        logger.Info($"PlayerConnection Unhandled tag: {message.Tag}");
                        break;
                }
            }
        }

        private void SendGameStart(IClient client)
        {
            InputTick = ServerInstance.ServerTick;
            
            NetworkingData.PlayerSpawnData[] players = new NetworkingData.PlayerSpawnData[2];
            players[0] = new NetworkingData.PlayerSpawnData(client.ID, Name, ServerPosition);
            players[1] = new NetworkingData.PlayerSpawnData(99, "agent 99", Vector2.Zero);
            
            using (Message m = Message.Create((ushort) NetworkingData.Tags.GameStartData,
                new NetworkingData.GameStartData(getAllPlayersSpawnData(), ServerInstance.ServerTick)))
            {
                logger.Info("Sending Game Start Data");
                
                client.SendMessage(m, SendMode.Reliable);
            }
        }

        private NetworkingData.PlayerSpawnData[] getAllPlayersSpawnData()
        {
            NetworkingData.PlayerSpawnData[] playerSpawnDatas = new NetworkingData.PlayerSpawnData[ServerInstance.Players.Count];
            int i = 0;
            foreach (KeyValuePair<string, PlayerConnection> entry in ServerInstance.PlayersByName)
            {
                playerSpawnDatas[i] =
                    new NetworkingData.PlayerSpawnData(entry.Value.Client.ID, entry.Value.Name, entry.Value.ServerPosition);
                i++;
            }

            return playerSpawnDatas;
        }
    }
    
    public class GameLogic
    {
        private Logger logger;
        private static int deltaTime = 100;
        
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

                NetworkingData.PlayerStateData[] allPlayerPositions = UpdatePlayerPositions(server.Instance.Players);
                SendPlayerStateDataUpdates(server.Instance.Players, allPlayerPositions);
                
                long currentTimeMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                long processingTimeMS = currentTimeMS - startTimeMS;
                if (processingTimeMS > deltaTime)
                {
                    logger.Error($"Time to optimize some game logic or get better hardware! Update took: {processingTimeMS} ms");
                }
                
                Thread.Sleep(deltaTime - (int) processingTimeMS);
            }
        }

        public static NetworkingData.PlayerStateData[] UpdatePlayerPositions(Dictionary<ushort, PlayerConnection> players)
        {
            NetworkingData.PlayerStateData[] allPlayerPositions = new NetworkingData.PlayerStateData[players.Count];

            int z = 0;
            foreach (KeyValuePair<ushort, PlayerConnection> p in players)
            {
                PlayerConnection player = p.Value;
                var inputs = player.inputBuffer.Get();
                
                if (inputs.Length > 0)
                {
                    NetworkingData.PlayerInputData input = inputs.First();
                    player.InputTick++;

                    for (int i = 1; i < inputs.Length; i++)
                    {
                        player.InputTick++;
                        for (int j = 0; j < input.Keyinputs.Length; j++)
                        {
                            input.Keyinputs[j] = input.Keyinputs[j] || inputs[i].Keyinputs[j];
                        }
                    }
    
                    Vector2 moveDirection = FrameData.GetNextFrameData(input, deltaTime/100);

                    player.ServerPosition = Vector2.Add(player.ServerPosition, moveDirection);
                }

                NetworkingData.PlayerStateData currentState =
                    new NetworkingData.PlayerStateData(player.Client.ID, player.ServerPosition, 0);
                player.PlayerStateDataHistory.Add(currentState);
                if (player.PlayerStateDataHistory.Count > 10)
                {
                    player.PlayerStateDataHistory.RemoveAt(0);
                }

                allPlayerPositions[z] = currentState;
                z++;
            }

            return allPlayerPositions;
        }

        public static void SendPlayerStateDataUpdates(Dictionary<ushort, PlayerConnection> players, NetworkingData.PlayerStateData[] positions)
        {
            foreach (KeyValuePair<ushort, PlayerConnection> p in players)
            {
                using (Message m = Message.Create(
                    (ushort)NetworkingData.Tags.GameUpdate, 
                    new NetworkingData.GameUpdateData(p.Value.InputTick, positions, new NetworkingData.PlayerSpawnData[]{}, new NetworkingData.PlayerDespawnData[]{})))
                {
                    p.Value.Client.SendMessage(m, SendMode.Reliable);
                }
            }
        }
    }
}