using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace LinkManager.Network;

/// <summary>
/// Tests internet connectivity for a specific adapter by probing through its local IP.
/// Strategy: ICMP ping first (fast), HTTP HEAD fallback if ICMP is blocked.
/// An adapter is considered reachable if ANY single endpoint responds via either method.
/// An adapter is considered dead ONLY if ALL ICMP and ALL HTTP probes fail.
/// </summary>
public sealed class ConnectivityProbe
{
    private readonly List<string> _icmpEndpoints;
    private readonly List<string> _httpEndpoints;
    private readonly int _timeoutMs;
    private readonly Action<string> _log;

    public ConnectivityProbe(
        List<string> icmpEndpoints,
        List<string> httpEndpoints,
        int timeoutMs,
        Action<string>? log = null)
    {
        _icmpEndpoints = icmpEndpoints;
        _httpEndpoints = httpEndpoints;
        _timeoutMs = timeoutMs;
        _log = log ?? (_ => { });
    }

    /// <summary>
    /// Probes connectivity through the given source IP.
    /// Returns true if the adapter can reach the internet via ICMP or HTTP.
    /// </summary>
    public async Task<bool> CheckConnectivity(string sourceIp)
    {
        // Phase 1: ICMP (fast path)
        foreach (var endpoint in _icmpEndpoints)
        {
            if (await PingIcmp(sourceIp, endpoint))
            {
                _log($"[Probe] {sourceIp} ICMP OK → {endpoint}");
                return true;
            }
        }

        // Phase 2: HTTP fallback (for ICMP-blocked environments)
        foreach (var url in _httpEndpoints)
        {
            if (await ProbeHttp(sourceIp, url))
            {
                _log($"[Probe] {sourceIp} HTTP OK → {url}");
                return true;
            }
        }

        _log($"[Probe] {sourceIp} DEAD — all ICMP and HTTP probes failed");
        return false;
    }

    // ── ICMP ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shells out to `ping -S sourceIp -n 1 -w timeoutMs endpoint`.
    /// The -S flag binds the ICMP packet to a specific source IP, ensuring
    /// we test through the correct adapter even when multiple adapters are up.
    /// </summary>
    private async Task<bool> PingIcmp(string sourceIp, string endpoint)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ping",
                Arguments = $"-S {sourceIp} -n 1 -w {_timeoutMs} {endpoint}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return false;

            // Give the process a hard deadline slightly beyond the ping timeout
            using var cts = new CancellationTokenSource(_timeoutMs + 1000);
            await proc.WaitForExitAsync(cts.Token);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // ── HTTP ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends an HTTP HEAD request bound to the given source IP.
    /// Uses SocketsHttpHandler.ConnectCallback to bind the socket before connecting,
    /// ensuring the request goes through the specific adapter.
    /// </summary>
    private async Task<bool> ProbeHttp(string sourceIp, string url)
    {
        try
        {
            var sourceEndpoint = new IPEndPoint(IPAddress.Parse(sourceIp), 0);

            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromMilliseconds(_timeoutMs),
                // Bind to source IP so request routes through the correct adapter
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    socket.Bind(sourceEndpoint);
                    try
                    {
                        await socket.ConnectAsync(context.DnsEndPoint, cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(_timeoutMs)
            };

            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // Any HTTP response (even 204, 301, 302) means the adapter can reach the internet
            return true;
        }
        catch
        {
            return false;
        }
    }
}
