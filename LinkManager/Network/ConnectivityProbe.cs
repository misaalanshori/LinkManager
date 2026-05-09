using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace LinkManager.Network;

/// <summary>
/// Tests internet connectivity for a specific adapter by probing through its local IP.
///
/// Strategy:
///   1. (Optional) ICMP fast-fail: if ALL pings fail, skip HTTP entirely — adapter is dead.
///      ICMP success is ignored; it can never declare an adapter healthy by itself, because
///      ISP edge equipment commonly responds to pings for 8.8.8.8/1.1.1.1 even when upstream
///      transit is severed (DNS proxy hijacking).
///
///   2. HTTP quorum: run all HTTP probes in parallel. Each validates the response body/status
///      against the expected value for that endpoint. At least ProbeQuorum endpoints must pass
///      content validation for the adapter to be declared healthy.
///      A captive portal or transparent proxy will fail content validation even if it returns
///      HTTP 200, preventing ISP-level false positives.
///
/// An adapter is healthy only if HTTP quorum is met.
/// An adapter is dead if ICMP fast-fail fires OR HTTP quorum is not met.
/// </summary>
public sealed class ConnectivityProbe
{
    private readonly List<string> _icmpEndpoints;
    private readonly List<HttpProbeTarget> _httpTargets;
    private readonly int _timeoutMs;
    private readonly int _quorum;
    private readonly bool _enableIcmp;
    private readonly Action<string> _log;

    // ── Well-known endpoint validators ────────────────────────────────────────

    // Maps URL prefixes to the validator that checks the response is genuine.
    private static readonly Dictionary<string, Func<HttpResponseMessage, string, bool>> _knownValidators =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // ── Microsoft ────────────────────────────────────────────────────

            // Microsoft NCSI (modern): must return exactly "Microsoft Connect Test"
            ["http://www.msftconnecttest.com/connecttest.txt"] =
                (r, body) => r.IsSuccessStatusCode &&
                             body.Trim().Equals("Microsoft Connect Test", StringComparison.Ordinal),

            // Microsoft NCSI (legacy): must return exactly "Microsoft NCSI"
            ["http://www.msftncsi.com/ncsi.txt"] =
                (r, body) => r.IsSuccessStatusCode &&
                             body.Trim().Equals("Microsoft NCSI", StringComparison.Ordinal),

            // ── Google ───────────────────────────────────────────────────────

            // Google NCSI (desktop): 204 No Content — captive portals typically return 200+HTML
            ["https://www.google.com/generate_204"] =
                (r, _) => (int)r.StatusCode == 204,

            // Google NCSI (Android): same contract, different host — tests a second Google PoP
            ["http://connectivitycheck.gstatic.com/generate_204"] =
                (r, _) => (int)r.StatusCode == 204,

            // Google static CDN 204: yet another independent Google endpoint
            ["http://www.gstatic.com/generate_204"] =
                (r, _) => (int)r.StatusCode == 204,

            // ── Cloudflare ───────────────────────────────────────────────────

            // Cloudflare trace endpoint: plain-text diagnostics, not behind WAF.
            // Genuine response always contains the "visit_scheme=" key.
            ["https://1.1.1.1/cdn-cgi/trace"] =
                (r, body) => r.IsSuccessStatusCode &&
                             body.Contains("visit_scheme=", StringComparison.OrdinalIgnoreCase),

            // ── Mozilla ──────────────────────────────────────────────────────

            // Firefox NCSI: must return exactly "success" (plain text, no HTML)
            ["http://detectportal.firefox.com/success.txt"] =
                (r, body) => r.IsSuccessStatusCode &&
                             body.Trim().Equals("success", StringComparison.OrdinalIgnoreCase),

            // ── GNOME / Linux ────────────────────────────────────────────────

