using LibHac.Sdmmc;
using NetCoreServer;
using Open.Nat;
using Ryujinx.Common.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu.Proxy;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu.Types;
using Ryujinx.Common.Memory;

namespace Ryujinx.RyuLDNServer
{
    internal class RyuLDNServer : TcpServer
    {
        private RyuLdnManager _manager;

        public const ushort PORT = 30456;

        public RyuLDNServer() : base(IPAddress.Any, PORT)
        {
            _manager = new RyuLdnManager();
            OptionNoDelay = true;
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, "RyuLDN server started");
        }

        protected override TcpSession CreateSession()
        {
            return new RyuLdnSession(this, _manager);
        }

        protected override void OnError(SocketError error)
        {
            Logger.Info?.PrintMsg(LogClass.ServiceLdn, $"Proxy TCP server caught an error with code {error}");
        }
    }
}
