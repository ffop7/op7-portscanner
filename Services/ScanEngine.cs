using System.Buffers;                       // ArrayPool — reuse memory instead of allocating new arrays
using System.Collections.Concurrent;        // ConcurrentBag — thread-safe list for results
using System.Net.Sockets;
using System.Text;
using Op7PortScanner.Models;

namespace Op7PortScanner.Services;

// ──────────────────────────────────────────────────────────────────────────────
//  Progress report sent to the UI after each port is tested.
//  Uses a record so we get equality, ToString, and immutability for free.
// ──────────────────────────────────────────────────────────────────────────────
public record ScanProgress(
    string Host,        // Which host was being scanned
    int    Port,        // Which port was just tested
    bool   Open,        // Was it open?
    int    Done,        // How many ports have been tested so far
    int    Total,       // Total ports to test
    string Service,     // Service name (e.g. "SSH")
    string Banner);     // Banner text grabbed after connecting

// ──────────────────────────────────────────────────────────────────────────────
//  ScanEngine — the core of op7 port scanner
//
//  How it works:
//    1. We receive a list of ports to test.
//    2. We spin up many parallel async tasks (controlled by a SemaphoreSlim).
//    3. Each task tries to open a TCP connection to one port.
//    4. If it connects, we optionally grab the banner (first bytes sent by the service).
//    5. We report progress after every single port so the UI stays responsive.
//
//  Why async instead of threads?
//    TCP connect is I/O-bound, not CPU-bound. Using async lets us have thousands
//    of "in-flight" connections without spawning thousands of OS threads.
//    SemaphoreSlim caps concurrency so we don't flood the network or OS.
// ──────────────────────────────────────────────────────────────────────────────
public class ScanEngine
{
    #region Constants

    // How many bytes we try to read for a banner.
    // 512 is enough to capture version strings; anything larger wastes time.
    private const int BannerBufferSize = 512;

    // How long we wait for a banner after connecting (ms).
    // Kept short — if the service doesn't speak within 400 ms it probably won't.
    private const int BannerReadDelayMs = 350;

    // Maximum banner wait regardless of user timeout setting.
    private const int BannerMaxWaitMs = 800;

    #endregion

    #region Known Services Dictionary

    // Maps well-known port numbers to human-readable service names.
    // Used purely for display — it does NOT affect scanning logic.
    public static readonly Dictionary<int, string> KnownServices = new()
    {
        // File transfer
        { 20, "FTP-data" }, { 21, "FTP" }, { 69, "TFTP" }, { 873, "rsync" },

        // Remote access
        { 22,   "SSH"      }, { 23,  "Telnet"   }, { 3389, "RDP"    },
        { 5900, "VNC"      }, { 4899,"Radmin"   }, { 5985, "WinRM"  },
        { 5986, "WinRM-SSL"}, { 902, "VMware"   },

        // Email
        { 25, "SMTP" }, { 110, "POP3" }, { 143, "IMAP"  }, { 993, "IMAPS"  },
        { 995,"POP3S"}, { 465, "SMTPS"}, { 587, "SMTP"  },

        // Web
        { 80,   "HTTP"       }, { 443,  "HTTPS"      }, { 8080, "HTTP-Proxy" },
        { 8443, "HTTPS-Alt"  }, { 8000, "HTTP-Alt"   }, { 8008, "HTTP-Alt"   },
        { 8888, "HTTP-Alt"   }, { 3000, "Dev"        },

        // DNS & Network
        { 53,  "DNS"     }, { 67, "DHCP"   }, { 68, "DHCP"   },
        { 123, "NTP"     }, { 161,"SNMP"   }, { 179,"BGP"    },

        // Windows networking
        { 135, "MSRPC"   }, { 137, "NetBIOS" }, { 138, "NetBIOS" },
        { 139, "NetBIOS" }, { 445, "SMB"     }, { 88,  "Kerberos"},
        { 49152, "Win-RPC"},

        // Directory services
        { 389, "LDAP"  }, { 636, "LDAPS"  },

        // Databases
        { 1433, "MSSQL"     }, { 1521, "Oracle"      }, { 3306, "MySQL"       },
        { 5432, "PostgreSQL" }, { 6379, "Redis"       }, { 27017,"MongoDB"     },
        { 27018,"MongoDB"    }, { 28017,"MongoDB-Web" }, { 11211,"Memcached"   },

        // Infrastructure / DevOps
        { 2375, "Docker"      }, { 2181, "Zookeeper"  }, { 9200, "Elasticsearch"},
        { 9300, "Elasticsearch"}, {5601, "Kibana"      }, { 9090, "Portainer"   },
        { 9000, "PHP-FPM"     }, { 2049, "NFS"        }, { 111,  "RPC"         },
        { 10000,"Webmin"      }, { 631,  "IPP"        }, { 515,  "LPD"         },

        // VPN / Tunnels
        { 1194, "OpenVPN" }, { 51820, "WireGuard" }, { 1723, "PPTP" },
        { 1080, "SOCKS"   },

        // Hosting panels
        { 2082, "cPanel" }, { 2083, "cPanel-SSL" }, { 2086, "WHM" }, { 2087, "WHM-SSL" },
        { 2095, "cPanel"  }, { 2096, "cPanel-SSL"},

        // Misc
        { 119, "NNTP" }, { 194, "IRC" }, { 514, "Syslog" },
        { 6660,"IRC"  }, { 6661,"IRC" }, { 6662,"IRC"    }, { 6663, "IRC" },
        { 6664,"IRC"  }, { 6665,"IRC" }, { 6666,"IRC"    }, { 6667, "IRC" },
        { 7070,"RealAudio"}, { 7777, "Game" }, { 4444, "Metasploit" },
        { 5000,"UPnP" },
    };

