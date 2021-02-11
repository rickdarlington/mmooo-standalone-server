using System.Collections.Generic;
using System.Numerics;
using DarkRift;
using DarkRift.Server;

namespace MmoooPlugin
{
    public class Player
    {
        public string Name { get; }
        public IClient Client { get; }
        private Logger logger = Server.Instance.LogManager.GetLoggerFor("Player");

        //TODO should make this not public 
        public Queue<NetworkingData.PlayerInputData> inputBuffer = new Queue<NetworkingData.PlayerInputData>();
        
        private bool playerReady = false;
        public Vector2 ServerPosition = Vector2.Zero;
        public byte LookDirection = 0;
        public byte spriteRow = 0;

        public Player(IClient client, NetworkingData.LoginRequestData data)
        {
            Client = client;
            Name = data.Name;
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
                        inputBuffer.Enqueue(message.Deserialize<NetworkingData.PlayerInputData>());
                        break;
                    default:
                        logger.Info($"PlayerConnection Unhandled tag: {message.Tag}");
                        break;
                }
            }
        }

        private void SendGameStart(IClient client)
        {
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

        public void SendMessage(Message m, SendMode sm)
        {
            Client.SendMessage(m, sm);
        }
    }
}