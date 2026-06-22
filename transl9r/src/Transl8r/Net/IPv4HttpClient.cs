using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Transl8r.Net;

/// <summary>
/// Builds an HttpClient that prefers IPv4 when connecting. .NET resolves
/// "localhost" to ::1 (IPv6) first, but local LLM servers (Ollama, llama.cpp)
/// bind 127.0.0.1 (IPv4) only → "target machine actively refused". Ordering IPv4
/// first makes the default localhost config work; remote hosts are unaffected.
/// See CSHARP_REWRITE_PLAN.md (Phase 1 "NEW C# gotcha").
/// </summary>
internal static class IPv4HttpClient
{
    public static HttpClient Create(TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (context, ct) =>
            {
                IPAddress[] addrs = await Dns
                    .GetHostAddressesAsync(context.DnsEndPoint.Host, ct)
                    .ConfigureAwait(false);
                IEnumerable<IPAddress> ordered = addrs.OrderBy(
                    a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1);

                Exception? last = null;
                foreach (IPAddress addr in ordered)
                {
                    var socket = new Socket(addr.AddressFamily, SocketType.Stream,
                        ProtocolType.Tcp) { NoDelay = true };
                    try
                    {
                        await socket.ConnectAsync(addr, context.DnsEndPoint.Port, ct)
                            .ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        socket.Dispose();
                    }
                }
                throw last ?? new SocketException((int)SocketError.HostNotFound);
            },
        };
        return new HttpClient(handler) { Timeout = timeout };
    }
}