    // Shortcut used throughout the UI to look up a service name by port.
    public static string GetServiceName(int port) =>
        KnownServices.TryGetValue(port, out var name) ? name : "";

    #endregion

    #region Common Ports List

    // The 80+ ports that appear most frequently in real networks.
    // Shown when the user clicks "★ Common Ports".
    public static readonly int[] CommonPorts =
    {
        20, 21, 22, 23, 25, 53, 67, 68, 69, 80, 88, 110, 111, 119, 123,
        135, 137, 138, 139, 143, 161, 179, 194, 389, 443, 445, 465, 514,
        515, 587, 631, 636, 873, 902, 993, 995, 1080, 1194, 1433, 1521,
        1723, 2049, 2082, 2083, 2086, 2087, 2095, 2096, 2181, 2375, 3000,
        3306, 3389, 4444, 4899, 5000, 5432, 5601, 5900, 5985, 5986, 6379,
        6660, 6661, 6662, 6663, 6664, 6665, 6666, 6667, 7070, 7777, 8000,
        8008, 8080, 8443, 8888, 9000, 9090, 9200, 9300, 10000, 11211,
        27017, 27018, 28017, 49152, 51820,
    };

    #endregion

    #region Public Scan Method

    /// <summary>
    /// Scans all ports in the list against the given host, in parallel.
    /// Results are streamed back via <paramref name="progress"/> so the UI
    /// can update in real time without waiting for the scan to finish.
    /// </summary>
    /// <param name="host">Resolved IP or hostname to scan.</param>
    /// <param name="ports">List of port numbers to test.</param>
    /// <param name="progress">Callback invoked after each port is tested.</param>
    /// <param name="ct">Cancellation token — honours the Stop button.</param>
    /// <param name="timeoutMs">Per-port connect timeout in milliseconds.</param>
    /// <param name="concurrency">Max parallel connections at any one time.</param>
    /// <param name="grabBanners">Whether to read the first bytes from open ports.</param>
    public async Task<List<ScanResult>> ScanAsync(
        string                  host,
        IEnumerable<int>        ports,
        IProgress<ScanProgress> progress,
        CancellationToken       ct,
        int  timeoutMs   = 300,
        int  concurrency = 1500,
        bool grabBanners = true)
    {
        var portList = ports.ToList();

        // Thread-safe bag to collect results from parallel tasks.
        // We sort at the end so the export files are in port order.
        var openPorts = new ConcurrentBag<ScanResult>();

        // Tracks how many ports we've finished (open or closed).
        // We use int[] instead of a plain int so async methods can capture
        // it by reference — C# async methods don't allow 'ref' parameters,
        // but an array is a reference type so Interlocked.Increment works fine.
        int[] counter = new int[1];

        // SemaphoreSlim is a lightweight gate:
        // at most `concurrency` tasks may be inside the gate at once.
        // This prevents us from opening 65535 sockets simultaneously.
        using var gate = new SemaphoreSlim(concurrency, concurrency);

        // Create one async task per port, but don't await them yet.
        var tasks = portList.Select(port => ScanPortAsync(
            port, host, portList.Count,
            openPorts, progress,
            counter,
            gate, ct, timeoutMs, grabBanners));

        // Now run all tasks concurrently and wait for every one to finish.
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // Return results sorted by port number for tidy output.
        return openPorts.OrderBy(r => r.Port).ToList();
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Tests a single port. This is the unit of work that runs in parallel.
    /// It: waits for a slot → connects → grabs banner → reports → releases slot.
    /// </summary>
    private static async Task ScanPortAsync(
        int port, string host, int totalPorts,
        ConcurrentBag<ScanResult> results,
        IProgress<ScanProgress>   progress,
        int[]             counter,
        SemaphoreSlim             gate,
        CancellationToken         ct,
        int  timeoutMs,
        bool grabBanners)
    {
        // Wait for a free slot in the gate before proceeding.
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Bail early if the user pressed Stop while we were waiting.
            if (ct.IsCancellationRequested) return;

            // Try to connect. If successful, optionally grab the banner.
            var (isOpen, banner) = await ConnectAndGrabAsync(
                host, port, timeoutMs, grabBanners, ct).ConfigureAwait(false);

            // Atomically increment the counter so all threads stay in sync.
            int done        = Interlocked.Increment(ref counter[0]);
            string service  = GetServiceName(port);

            if (isOpen)
            {
                results.Add(new ScanResult
                {
                    Host      = host,
                    Port      = port,
                    Service   = service,
                    Banner    = banner,
                    Timestamp = DateTime.Now,
                });
            }

            // Notify the UI — Progress<T> automatically marshals to the UI thread.
            progress.Report(new ScanProgress(
                host, port, isOpen, done, totalPorts, service, banner));
        }
        finally
        {
            // Always release the gate slot, even if an exception occurred.
            gate.Release();
        }
    }

