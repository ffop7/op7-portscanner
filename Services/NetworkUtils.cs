using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace Op7PortScanner.Services;

// ──────────────────────────────────────────────────────────────────────────────
//  NetworkUtils — helper methods for everything outside of port scanning:
//    • Ping (with OS fingerprinting via TTL)
//    • DNS resolution
//    • Parsing host input (single IP, range, CIDR, comma list)
//
//  All methods are static so they can be called without creating an instance.
// ──────────────────────────────────────────────────────────────────────────────
public static class NetworkUtils
{
    #region Ping

    /// <summary>
    /// Quick ping — just tells you if the host is reachable.
    /// Used to skip dead hosts before scanning all their ports.
    /// </summary>
    public static async Task<bool> PingAsync(string host, int timeoutMs = 1500)
    {
        try
        {
            using var ping  = new Ping();
            var reply = await ping.SendPingAsync(host, timeoutMs);
            return reply.Status == IPStatus.Success;
        }
        catch { return false; }
    }

    /// <summary>
    /// Detailed ping — returns alive status, TTL, and an OS guess.
    ///
    /// How OS fingerprinting works:
    ///   Every OS sets a default TTL when it sends IP packets.
    ///   Windows sets 128, Linux/macOS set 64, routers/Solaris set 255.
    ///   By the time the reply reaches us, the TTL has been decremented by
    ///   each router hop. So we use thresholds rather than exact values:
    ///     TTL > 120  →  started at 128  →  Windows
    ///     TTL > 55   →  started at 64   →  Linux / macOS
    ///     TTL > 240  →  started at 255  →  Network device / Solaris
    ///
    ///  This is a heuristic, not a guarantee — but it's surprisingly accurate
    ///  on local networks where there are few router hops.
    /// </summary>
    public static async Task<(bool Alive, int Ttl, string OsGuess)> PingDetailAsync(string host)
    {
        try
        {
            using var ping  = new Ping();
            var options     = new PingOptions { DontFragment = false };
            var reply       = await ping.SendPingAsync(host, 2000, new byte[32], options);

            if (reply.Status != IPStatus.Success)
                return (false, 0, "");

            int ttl      = reply.Options?.Ttl ?? 64;
            string osGuess = GuessOsFromTtl(ttl);

            return (true, ttl, osGuess);
        }
        catch { return (false, 0, ""); }
    }

    /// <summary>
    /// Maps a TTL value to a likely operating system.
    /// See <see cref="PingDetailAsync"/> for the reasoning.
    /// </summary>
    private static string GuessOsFromTtl(int ttl) => ttl switch
    {
        >= 240 => "Network device / Solaris",
        >= 120 => "Windows",
        >= 55  => "Linux / macOS / Android",
        _      => "Unknown"
    };

    #endregion

    #region DNS Resolution

    /// <summary>
    /// Resolves a hostname to its IP address.
    /// If the input is already an IP (e.g. "192.168.1.1"), returns it unchanged.
    /// If resolution fails, also returns the input unchanged so the scan
    /// can still attempt a direct connection.
    /// </summary>
    public static async Task<string> ResolveAsync(string host)
    {
        // Already an IP address — nothing to do.
        if (IPAddress.TryParse(host, out _))
            return host;

        try
        {
            var entry = await Dns.GetHostEntryAsync(host);

            // GetHostEntryAsync may return multiple IPs (IPv4 + IPv6).
            // We prefer IPv4 for compatibility.
            var ipv4 = entry.AddressList
                .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);

            return ipv4?.ToString() ?? entry.AddressList.FirstOrDefault()?.ToString() ?? host;
        }
        catch
        {
            // DNS failed — return the original; the connect will fail gracefully.
            return host;
        }
    }

    #endregion

    #region Host Range Parsing

    /// <summary>
    /// Converts whatever the user typed into a flat list of host strings.
    ///
    /// Supported formats:
    ///   • Single IP or hostname:  "192.168.1.1"  /  "scanme.nmap.org"
    ///   • Last-octet range:       "192.168.1.1-254"   →  .1, .2, … .254
    ///   • CIDR /24 subnet:        "192.168.1.0/24"    →  .1, .2, … .254
    ///   • Comma-separated list:   "10.0.0.1, 10.0.0.5, server.local"
    /// </summary>
    public static List<string> ParseHosts(string input)
    {
        input = input.Trim();

        // ── Comma-separated list ──────────────────────────────────────────────
        if (input.Contains(','))
            return input.Split(',')
                        .Select(h => h.Trim())
                        .Where(h => h.Length > 0)
                        .ToList();

        // ── IP range: 192.168.1.1-254 ─────────────────────────────────────────
        // Pattern: four octets, a dash, then the ending value of the last octet.
        var rangeMatch = Regex.Match(input, @"^(\d+)\.(\d+)\.(\d+)\.(\d+)-(\d+)$");
        if (rangeMatch.Success)
        {
            string subnet = $"{rangeMatch.Groups[1]}.{rangeMatch.Groups[2]}.{rangeMatch.Groups[3]}";
            int    start  = int.Parse(rangeMatch.Groups[4].Value);
            int    end    = int.Parse(rangeMatch.Groups[5].Value);

            return Enumerable
                .Range(start, Math.Min(end, 254) - start + 1)
                .Select(i => $"{subnet}.{i}")
                .ToList();
        }

        // ── CIDR /24 ──────────────────────────────────────────────────────────
        // We only support /24 for simplicity (256 addresses, last octet 1-254).
        var cidrMatch = Regex.Match(input, @"^(\d+\.\d+\.\d+)\.\d+/24$");
        if (cidrMatch.Success)
        {
            string subnet = cidrMatch.Groups[1].Value;
            return Enumerable
                .Range(1, 254)
                .Select(i => $"{subnet}.{i}")
                .ToList();
        }

        // ── Single host or IP ─────────────────────────────────────────────────
        return new List<string> { input };
    }

    #endregion
}
