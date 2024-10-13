using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
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
using System.Net.Sockets;
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
            // TODO filter on StationAcceptPolicy too? If it's set to deny all the session is probably not joinable so shouldn't be sent.
            var filterFlag = filter.Flag;
            foreach (var room in RoomList)
            {
                if (!session.PassPhrase.AsSpan().SequenceEqual(room.hostSession.PassPhrase.AsSpan()))
                {
                    continue;
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.LocalCommunicationId))
                {
                    if (filter.NetworkId.IntentId.LocalCommunicationId != room.NetworkInfo.NetworkId.IntentId.LocalCommunicationId)
                    {
                        continue;
                    }
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.SessionId))
                {
                    if (!filter.NetworkId.SessionId.AsSpan().SequenceEqual(room.NetworkInfo.NetworkId.SessionId.AsSpan())) {
                        continue;
                    }
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.NetworkType))
                {
                    if (filter.NetworkType != (NetworkType)room.NetworkInfo.Common.NetworkType)
                    {
                        continue;
                    }
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.Ssid))
                {
                    Span<byte> gameSsid = room.NetworkInfo.Common.Ssid.Name.AsSpan()[room.NetworkInfo.Common.Ssid.Length..];
                    Span<byte> scanSsid = filter.Ssid.Name.AsSpan()[filter.Ssid.Length..];
                    if (!gameSsid.SequenceEqual(scanSsid))
                    {
                        continue;
                    }
                }

                if (filter.Flag.HasFlag(ScanFilterFlag.SceneId))
                {
                    if (filter.NetworkId.IntentId.SceneId != room.NetworkInfo.NetworkId.IntentId.SceneId)
                    {
                        continue;
                    }
                }

                if (room.hostSession.UserName[0] != 0)
                {
                    session.SendAsync(session.Protocol.Encode(PacketId.ScanReply, room.NetworkInfo));
                }
                else
                {
                    Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "LdnManager Scan: Got empty Username. There might be a timing issue somewhere...");
                }
            }
            Task.Delay(250).Wait(); // Reduces scan frequency a little by delaying the end packet
            session.SendAsync(session.Protocol.Encode(PacketId.ScanReplyEnd));
        }

        internal Room CreateRoom(RyuLdnSession session, NetworkInfo networkInfo, RyuNetworkConfig ryuNetworkConfig)
        {
            var gameVersion = Encoding.ASCII.GetString(ryuNetworkConfig.GameVersion.AsSpan()).TrimEnd('\0');
            _random.NextBytes(networkInfo.NetworkId.SessionId.AsSpan());

            networkInfo.Common.Ssid.Length = 32;
            Encoding.UTF8.GetBytes("12345678123456781234567812345678").CopyTo(networkInfo.Common.Ssid.Name.AsSpan());
            networkInfo.Ldn.SecurityMode = 1;
            var room = new Room()
            {
                NetworkInfo = networkInfo,
                Sessions = new List<RyuLdnSession>(),
                nextFakeIp = NetworkHelpers.ConvertIpv4Address("10.114.0.1"),
                fakeNetworkSubnetMask = NetworkHelpers.ConvertIpv4Address("255.255.0.0"),
                gameVersion = gameVersion,
                networkConfig = ryuNetworkConfig,
                hostSession = session
            };
            bool isP2P = ryuNetworkConfig.ExternalProxyPort != 0;
            if (isP2P)
            {
                bool isAccessible = true;
                // probe the port to verify externally reachable
                try
                {
                    using (var client = new TcpClient())
                    {
                        client.ReceiveTimeout = 2000;
                        client.SendTimeout = 2000;
                        client.Connect(((IPEndPoint)session.Socket.RemoteEndPoint!).Address, ryuNetworkConfig.ExternalProxyPort);
                    }
                }
                catch (Exception)
                {
                    isAccessible = false;
                }
                if (!isAccessible)
                {
                    session.SendAsync(session.Protocol.Encode(PacketId.NetworkError, new NetworkErrorMessage()
                    {
                        Error = NetworkError.PortUnreachable
                    }));
                }
            }
            RoomList.Add(room);
            return room;
        }
        internal void AddSessionToRoom(Room room, RyuLdnSession session, UserConfig userConfig)
        {
            session._state = SessionState.Connected;
            room.Sessions.Add(session);
            session.Room = room;
            session.fakeIp = room.nextFakeIp++;
            // If P2P is enabled
            if (room.networkConfig.ExternalProxyPort != 0)
            {
                var token = new Array16<byte>();
                _random.NextBytes(token.AsSpan());
                bool isPrivate = session.ip.AsSpan().SequenceEqual(room.hostSession.ip.AsSpan());
                ExternalProxyConfig config;
                // If same network, send private IP, since same-IP connections through a uPNP opened port on the router often fails depending on router configuration
                if (isPrivate)
                {
                    room.hostSession.SendAsync(room.hostSession.Protocol.Encode(PacketId.ExternalProxyToken, new ExternalProxyToken()
                    {
                        AddressFamily = AddressFamily.InterNetwork,
                        Token = token,
                        VirtualIp = session.fakeIp
                    }));
                    session.SendAsync(session.Protocol.Encode(PacketId.ExternalProxy, new ExternalProxyConfig()
                    {
                        AddressFamily = room.networkConfig.AddressFamily,
                        ProxyIp = room.networkConfig.PrivateIp,
                        ProxyPort = room.networkConfig.InternalProxyPort,
                        Token = token
                    }));
                }
                else
                {
                    room.hostSession.SendAsync(room.hostSession.Protocol.Encode(PacketId.ExternalProxyToken, new ExternalProxyToken()
                    {
                        AddressFamily = session.addressFamily,
                        PhysicalIp = session.ip,
                        Token = token,
                        VirtualIp = session.fakeIp
                    }));
                    session.SendAsync(session.Protocol.Encode(PacketId.ExternalProxy, new ExternalProxyConfig()
                    {
                        AddressFamily = room.hostSession.addressFamily,
                        ProxyIp = room.hostSession.ip,
                        ProxyPort = room.networkConfig.ExternalProxyPort,
                        Token = token
                    }));
                }
            }
            else
            {
                session.SendAsync(session.Protocol.Encode(PacketId.ProxyConfig, new ProxyConfig()
                {
                    ProxyIp = session.fakeIp,
                    ProxySubnetMask = room.fakeNetworkSubnetMask
                }));
            }
            SyncNetwork(room, session);
        }

        internal void SyncNetwork(Room room, RyuLdnSession? connectedSession = null)
        {
            for (var i = 0; i < 8; i++)
            {
                var session = room.Sessions.ElementAtOrDefault(i);
                if (session == null)
                {
                    room.NetworkInfo.Ldn.Nodes[i] = new NodeInfo();
                }
                else
                {
                    room.NetworkInfo.Ldn.Nodes[i] = new NodeInfo()
                    {
                        MacAddress = session.InitializeData.MacAddress,
                        NodeId = (byte)i,
                        IsConnected = 1,
                        UserName = session.UserName,
                        LocalCommunicationVersion = (ushort)session.LocalCommunicationVersion,
                        Ipv4Address = session.fakeIp
                    };
                }
            }
            room.NetworkInfo.Ldn.NodeCount = (byte)room.Sessions.Count;
            foreach (var session in room.Sessions)
            {
                if (session == connectedSession)
                {
                    session.SendAsync(session.Protocol.Encode(PacketId.Connected, room.NetworkInfo));
                }
                else
                {
                    session.SendAsync(session.Protocol.Encode(PacketId.SyncNetwork, room.NetworkInfo));
                }
            }
        }

        internal void CloseRoom(Room room)
        {
            foreach (var session in room.Sessions)
            {
                session.SendAsync(session.Protocol.Encode(PacketId.Disconnect, new DisconnectMessage()
                {
                    DisconnectIP = 0
                }));
                session.Disconnect();
            }
            RoomList.Remove(room);
        }

        internal void RemoveSessionFromRoom(Room room, RyuLdnSession ryuLdnSession)
        {
            room.Sessions.Remove(ryuLdnSession);
            SyncNetwork(room);
        }
    }
}
