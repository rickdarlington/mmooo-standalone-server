using System;
using System.Collections.Generic;

using System.Threading;
using DarkRift;
using DarkRift.Server;
using MmoooPlugin.Shared;

namespace MmoooPlugin
{
    public sealed class Server : Plugin
    {
        public override Version Version => new Version(1, 0, 0);
        public override bool ThreadSafe => true;
        
        private static Server instance;
        public static Server Instance => instance;
        
        public uint ServerTick;
        
        public readonly Dictionary<ushort, Player> Players = new Dictionary<ushort, Player>();
        public readonly Dictionary<string, Player> PlayersByName = new Dictionary<string, Player>();

        public Server(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            instance = this;
            ClientManager.ClientConnected += ConnectionManager.Connected;
            ClientManager.ClientDisconnected += ConnectionManager.Disconnected;

            new GameLogic(this);
        }
        
        public void AddPlayer(ushort id, string name, Player p)
        {
            Players.Add(id, p);
            PlayersByName.Add(name, p);
            SendPlayerSpawnToAll(p);
            Logger.Info($"Adding player \"{name}\" (id: {id})");
            Logger.Info($"{Players.Count} players online.");
        }

        private void SendPlayerSpawnToAll(Player p)
        {
            foreach (KeyValuePair<ushort, Player> kv in Players)
            {
                using (Message m = Message.Create((ushort) NetworkingData.Tags.PlayerSpawn,
                    new NetworkingData.PlayerSpawnData(p.Client.ID, p.Name, p.ServerPosition)))
                {
                    kv.Value.SendMessage(m, SendMode.Reliable);
                }
            }
        }

        public void RemovePlayer(ushort id, String name)
        {
            Players.Remove(id);
            PlayersByName.Remove(name);
            SendPlayerDespawnToAll(id);
            Logger.Info($"removing player {name} (id: {id})");
            Logger.Info($"{Players.Count} players online.");
        }
        
        private void SendPlayerDespawnToAll(ushort id)
        {
            foreach (KeyValuePair<ushort, Player> kv in Players)
            {
                using (Message m = Message.Create((ushort) NetworkingData.Tags.PlayerDeSpawn,
                    new NetworkingData.PlayerDespawnData(id)))
                {
                    kv.Value.SendMessage(m, SendMode.Reliable);
                }
            }
        }
    }
    
    public class GameLogic
    {
        private Logger logger = Server.Instance.LogManager.GetLoggerFor("GameLogic");
        public GameLogic(Server s)
        {
            Thread t = new Thread(GameLoop);
            t.Start();
        }

        void GameLoop()
        {
            long tickRate = 1000 / 100; //10 ticks per second
            int dt = 0;
            while (true)
            {
                long startTimeMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

                var positionUpdates = UpdatePlayerPositions(Server.Instance.Players, dt);
                SendPlayerStateDataUpdates(positionUpdates);

                long finishTimeMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                long elapsedTimeMS = finishTimeMS - startTimeMS;
                
                if (elapsedTimeMS < tickRate)
                {
                    dt = (int)tickRate - (int)elapsedTimeMS;
                    Thread.Sleep(dt);
                }
                else
                {
                    logger.Error($"Game loop update took longer ({elapsedTimeMS}) than tick {tickRate}");
                }
            }
        }

        static NetworkingData.PlayerStateData[] UpdatePlayerPositions(Dictionary<ushort, Player> players, int dt)
        {
            NetworkingData.PlayerStateData[] updates = new NetworkingData.PlayerStateData[players.Count];

            int i = 0;
            foreach (KeyValuePair<ushort, Player> kv in players)
            {
                uint lastProcessedInput = 0;
                Player player = kv.Value;
                if (player.inputBuffer.Count > 0)
                {
                    int numInputs = player.inputBuffer.Count;
                    for (int j = 0; j < numInputs; j++)
                    {
                        NetworkingData.PlayerInputData nextInput = player.inputBuffer.Dequeue();
                        float timeStep = ((float)dt / 1000) / numInputs;
                        
                        player.ServerPosition = PlayerMovement.MovePlayer(nextInput, player.ServerPosition, timeStep);
                        lastProcessedInput = nextInput.InputSeq;
                    }
                    
                    updates[i] = new NetworkingData.PlayerStateData(player.Client.ID, player.ServerPosition, player.LookDirection, lastProcessedInput);
                    //TODO scroll warning
                    //Server.Instance.LogManager.GetLoggerFor("GameLoop").Info($"{player.clientId} pos {player.ServerPosition.X}, {player.ServerPosition.Y}");
                }
                else
                {
                    updates[i] = new NetworkingData.PlayerStateData(player.Client.ID, player.ServerPosition, player.LookDirection, 0);
                }
                i++;
            }

            return updates;
        }

        static void SendPlayerStateDataUpdates(NetworkingData.PlayerStateData[] positions)
        {
            foreach (KeyValuePair<ushort, Player> p in Server.Instance.Players)
            {
                NetworkingData.GameUpdateData data = new NetworkingData.GameUpdateData(positions);
                //Server.Instance.LogManager.GetLoggerFor("preserialize").Info(data.toString());
                using (Message m = Message.Create((ushort)NetworkingData.Tags.GameUpdate, data))
                {
                    p.Value.SendMessage(m, SendMode.Reliable);
                    NetworkingData.GameUpdateData gameUpdateData = m.Deserialize<NetworkingData.GameUpdateData>();
                }
            }
        }
    }
}