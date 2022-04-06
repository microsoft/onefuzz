using System;
using System.Collections.Generic;
using ApiService;

namespace ApiService;

record ProxyHeartbeat
{
    string Region;
    string ProxyId;
    List<ProxyForward> Forwards;
    DateTimeOffset Timestamp;

}