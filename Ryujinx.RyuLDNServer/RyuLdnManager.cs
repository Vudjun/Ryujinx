using Ryujinx.Common.Logging;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Types;
using Ryujinx.HLE.HOS.Services.Nifm.StaticService.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using static Ryujinx.RyuLDNServer.RyuLdnSession;

namespace Ryujinx.RyuLDNServer
{
    internal class RyuLdnManager
    {
        // TODO once connected, ping session every 10 seconds to ensure connected
        private static byte[] _emptyId = new byte[16];

        public List<Room> RoomList = new List<Room>();
        private Random _random { get; set; } = new Random();

        internal void InitializeClient(RyuLdnSession session, InitializeMessage message)
        {
            //if (message.Id.AsSpan().SequenceEqual(_emptyId))
            {
                Random random = new Random();
                random.NextBytes(message.Id.AsSpan());
                random.NextBytes(message.MacAddress.AsSpan());
                session.InitializeData = message;
                session._state = RyuLdnSession.SessionState.Initialized;
                session.SendAsync(session.Protocol.Encode(PacketId.Initialize, message));
            }
            //else
            //{
            //    Logger.Info?.PrintMsg(LogClass.ServiceLdn, "Client already has an ID");
            //    session._state = RyuLdnSession.SessionState.Initialized;
            //    session.SendAsync(session.Protocol.Encode(PacketId.Initialize, message));
            //    // TODO validate client id from cache
            //}
        }

        internal void SendGameList(RyuLdnSession session, ScanFilter filter)
        {
            // TODO filter it based on passphrase
            // We should filter on StationAcceptPolicy too (but it might be better if we don't?)
            var filterFlag = filter.Flag;
            IEnumerable<Room> rooms = RoomList;
            if (filterFlag.HasFlag(ScanFilterFlag.SessionId))
            {

            }
            if (filterFlag.HasFlag(ScanFilterFlag.IntentId))
            {

            }
            foreach (var room in rooms)
            {
                session.SendAsync(session.Protocol.Encode(PacketId.ScanReply, room.NetworkInfo));
            }
            Task.Delay(250).Wait(); // Reduces scan frequency a little by delaying the end packet
            session.SendAsync(session.Protocol.Encode(PacketId.ScanReplyEnd));
        }

        internal void SendProxyConfig(RyuLdnSession session)
        {
            session.SendAsync(session.Protocol.Encode(PacketId.ProxyConfig, session.ProxyConfig));
        }

        internal Room CreateRoom(NetworkInfo networkInfo)
        {
            _random.NextBytes(networkInfo.NetworkId.SessionId.AsSpan());

            networkInfo.Common.Ssid.Length = 32;
            Encoding.UTF8.GetBytes("12345678123456781234567812345678").CopyTo(networkInfo.Common.Ssid.Name.AsSpan());
            networkInfo.Ldn.SecurityMode = 1;
            var room = new Room()
            {
                NetworkInfo = networkInfo,
                Sessions = new List<RyuLdnSession>(),
                firstIp = NetworkHelpers.ConvertIpv4Address("10.114.0.1"),
                subnetMask = NetworkHelpers.ConvertIpv4Address("255.255.0.0")
            };
            RoomList.Add(room);
            Task.Delay(5000).ContinueWith((t) =>
            {
                AddDummySession(room);
            });
            return room;
        }
        internal void AddSessionToRoom(Room room, RyuLdnSession session, UserConfig userConfig, ushort localCommunicationVersion)
        {
            int nodeIndex = room.NetworkInfo.Ldn.Nodes.AsSpan().ToArray().ToList().FindIndex(x => x.IsConnected == 0); // todo :)
            var node = room.NetworkInfo.Ldn.Nodes[nodeIndex];
            node.MacAddress = session.InitializeData.MacAddress;
            node.NodeId = (byte)nodeIndex;
            node.IsConnected = 1;
            node.UserName = userConfig.UserName;
            node.LocalCommunicationVersion = localCommunicationVersion;
            node.Ipv4Address = room.firstIp + (uint)nodeIndex;
            session.ProxyConfig = new ProxyConfig()
            {
                ProxyIp = node.Ipv4Address,
                ProxySubnetMask = room.subnetMask
            };
            room.NetworkInfo.Ldn.Nodes[nodeIndex] = node;
            room.NetworkInfo.Ldn.NodeCount = (byte)(nodeIndex + 1);
            room.Sessions.Add(session);
            session.Room = room;
            SendProxyConfig(session);
            session._state = SessionState.Connected;
            SendConnected(session);
            SyncNetwork(room);
        }

        internal void AddDummySession(Room room)
        {
            int nodeIndex = room.NetworkInfo.Ldn.Nodes.AsSpan().ToArray().ToList().FindIndex(x => x.IsConnected == 0); // todo :)
            var node = room.NetworkInfo.Ldn.Nodes[nodeIndex];
            _random.NextBytes(node.MacAddress.AsSpan());
            node.NodeId = (byte)nodeIndex;
            node.IsConnected = 1;
            Encoding.UTF8.GetBytes("Dummy\0").CopyTo(node.UserName.AsSpan());
            node.LocalCommunicationVersion = 13;
            node.Ipv4Address = room.firstIp + (uint)nodeIndex;
            room.NetworkInfo.Ldn.Nodes[nodeIndex] = node;
            room.NetworkInfo.Ldn.NodeCount = (byte)(nodeIndex + 1);
            SyncNetwork(room);
        }

        internal void SyncNetwork(Room room)
        {
            foreach (var session in room.Sessions)
            {
                session.SendAsync(session.Protocol.Encode(PacketId.SyncNetwork, room.NetworkInfo));
            }
        }

        internal void SendConnected(RyuLdnSession ryuLdnSession)
        {
            ryuLdnSession.SendAsync(ryuLdnSession.Protocol.Encode(PacketId.Connected, ryuLdnSession.Room.NetworkInfo));
        }
    }
}
