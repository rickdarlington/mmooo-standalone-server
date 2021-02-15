﻿using System;
using System.Collections.Generic;
using System.Linq;
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
        public byte RandomSpriteRow = 0;
        
        public readonly float playerInputTimestep = 1f / 30f;
        
        public readonly Dictionary<ushort, Player> Players = new Dictionary<ushort, Player>();
        public readonly Dictionary<string, Player> PlayersByName = new Dictionary<string, Player>();

        public Server(PluginLoadData pluginLoadData) : base(pluginLoadData)
        {
            instance = this;
            ClientManager.ClientConnected += ConnectionManager.Connected;
            ClientManager.ClientDisconnected += ConnectionManager.Disconnected;

            new GameLogic();
        }
        
        //TODO refactor: temporary
        public byte getNextSpriteRow()
        {
            RandomSpriteRow++;
            return RandomSpriteRow;
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
                    new NetworkingData.PlayerSpawnData(p.Client.ID, p.Name, p.spriteRow, p.ServerPosition)))
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
        public GameLogic()
        {
            Thread ul = new Thread(StateUpdateLoop);
            Thread al = new Thread(AILoop);
            ul.Start();
            al.Start();
        }

        void AILoop()
        {
            long tickRate = 1000; //1 AI update per second
            int dt = 0;
            while (true)
            {
                long startTimeMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                
                //TODO do AI logic here
                
                long finishTimeMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                long elapsedTimeMS = finishTimeMS - startTimeMS;
                
                if (elapsedTimeMS < tickRate)
                {
                    dt = (int)tickRate - (int)elapsedTimeMS;
                    Thread.Sleep(dt);
                }
                else
                {
                    logger.Error($"AI loop update took longer ({elapsedTimeMS}) than tick {tickRate}");
                }
            }
        }

        void StateUpdateLoop()
        {
            //update loop 
            long tickRate = 100; //10 ticks per second
            int dt = 0;
            while (true)
            {
                long startTimeMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;

                UpdatePlayerPositions(Server.Instance.playerInputTimestep, Server.Instance.Players.Values.ToArray());
                SendPlayerStateDataUpdates();
                
                long finishTimeMS = DateTime.Now.Ticks / TimeSpan.TicksPerMillisecond;
                long elapsedTimeMS = finishTimeMS - startTimeMS;
                
                if (elapsedTimeMS < tickRate)
                {
                    dt = (int)tickRate - (int)elapsedTimeMS;
                    Thread.Sleep(dt);
                }
                else
                {
                    logger.Error($"Position update took longer ({elapsedTimeMS}) than tick {tickRate}");
                }
            }
        }
        
        static void UpdatePlayerPositions(float timeStep, Array players)
        {
            //TODO check if inputbuffer length is > than number they can send at 30fps (drop the extras)
            //TODO if so, they're trying to send inputs too fast (ban)
            foreach (Player player in players)
            {
                uint lastProcessedInput = 0;
                if (player.inputBuffer.Count > 0)
                {
                    int numInputs = player.inputBuffer.Count;
                    for (int j = 0; j < numInputs; j++)
                    {
                        NetworkingData.PlayerInputData nextInput = player.inputBuffer.Dequeue();

                        player.ServerPosition = PlayerMovement.MovePlayer(nextInput, player.ServerPosition, timeStep);
                        player.LookDirection = nextInput.LookDirection;
                        player.LastProcessedInput = nextInput.InputSeq;
                    }
                }
            }
        }
        
        static void SendPlayerStateDataUpdates()
        {
            var players = Server.Instance.Players.Values.ToArray();
            NetworkingData.PlayerStateData[] positions = new NetworkingData.PlayerStateData[players.Length];

            int i = 0;
            foreach (var player in players)
            {
                positions[i] = player.getStateData();
                i++;
            }
            
            foreach (KeyValuePair<ushort, Player> p in Server.Instance.Players)
            {
                NetworkingData.GameUpdateData data = new NetworkingData.GameUpdateData(positions);
                using (Message m = Message.Create((ushort)NetworkingData.Tags.GameUpdate, data))
                {
                    p.Value.SendMessage(m, SendMode.Reliable);
                }
            }
        }
    }
}