            // NetworkManager NCSI: must return exactly "NetworkManager is online"
            ["http://nmcheck.gnome.org/check_network_status.txt"] =
                (r, body) => r.IsSuccessStatusCode &&
                             body.Trim().Equals("NetworkManager is online", StringComparison.OrdinalIgnoreCase),
        };

    // ── Constructor ───────────────────────────────────────────────────────────

    public ConnectivityProbe(
        List<string> icmpEndpoints,
        List<string> httpEndpoints,
        int timeoutMs,
        int quorum,
        bool enableIcmp = true,
        Action<string>? log = null)
    {
        _icmpEndpoints = icmpEndpoints;
        _timeoutMs = timeoutMs;
        _enableIcmp = enableIcmp;
        _log = log ?? (_ => { });

        // Auto-clamp quorum to a valid range so misconfiguration can't break things
        int effectiveQuorum = Math.Clamp(quorum, 1, Math.Max(1, httpEndpoints.Count));
        if (effectiveQuorum != quorum)
            _log($"[Probe] ProbeQuorum clamped from {quorum} to {effectiveQuorum} " +
                 $"(must be between 1 and endpoint count {httpEndpoints.Count})");
        _quorum = effectiveQuorum;

        // Pre-build target descriptors so we don't re-lookup validators on every probe
        _httpTargets = httpEndpoints.Select(url =>
        {
            _knownValidators.TryGetValue(url, out var validator);
            return new HttpProbeTarget(url, validator);
        }).ToList();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Probes connectivity through the given source IP.
    /// Returns true only if the HTTP quorum is met with content-validated responses.
    /// </summary>
    public async Task<bool> CheckConnectivity(string sourceIp)
    {
        // ── Phase 1: ICMP fast-fail (optional) ──────────────────────────────
        // If every ICMP probe fails, skip the slower HTTP probes — clearly dead.
        // If any ICMP probe succeeds, we do NOT return true here; we still require
        // HTTP quorum, because ISP proxies can spoof ICMP for upstream IPs.
        if (_enableIcmp && _icmpEndpoints.Count > 0)
        {
            bool anyIcmpOk = false;
            foreach (var endpoint in _icmpEndpoints)
            {
                if (await PingIcmp(sourceIp, endpoint))
                {
                    _log($"[Probe] {sourceIp} ICMP OK → {endpoint} (informational only, not sufficient for health)");
                    anyIcmpOk = true;
                    break; // one success is enough to skip the fast-fail
                }
            }

            if (!anyIcmpOk)
            {
                _log($"[Probe] {sourceIp} ICMP fast-fail — all pings failed, skipping HTTP");
                return false;
            }
        }

        // ── Phase 2: HTTP quorum with content validation ─────────────────────
        // Run all HTTP probes in parallel; count how many pass content validation.
        var probeTasks = _httpTargets.Select(target => ProbeHttpValidated(sourceIp, target)).ToList();
        var probeResults = await Task.WhenAll(probeTasks);

        int passed = probeResults.Count(r => r);
        bool healthy = passed >= _quorum;

        _log($"[Probe] {sourceIp} HTTP quorum: {passed}/{_httpTargets.Count} passed, " +
             $"need {_quorum} → {(healthy ? "HEALTHY" : "DEAD")}");

        return healthy;
    }

    // ── ICMP ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shells out to `ping -S sourceIp -n 1 -w timeoutMs endpoint`.
    /// The -S flag binds the ICMP packet to the adapter's source IP so we test
    /// through the correct adapter even when multiple adapters are up.
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

            // Hard deadline slightly beyond the ping timeout
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
    /// Sends an HTTP GET request bound to the given source IP and validates the response
    /// against the expected content for that endpoint.
    /// For well-known endpoints, validates exact body/status.
    /// For unknown endpoints, applies captive-portal heuristics.
    /// </summary>
    private async Task<bool> ProbeHttpValidated(string sourceIp, HttpProbeTarget target)
    {
        try
        {
            var sourceEndpoint = new IPEndPoint(IPAddress.Parse(sourceIp), 0);

            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromMilliseconds(_timeoutMs),
                // Disable auto-redirect: a redirect is itself a signal of captive portal
                AllowAutoRedirect = false,
                // Bind socket to the specific adapter's source IP
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

            // Use GET (not HEAD) so we can read and validate the response body
            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // Read body (limit to 2KB — enough for content validation, avoids large payloads)
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(contentStream);
            var buffer = new char[2048];
            int read = await reader.ReadAsync(buffer, 0, buffer.Length);
            string body = new string(buffer, 0, read);

            bool valid;
            string reason;

            if (target.Validator != null)
            {
                // Well-known endpoint: use its specific validator
                valid = target.Validator(response, body);
                reason = valid ? "content validated" : "content mismatch (likely captive portal or proxy)";
            }
            else
            {
                // Unknown endpoint: apply generic captive-portal heuristics
                (valid, reason) = GenericValidate(response, body, target.Url);
            }

            _log($"[Probe] {sourceIp} HTTP {(valid ? "✓" : "✗")} → {target.Url} " +
                 $"[{(int)response.StatusCode}] — {reason}");
            return valid;
        }
        catch (Exception ex)
        {
            _log($"[Probe] {sourceIp} HTTP ✗ → {target.Url} — exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Heuristic captive-portal detection for endpoints not in the known-validators map.
    /// Returns (isGenuine, reasonString).
    /// </summary>
    private static (bool valid, string reason) GenericValidate(
        HttpResponseMessage response, string body, string url)
    {
        // Reject redirects — genuine connectivity probes don't redirect
        if ((int)response.StatusCode is >= 300 and < 400)
            return (false, $"unexpected redirect {(int)response.StatusCode} — captive portal suspected");

        // Reject non-2xx (error pages, etc.)
        if (!response.IsSuccessStatusCode)
            return (false, $"non-success status {(int)response.StatusCode}");

        // Reject if the content-type looks like HTML — a connectivity probe should never
        // return HTML unless it's a captive portal landing page
        var ct = response.Content.Headers.ContentType?.MediaType ?? "";
        if (ct.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            return (false, "response is HTML — likely captive portal");

        // Reject if body is suspiciously large (connectivity probes return tiny responses)
        if (body.Length >= 2048)
            return (false, "response body too large (≥2KB) — likely captive portal page");

        return (true, "generic heuristics passed");
    }

    // ── Internal types ────────────────────────────────────────────────────────

    private sealed record HttpProbeTarget(
        string Url,
        Func<HttpResponseMessage, string, bool>? Validator);
}
