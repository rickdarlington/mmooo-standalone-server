using System.Collections.Generic;
using System.Numerics;
using DarkRift;
using DarkRift.Server;

namespace MmoooPlugin
{
    public class Player
    {
        public IClient Client { get; }
        private Logger logger = Server.Instance.LogManager.GetLoggerFor("Player");
        
        //buffer of unprocessed inputs for this player.  Processed in GameManager.StateUpdateLoop
        public Queue<NetworkingData.PlayerInputData> inputBuffer = new Queue<NetworkingData.PlayerInputData>();

        public bool PlayerReady;

        public string Name { get; }
        public Vector2 ServerPosition = Vector2.Zero;
        public byte LookDirection = 0;
        public byte spriteRow = 0;
        public uint LastProcessedInput = 0;

        public Player(IClient client, NetworkingData.LoginRequestData data)
        {
            Client = client;
            Name = data.Name;
            PlayerReady = false;
            client.MessageReceived += OnPlayerMessage;

            //TODO refactor: this should come from db/configuration/etc
            spriteRow = Server.Instance.getNextSpriteRow();

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
                    case NetworkingData.Tags.PlayerInput:
                        //TODO we shouldn't hit this anymore since we're batching inputs.  Hitting here means you missed something in the client
                        logger.Warning($"Got non-batched input from player id {client.ID}");
                        inputBuffer.Enqueue(message.Deserialize<NetworkingData.PlayerInputData>());
                        break;
                    case NetworkingData.Tags.PlayerInputs:
                        var datas = message.Deserialize<NetworkingData.PlayerInputDatas>();
                        foreach (var data in datas.InputDatas)
                        {
                            inputBuffer.Enqueue(data);
                        }
                        break;
                    default:
                        logger.Info($"PlayerConnection Unhandled tag: {message.Tag}");
                        break;
                }
            }
        }

        private void SendGameStart(IClient client)
        {
            PlayerReady = true;
            using (Message m = Message.Create((ushort) NetworkingData.Tags.GameStartData,
                new NetworkingData.GameStartData(getAllPlayersSpawnData(), Server.Instance.ServerTick)))
            {
                logger.Info("Sending Game Start Data");

                client.SendMessage(m, SendMode.Reliable);
            }
        }

        private static NetworkingData.PlayerSpawnData[] getAllPlayersSpawnData()
        {
            NetworkingData.PlayerSpawnData[] playerSpawnDatas = new NetworkingData.PlayerSpawnData[Server.Instance.Players.Count];
            int i = 0;
            foreach (KeyValuePair<string, Player> entry in Server.Instance.PlayersByName)
            {
                Player p = entry.Value;
                playerSpawnDatas[i] = new NetworkingData.PlayerSpawnData(p.Client.ID, p.Name, p.spriteRow, p.ServerPosition);
                i++;
            }

            return playerSpawnDatas;
        }

        public NetworkingData.PlayerStateData getStateData()
        {
            return new NetworkingData.PlayerStateData(Client.ID, ServerPosition, LookDirection, LastProcessedInput);
        }

        public void SendMessage(Message m, SendMode sm)
        {
            Client.SendMessage(m, sm);
        }
    }
}