    /// <summary>
    /// Attempts a TCP connection to one port.
    /// Returns (true, banner) if the port is open, (false, "") otherwise.
    ///
    /// We create a linked CancellationTokenSource so we can honour BOTH:
    ///   • The user pressing Stop  (parent token)
    ///   • The per-port timeout    (CancelAfter)
    /// </summary>
    private static async Task<(bool IsOpen, string Banner)> ConnectAndGrabAsync(
        string host, int port, int timeoutMs, bool grabBanner, CancellationToken parentCt)
    {
        // LinkedTokenSource combines the parent (Stop button) with our timeout.
        // When either fires, the socket connect is cancelled.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(parentCt);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            using var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay     = true,                              // Disable Nagle — we want fast small packets
                LingerState = new LingerOption(true, 0),        // Don't keep the socket alive after close
            };

            await socket.ConnectAsync(host, port, timeoutCts.Token).ConfigureAwait(false);

            // If we reach here the port is open. Optionally read the banner.
            string banner = grabBanner && socket.Connected
                ? await ReadBannerAsync(socket, port, Math.Min(timeoutMs, BannerMaxWaitMs)).ConfigureAwait(false)
                : "";

            return (true, banner);
        }
        catch
        {
            // Any exception (refused, timeout, cancelled) means the port is closed/filtered.
            return (false, "");
        }
    }

    /// <summary>
    /// Tries to read the first bytes sent by the service after we connect.
    /// Many protocols (SSH, FTP, SMTP) send a greeting immediately.
    /// For HTTP we first send a HEAD request to trigger a response.
    ///
    /// Optimization: we use ArrayPool to borrow a buffer instead of
    /// allocating a new byte[512] for every single open port found.
    /// This reduces GC pressure significantly on large scans.
    /// </summary>
    private static async Task<string> ReadBannerAsync(Socket socket, int port, int maxWaitMs)
    {
        // Borrow a buffer from the shared pool instead of allocating.
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BannerBufferSize);
        try
        {
            // HTTP ports won't send anything until they receive a request first.
            if (IsHttpPort(port))
            {
                var request = "HEAD / HTTP/1.0\r\nHost: target\r\n\r\n"u8.ToArray();
                await socket.SendAsync(request, SocketFlags.None).ConfigureAwait(false);
            }

            // Give the service a moment to respond.
            await Task.Delay(Math.Min(maxWaitMs, BannerReadDelayMs)).ConfigureAwait(false);

            // If there's data waiting, read it.
            if (socket.Available > 0)
            {
                int bytesRead = socket.Receive(buffer, 0,
                    Math.Min(BannerBufferSize, socket.Available), SocketFlags.None);

                if (bytesRead > 0)
                    return SanitizeBanner(buffer, bytesRead);
            }
        }
        catch
        {
            // If banner reading fails for any reason, we still report the port as open.
            // A banner is a bonus, not a requirement.
        }
        finally
        {
            // Always return the buffer to the pool.
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return "";
    }

    /// <summary>
    /// Returns true for ports that speak HTTP and need a request before responding.
    /// </summary>
    private static bool IsHttpPort(int port) =>
        port is 80 or 8080 or 8000 or 8008 or 8888 or 3000;

    /// <summary>
    /// Converts raw bytes into a clean printable string.
    /// Replaces newlines with spaces and strips non-ASCII characters.
    /// This prevents the terminal from being messed up by control codes.
    /// </summary>
    private static string SanitizeBanner(byte[] buffer, int length)
    {
        var sb = new StringBuilder(length);
        for (int i = 0; i < length && i < 200; i++)
        {
            char c = (char)buffer[i];
            if      (c is '\r' or '\n') sb.Append(' ');   // Flatten newlines
            else if (c >= 32 && c < 127) sb.Append(c);   // Keep printable ASCII only
        }
        return sb.ToString().Trim();
    }

    #endregion
}
