namespace Op7PortScanner.Models;

/// <summary>
/// Holds everything we found out about a single open port.
/// One instance is created per open port discovered during a scan.
/// </summary>
public class ScanResult
{
    /// <summary>IP address or hostname that was scanned.</summary>
    public string Host { get; set; } = "";

    /// <summary>Port number (1–65535).</summary>
    public int Port { get; set; }

    /// <summary>Protocol used — always "TCP" for now.</summary>
    public string Protocol { get; set; } = "TCP";

    /// <summary>
    /// Human-readable service name looked up from our dictionary.
    /// Examples: "SSH", "HTTP", "MySQL". Empty if unknown.
    /// </summary>
    public string Service { get; set; } = "";

    /// <summary>
    /// First bytes received from the port after connecting.
    /// Many services send their version string immediately (SSH, FTP, SMTP…).
    /// HTTP ports receive a HEAD request first.
    /// Empty if the service sent nothing within the timeout.
    /// </summary>
    public string Banner { get; set; } = "";

    /// <summary>
    /// Operating system guess based on the ping TTL value.
    /// TTL ≈ 128 → Windows, TTL ≈ 64 → Linux/macOS, TTL ≈ 255 → network device.
    /// </summary>
    public string OsGuess { get; set; } = "";

    /// <summary>Raw TTL value from the ping reply.</summary>
    public int Ttl { get; set; }

    /// <summary>Exact moment this port was confirmed open.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// Banner trimmed to 70 characters for display in the terminal.
    /// The full banner is always kept in <see cref="Banner"/>.
    /// </summary>
    public string DisplayBanner =>
        Banner.Length > 70 ? Banner[..70] + "…" : Banner;
}
