using System;
using System.Collections.Generic;
using DarkRift;
using DarkRift.Server;

namespace MmoooPlugin
{
    public class ConnectionManager
    {
        private static int connectionCount = 0;
        
        private static Logger logger = Server.Instance.LogManager.GetLoggerFor("ConnectionManager");
        private static IClientManager clientManager = Server.Instance.ClientManager;

        public static void Connected(object sender, ClientConnectedEventArgs args)
        {
            logger.Info($"{clientManager.Count} client connections total.");
            args.Client.MessageReceived += OnMessage;
        }

        public static void Disconnected(object sender, ClientDisconnectedEventArgs args)
        {
            IClient client = args.Client;
            Player p;
            if (Server.Instance.Players.TryGetValue(client.ID, out p))
            {
                logger.Info($"{p.Name} (id: {client.ID}) disconnected.");
                Server.Instance.RemovePlayer(args.Client.ID, p.Name);
                
                //TODO need to call destroy type method on playerConnection?
            }

            args.Client.MessageReceived -= OnMessage;
        }

        private static void OnMessage(object sender, MessageReceivedEventArgs e)
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
                        logger.Info($"Unhandled tag: {message.Tag}");
                        break;
                }
            }
        }

        private static void OnClientLogin(IClient client, NetworkingData.LoginRequestData data)
        {
            logger.Info($"Login request from ({data.Name}).");
            //TODO secure, load player from DB, etc
            if (Server.Instance.PlayersByName.ContainsKey(data.Name))
            {
                using (Message message = Message.CreateEmpty((ushort) NetworkingData.Tags.LoginRequestDenied))
                {
                    logger.Info($"{data.Name} is already logged in!");
                    client.SendMessage(message, SendMode.Reliable);
                }

                return;
            }
            
            client.MessageReceived -= OnMessage;
            
            Player p = new Player(client, data);
            Server.Instance.AddPlayer(client.ID, data.Name, p);
        }
    }
}