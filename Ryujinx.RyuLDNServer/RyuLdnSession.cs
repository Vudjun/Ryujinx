using NetCoreServer;
using Open.Nat;
using Ryujinx.Common.Logging;
using Ryujinx.Common.Memory;
using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu.Proxy;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Ryujinx.RyuLDNServer
{
    internal class RyuLdnSession : TcpSession
    {
        private const byte CommonLinkLevel = 3;
        private const byte CommonNetworkType = 2;

        private const int FailureTimeout = 4000;

        internal enum SessionState
        {
            Uninitialized,
            Initialized,
            Creating,
            Connected,
            Disconnected
        }

        private RyuLDNServer _server;
        private RyuLdnManager _manager;
        internal SessionState _state;
        internal CreateAccessPointRequest ap;
        internal CreateAccessPointPrivateRequest privateAp;
        internal InitializeMessage InitializeData = new InitializeMessage();
        public RyuLdnProtocol Protocol { get; }
        public Array128<byte> PassPhrase { get; private set; } = new Array128<byte>();

        public Array16<byte> ip;
        public AddressFamily addressFamily;
        public Array33<byte> UserName;
        public string userNameString;
        public uint LocalCommunicationVersion;
        public uint fakeIp;

        private Random _random { get; set; } = new Random();
        public Room Room { get; internal set; }

        public RyuLdnSession(RyuLDNServer server, RyuLdnManager manager) : base(server)
        {
            _server = server;
            _manager = manager;
            _state = SessionState.Uninitialized;
            Protocol = new RyuLdnProtocol();
            // Client Packets
            Protocol.Initialize += HandleInitialize;
            Protocol.Passphrase += HandlePassphrase;
            Protocol.Connected += HandleConnected;
            Protocol.SyncNetwork += HandleSyncNetwork;
            Protocol.ScanReply += HandleScanReply;
            Protocol.ScanReplyEnd += HandleScanReplyEnd;
            Protocol.Disconnected += HandleDisconnected;

            // External Proxy Packets
            Protocol.ExternalProxy += HandleExternalProxy;
            Protocol.ExternalProxyState += HandleExternalProxyState;
            Protocol.ExternalProxyToken += HandleExternalProxyToken;

            // Server Packets
            Protocol.CreateAccessPoint += HandleCreateAccessPoint;
            Protocol.CreateAccessPointPrivate += HandleCreateAccessPointPrivate;
            Protocol.Reject += HandleReject;
            Protocol.RejectReply += HandleRejectReply;
            Protocol.SetAcceptPolicy += HandleSetAcceptPolicy;
            Protocol.SetAdvertiseData += HandleSetAdvertiseData;
            Protocol.Connect += HandleConnect;
            Protocol.ConnectPrivate += HandleConnectPrivate;
            Protocol.Scan += HandleScan;

            // Proxy Packets
            Protocol.ProxyConfig += HandleProxyConfig;
            Protocol.ProxyConnect += HandleProxyConnect;
            Protocol.ProxyConnectReply += HandleProxyConnectReply;
            Protocol.ProxyData += HandleProxyData;
            Protocol.ProxyDisconnect += HandleProxyDisconnect;


            // Lifecycle Packets
            Protocol.NetworkError += HandleNetworkError;
            Protocol.Ping += HandlePing;
        }

        private void HandleCreateAccessPoint(LdnHeader header, CreateAccessPointRequest request, byte[] advertiseData)
        {
            UserName = request.UserConfig.UserName;
            userNameString = Encoding.UTF8.GetString(UserName.AsSpan()).TrimEnd('\0');
            LocalCommunicationVersion = request.NetworkConfig.LocalCommunicationVersion;

            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleCreateAccessPoint");
            ap = request;
            var networkInfo = new NetworkInfo()
            {
                NetworkId = new NetworkId()
                {
                    IntentId = request.NetworkConfig.IntentId
                },
                Common = new CommonNetworkInfo()
                {
                    Channel = request.NetworkConfig.Channel,
                    NetworkType = (byte)NetworkType.Ldn,
                    LinkLevel = CommonLinkLevel,
                    MacAddress = InitializeData.MacAddress
                },
                Ldn = new LdnNetworkInfo()
                {
                    NodeCount = 0,
                    NodeCountMax = LdnConst.NodeCountMax,
                    Nodes = new Array8<NodeInfo>(),
                    AdvertiseData = new Array384<byte>(),
                    Reserved4 = new Array140<byte>(),
                }
            };
            advertiseData.CopyTo(networkInfo.Ldn.AdvertiseData.AsSpan());
            networkInfo.Ldn.AdvertiseDataSize = (ushort)advertiseData.Length;
            var passphrase = request.SecurityConfig.Passphrase.AsSpan();
            var passphraseBytes = passphrase.Slice(0, request.SecurityConfig.PassphraseSize).ToArray();
            var room = _manager.CreateRoom(this, networkInfo, request.RyuNetworkConfig);
            _manager.AddSessionToRoom(room, this, request.UserConfig);
            ap = request;
        }

        private void HandleCreateAccessPointPrivate(LdnHeader header, CreateAccessPointPrivateRequest request, byte[] advertiseData)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleCreateAccessPointPrivate not implemented");
            /*
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleCreateAccessPointPrivate");
            privateAp = request;
            // Send ProxyConfig
            _manager.SendProxyConfig(this);
            NetworkInfo = new NetworkInfo()
            {
                NetworkId = new NetworkId()
                {
                    SessionId = request.SecurityParameter.SessionId,
                    IntentId = request.NetworkConfig.IntentId
                },
                Common = new CommonNetworkInfo()
                {
                    Channel = request.NetworkConfig.Channel,
                    NetworkType = (byte)NetworkType.Ldn,
                    LinkLevel = CommonLinkLevel,
                    MacAddress = InitializeData.MacAddress,
                    Ssid = _fakeSsid
                },
                Ldn = new LdnNetworkInfo()
                {
                    NodeCount = 1,
                    NodeCountMax = LdnConst.NodeCountMax,
                    SecurityParameter = request.SecurityParameter.Data,
                    Nodes = new Array8<NodeInfo>(),
                    AdvertiseData = new Array384<byte>(),
                    Reserved4 = new Array140<byte>(),
                }
            };

            NetworkInfo.Ldn.Nodes[0] = GetNodeInfo(NetworkInfo.Ldn.Nodes[0], request.UserConfig, request.NetworkConfig.LocalCommunicationVersion);
            */
        }
        private void HandleReject(LdnHeader header, RejectRequest request)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleReject not implemented");
        }

        private void HandleRejectReply(LdnHeader header)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleRejectReply not implemented");
        }

        private void HandleSetAcceptPolicy(LdnHeader header, SetAcceptPolicyRequest request)
        {
            Room.NetworkInfo.Ldn.StationAcceptPolicy = request.StationAcceptPolicy;
            _manager.SyncNetwork(Room);
        }

        private void HandleSetAdvertiseData(LdnHeader header, byte[] data)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleSetAdvertiseData");
            // TODO validate?
            data.AsSpan().CopyTo(Room.NetworkInfo.Ldn.AdvertiseData.AsSpan());
            Room.NetworkInfo.Ldn.AdvertiseDataSize = (ushort)data.Length;
            _manager.SyncNetwork(Room);
        }

        private void HandleConnect(LdnHeader header, ConnectRequest request)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleConnect");
            var room = _manager.RoomList.FirstOrDefault(x => x.NetworkInfo.Common.MacAddress.AsSpan().SequenceEqual(request.NetworkInfo.Common.MacAddress.AsSpan()));
            if (room != null)
            {
                UserName = request.UserConfig.UserName;
                userNameString = Encoding.UTF8.GetString(UserName.AsSpan()).TrimEnd('\0');
                LocalCommunicationVersion = request.LocalCommunicationVersion;
                _manager.AddSessionToRoom(room, this, request.UserConfig);
            }
        }

        private void HandleScan(LdnHeader header, ScanFilter filter)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleScan");
            if (_state != SessionState.Initialized)
            {
                return;
            }
            _manager.SendGameList(this, filter);
        }

        private void HandleProxyConfig(LdnHeader header, ProxyConfig config)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleProxyConfig not implemented");
        }

        private void HandleProxyConnect(LdnHeader header, ProxyConnectRequest request)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleProxyConnect not implemented");
        }

        private void HandleProxyConnectReply(LdnHeader header, ProxyConnectResponse response)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleProxyConnectReply not implemented");
        }

        private void HandleProxyData(LdnHeader header, ProxyDataHeader proxyHeader, byte[] data)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleProxyData");
            if (_state != SessionState.Connected)
            {
                return;
            }
            proxyHeader.Info.SourceIpV4 = fakeIp;
            var destIp = proxyHeader.Info.DestIpV4;
            if (destIp == 175308799 || destIp == 4294967295) // 10.114.255.255 / 255.255.255.255
            {
                var otherClients = Room.Sessions.Where(x => x != this);
                foreach (var client in otherClients)
                {
                    client.SendAsync(client.Protocol.Encode(PacketId.ProxyData, proxyHeader, data));
                }
            }
            else
            {
                var recipient = Room.Sessions.FirstOrDefault(x => x.fakeIp == destIp);
                if (recipient != null)
                {
                    recipient.SendAsync(recipient.Protocol.Encode(PacketId.ProxyData, proxyHeader, data));
                }
            }
        }

        private void HandleProxyDisconnect(LdnHeader header, ProxyDisconnectMessage message)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleProxyDisconnect not implemented");
        }

        private void HandlePing(LdnHeader header, PingMessage message)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandlePing not implemented");
        }

        private void HandleNetworkError(LdnHeader header, NetworkErrorMessage message)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleNetworkError not implemented");
        }

        private void HandleExternalProxyToken(LdnHeader header, ExternalProxyToken token)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleExternalProxyToken not implemented");
        }

        private void HandleExternalProxyState(LdnHeader header, ExternalProxyConnectionState state)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleExternalProxyState");
            if (_state != SessionState.Connected)
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleExternalProxyState in wrong state");
                return;
            }
            if (Room.hostSession != this)
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleExternalProxyState not from external proxy");
                return;
            }
            if (!state.Connected)
            {
                var disconnectedIp = state.IpAddress;
                var disconnectedSession = Room.Sessions.FirstOrDefault(x => x.fakeIp == disconnectedIp);
                if (disconnectedSession != null)
                {
                    _manager.RemoveSessionFromRoom(Room, disconnectedSession);
                }
            }
            SendAsync(Protocol.Encode(PacketId.ExternalProxyState, state));
        }

        private void HandleExternalProxy(LdnHeader header, ExternalProxyConfig config)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleExternalProxy not implemented");
        }

        private void HandleDisconnected(LdnHeader header, DisconnectMessage message)
        {
            if (_state == SessionState.Disconnected)
            {
                return;
            }
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "HandleDisconnected");

            if (Room != null)
            {
                if (Room.hostSession == this)
                {
                    _manager.CloseRoom(Room);
                }
                else
                {
                    _manager.RemoveSessionFromRoom(Room, this);
                }
                Room = null;
            }
            _state = SessionState.Disconnected;
            Disconnect();
        }

        private void HandleScanReplyEnd(LdnHeader header)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleScanReplyEnd not implemented");
        }

        private void HandleScanReply(LdnHeader header, NetworkInfo info)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleScanReply not implemented");
        }

        private void HandleSyncNetwork(LdnHeader header, NetworkInfo info)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleSyncNetwork not implemented");
        }

        private void HandlePassphrase(LdnHeader header, PassphraseMessage message)
        {
            PassPhrase = message.Passphrase;
        }

        private void HandleInitialize(LdnHeader header, InitializeMessage message)
        {
            if (_state == SessionState.Uninitialized)
            {
                _manager.InitializeClient(this, message);
            }
        }

        private void HandleConnectPrivate(LdnHeader header, ConnectPrivateRequest request)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleConnectPrivate not implemented");
        }

        private void HandleConnected(LdnHeader header, NetworkInfo info)
        {
            Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "HandleConnected not implemented");
        }

        protected override void OnDisconnected()
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "Client has disconnected");
            if (_state == SessionState.Disconnected)
            {
                return;
            }
            if (Room != null)
            {
                if (Room.hostSession == this)
                {
                    _manager.CloseRoom(Room);
                }
                else
                {
                    _manager.RemoveSessionFromRoom(Room, this);
                }
                Room = null;
            }
            _state = SessionState.Disconnected;
        }

        protected override void OnConnected()
        {
            var ipEndpoint = Socket.RemoteEndPoint as IPEndPoint;
            if (ipEndpoint == null)
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceLdn, "Client connected with invalid IP");
                Disconnect();
                return;
            }
            var addressBytes = ProxyHelpers.AddressTo16Byte(ipEndpoint.Address);
            addressBytes.CopyTo(ip.AsSpan());
            addressFamily = ipEndpoint.AddressFamily;
        }

        protected override void OnReceived(byte[] buffer, long offset, long size)
        {
            try
            {
                Protocol.Read(buffer, (int)offset, (int)size);
            }
            catch (Exception ex)
            {
                Logger.Warning?.PrintMsg(LogClass.ServiceLdn, ex.ToString());
                Disconnect();
            }
        }
    }
}
