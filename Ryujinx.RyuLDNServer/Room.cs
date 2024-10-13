using Ryujinx.Common.Utilities;
using Ryujinx.HLE.HOS.Services.Ldn.Types;
using Ryujinx.HLE.HOS.Services.Ldn.UserServiceCreator.LdnRyu.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ryujinx.RyuLDNServer
{
    internal class Room
    {
        public NetworkInfo NetworkInfo;
        public List<RyuLdnSession> Sessions = new List<RyuLdnSession>();
        public RyuLdnSession hostSession;
        public uint nextFakeIp;
        public uint fakeNetworkSubnetMask;
        public string gameVersion;
        public RyuNetworkConfig networkConfig;
    }
}
