using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Op7PortScanner.Models;
using Op7PortScanner.Services;

namespace Op7PortScanner;

// ──────────────────────────────────────────────────────────────────────────────
//  MainForm — the entire graphical interface of op7 port scanner.
//
//  Layout (top to bottom):
//    1. Header panel   — title, flower image, inputs, buttons
//    2. Folder bar     — save location + export buttons
//    3. Progress bar   — fills as ports are tested
//    4. Status row     — one-line status text
//    5. SplitContainer — left: terminal log  |  right: tabbed sidebar
//         Sidebar tabs:  Stats | History | Profiles
//
//  Threading model:
//    All scan work runs on background threads via async/await.
//    UI updates go through Progress<T> which automatically marshals
//    back to the UI thread — no manual Invoke() needed for progress reports.
//    The Log() helper uses rtbLog.InvokeRequired for any other UI updates.
// ──────────────────────────────────────────────────────────────────────────────
public class MainForm : Form
{
    #region Fields

    // ── Services (created once, reused for every scan) ────────────────────────
    private readonly ScanEngine         _scanner  = new();
    private readonly PersistenceService _storage  = new();

    // ── App state ─────────────────────────────────────────────────────────────
    private List<ScanHistoryEntry> _history     = new();
    private List<ScanProfile>      _profiles    = new();
    private List<ScanResult>       _lastResults = new();  // Results from the most recent scan

    private string _saveFolder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    private CancellationTokenSource? _cts;   // Cancelled when the user presses Stop
    private bool     _darkMode;
    private int      _openCount;             // Incremented each time an open port is found
    private readonly Stopwatch _stopwatch = new();  // Measures scan duration for ETA

    // ── Controls — assigned in BuildUI(), never null after that ──────────────
    private TextBox       txtHost     = null!;
    private NumericUpDown numStart    = null!, numEnd      = null!,
                          numTimeout  = null!, numThreads  = null!;
    private CheckBox      chkPing     = null!, chkBanner   = null!;
    private Button        btnScan     = null!, btnCommon   = null!,
                          btnStop     = null!, btnClear    = null!,
                          btnTheme    = null!, btnFolder   = null!,
                          btnExpTxt   = null!, btnExpCsv   = null!,
                          btnExpJson  = null!, btnCopy     = null!,
                          btnSaveProf = null!, btnLoadProf = null!,
                          btnDelProf  = null!;
    private Label         lblStatus   = null!, lblFolder   = null!,
                          lblSpeed    = null!, lblElapsed  = null!,
                          lblEta      = null!, lblOpen     = null!,
                          lblTested   = null!, lblHost     = null!,
                          lblOs       = null!, lblTtl      = null!;
    private RichTextBox   rtbLog      = null!;
    private ProgressBar   pBar        = null!;
    private ListBox       lbHistory   = null!, lbProfiles  = null!;
    private PictureBox    pbFlower    = null!;
    private TabControl    sidebar     = null!;

    // Timer that updates the stats panel every second during a scan.
    private System.Windows.Forms.Timer _statsTimer = null!;

    // ── Theme colors ──────────────────────────────────────────────────────────
    private static readonly Color White      = Color.White;
    private static readonly Color NearBlack  = Color.FromArgb(18, 18, 18);
    private static readonly Color DarkPanel  = Color.FromArgb(26, 26, 26);
    private static readonly Color TermBg     = Color.FromArgb(8, 8, 8);
    private static readonly Color Green      = Color.FromArgb(0, 200, 90);

    #endregion

    #region Constructor

    public MainForm()
    {
        // Load persisted data before building the UI so the lists are ready to render.
        _history  = _storage.LoadHistory();
        _profiles = _storage.LoadProfiles();

        BuildUI();

        // Generate the flower image after the PictureBox is created.
        pbFlower.Image = FlowerArt.Generate(165);

        // Populate the sidebar lists.
        RefreshHistoryList();
        RefreshProfileList();
    }

    #endregion

    #region UI Construction

    /// <summary>
    /// Builds the entire form programmatically.
    /// No .Designer.cs file — everything is in one place, easy to follow.
    /// </summary>
    private void BuildUI()
    {
        // ── Form itself ───────────────────────────────────────────────────────
        Text            = "op7 port scanner v3";
        ClientSize      = new Size(1060, 770);
        MinimumSize     = new Size(1060, 770);
        MaximumSize     = new Size(1060, 770);
        MaximizeBox     = false;
        BackColor       = White;
        Font            = new Font("Segoe UI", 9f);
        StartPosition   = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;

        BuildHeaderPanel();
        BuildFolderBar();
        BuildProgressRow();
        BuildSplitPanel();

        // The stats timer ticks every second to refresh elapsed time, speed, ETA.
        _statsTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _statsTimer.Tick += OnStatsTick;
    }

    /// <summary>
    /// Top section: title, subtitle, flower image, all input controls, action buttons.
    /// </summary>
    private void BuildHeaderPanel()
    {
        var panel = new Panel { Bounds = new Rectangle(0, 0, 1060, 215), BackColor = White };
        Controls.Add(panel);

        // Flower image — generated in the constructor
        pbFlower = new PictureBox
        {
            Bounds   = new Rectangle(875, 8, 165, 165),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = White
        };
        panel.Controls.Add(pbFlower);

        // Title and subtitle
        panel.Controls.Add(MakeLabel("op7 port scanner v3",
            new Font("Consolas", 20f, FontStyle.Bold),
            new Rectangle(18, 12, 850, 44), Color.Black));

        panel.Controls.Add(MakeLabel(
            "TCP scanner  ●  banner grab  ●  IP ranges  ●  DNS  ●  OS fingerprint  ●  JSON / CSV / TXT",
            new Font("Segoe UI", 8.5f, FontStyle.Italic),
            new Rectangle(20, 54, 850, 18), Color.Gray));

        panel.Controls.Add(MakeHLine(20, 78, 1020));

        // ── Target input ──────────────────────────────────────────────────────
        panel.Controls.Add(MakeLabel("Target", null,
            new Rectangle(20, 92, 52, 22), Color.Black));

        txtHost = new TextBox
        {
            Bounds      = new Rectangle(74, 91, 280, 22),
            Text        = "127.0.0.1",
            Font        = new Font("Consolas", 9.5f),
            BorderStyle = BorderStyle.FixedSingle,
        };
        panel.Controls.Add(txtHost);

        panel.Controls.Add(MakeLabel(
            "  supports: IP · hostname · 192.168.1.1-254 · /24 · comma-list",
            new Font("Segoe UI", 7.5f),
            new Rectangle(360, 94, 480, 18), Color.Gray));

        // ── Port range + scan options ─────────────────────────────────────────
        panel.Controls.Add(MakeLabel("From", null, new Rectangle(20, 122, 40, 22), Color.Black));
        numStart = MakeSpinner(62, 121, 82, min: 1, max: 65535, val: 1);
        panel.Controls.Add(numStart);

        panel.Controls.Add(MakeLabel("To", null, new Rectangle(152, 122, 22, 22), Color.Black));
        numEnd = MakeSpinner(176, 121, 82, min: 1, max: 65535, val: 1024);
        panel.Controls.Add(numEnd);

        panel.Controls.Add(MakeLabel("Timeout (ms)", null, new Rectangle(270, 122, 94, 22), Color.Black));
        numTimeout = MakeSpinner(368, 121, 72, min: 50, max: 5000, val: 300);
        panel.Controls.Add(numTimeout);

        panel.Controls.Add(MakeLabel("Threads", null, new Rectangle(448, 122, 62, 22), Color.Black));
        numThreads = MakeSpinner(514, 121, 82, min: 50, max: 5000, val: 1500);
        panel.Controls.Add(numThreads);

        // Optional behaviours
        chkPing   = new CheckBox { Text = "Ping first",  Bounds = new Rectangle(610, 122, 95,  22), Checked = true };
        chkBanner = new CheckBox { Text = "Banner grab", Bounds = new Rectangle(710, 122, 100, 22), Checked = true };
        panel.Controls.Add(chkPing);
        panel.Controls.Add(chkBanner);

        // ── Action buttons ────────────────────────────────────────────────────
        btnScan   = MakeButton("▶  Scan",    20,  160, 100, Color.Black,             Color.White);
        btnCommon = MakeButton("★  Common", 128,  160, 100, Color.FromArgb(40,40,40), Color.White);
        btnStop   = MakeButton("■  Stop",   236,  160, 84,  Color.DarkRed,            Color.White);
        btnClear  = MakeButton("⌫  Clear",  328,  160, 84,  White,                    Color.Black);
        btnTheme  = MakeButton("☀  Theme",  420,  160, 88,  Color.FromArgb(30,30,30), Color.White);

        btnScan.Click   += OnScanClicked;
        btnCommon.Click += OnCommonPortsClicked;
        btnStop.Click   += (_, __) => _cts?.Cancel();   // Signal cancellation
        btnClear.Click  += OnClearClicked;
        btnTheme.Click  += OnToggleTheme;
        btnStop.Enabled  = false;   // Only enabled during an active scan

        foreach (var btn in new[] { btnScan, btnCommon, btnStop, btnClear, btnTheme })
            panel.Controls.Add(btn);
    }

    /// <summary>
    /// Thin bar below the header: shows the save folder path and export buttons.
    /// </summary>
    private void BuildFolderBar()
    {
        var bar = new Panel
        {
            Bounds    = new Rectangle(0, 215, 1060, 32),
            BackColor = Color.FromArgb(246, 246, 246),
        };
        Controls.Add(bar);
        bar.Controls.Add(MakeHLine(0, 0,  1060, Color.FromArgb(210, 210, 210)));
        bar.Controls.Add(MakeHLine(0, 31, 1060, Color.FromArgb(210, 210, 210)));

        // Current save folder path (truncated to fit)
        lblFolder = new Label
        {
            Bounds       = new Rectangle(10, 7, 690, 18),
            Text         = _saveFolder,
            ForeColor    = Color.DimGray,
            Font         = new Font("Segoe UI", 8f),
            AutoEllipsis = true,
        };
        bar.Controls.Add(lblFolder);

        // Folder picker button
        btnFolder = MakeButton("📁 Folder", 708, 3, 90, White, Color.DimGray);
        btnFolder.FlatAppearance.BorderColor = Color.FromArgb(200, 200, 200);
        btnFolder.Click += OnChooseFolder;
        bar.Controls.Add(btnFolder);

        // Export format buttons — disabled until a scan has been run
        btnExpTxt  = MakeButton("TXT",  806, 3, 48, White, Color.Black); btnExpTxt.Enabled  = false;
        btnExpCsv  = MakeButton("CSV",  858, 3, 48, White, Color.Black); btnExpCsv.Enabled  = false;
        btnExpJson = MakeButton("JSON", 910, 3, 56, White, Color.Black); btnExpJson.Enabled = false;

        btnExpTxt.Click  += async (_, __) => await ExportResultsAsync("txt");
        btnExpCsv.Click  += async (_, __) => await ExportResultsAsync("csv");
        btnExpJson.Click += async (_, __) => await ExportResultsAsync("json");

        bar.Controls.Add(btnExpTxt);
        bar.Controls.Add(btnExpCsv);
        bar.Controls.Add(btnExpJson);
    }

    /// <summary>
    /// Progress bar + one-line status text below the folder bar.
    /// </summary>
    private void BuildProgressRow()
    {
        pBar = new ProgressBar
        {
            Bounds = new Rectangle(0, 247, 1060, 8),
            Style  = ProgressBarStyle.Continuous,
        };
        Controls.Add(pBar);

        var statusRow = new Panel { Bounds = new Rectangle(0, 255, 1060, 22), BackColor = White };
        Controls.Add(statusRow);

        lblStatus = new Label
        {
            Bounds    = new Rectangle(10, 3, 1040, 16),
            Text      = "Idle — configure target and press Scan.",
            ForeColor = Color.Gray,
            Font      = new Font("Segoe UI", 8f),
        };
        statusRow.Controls.Add(lblStatus);
    }

    /// <summary>
    /// The main content area: terminal on the left, tabbed sidebar on the right.
    /// </summary>
    private void BuildSplitPanel()
    {
        var split = new SplitContainer
        {
            Bounds           = new Rectangle(0, 277, 1060, 493),
            SplitterDistance = 650,
            Panel1MinSize    = 420,
            Panel2MinSize    = 200,
            BorderStyle      = BorderStyle.None,
            BackColor        = Color.Black,
        };
        Controls.Add(split);

        // ── Left: terminal output ─────────────────────────────────────────────
        var termHeader = new Panel { Dock = DockStyle.Top, Height = 22, BackColor = Color.Black };
        termHeader.Controls.Add(MakeLabel("  RESULTS  ●",
            new Font("Consolas", 8.5f, FontStyle.Bold),
            new Rectangle(0, 4, 300, 14), Green));
        split.Panel1.Controls.Add(termHeader);

        rtbLog = new RichTextBox
        {
            Dock        = DockStyle.Fill,
            BackColor   = TermBg,
            ForeColor   = Green,
            Font        = new Font("Consolas", 9.5f),
            ReadOnly    = true,
            BorderStyle = BorderStyle.None,
            WordWrap    = false,
            ScrollBars  = RichTextBoxScrollBars.Both,
        };
        split.Panel1.Controls.Add(rtbLog);

        // ── Right: tabbed sidebar ─────────────────────────────────────────────
        sidebar = new TabControl
        {
            Dock      = DockStyle.Fill,
            Font      = new Font("Segoe UI", 8.5f),
            BackColor = White,
        };
        split.Panel2.Controls.Add(sidebar);

        BuildStatsTab();
        BuildHistoryTab();
        BuildProfilesTab();
    }

    /// <summary>
    /// Stats tab: live metrics updated every second during a scan.
    /// </summary>
    private void BuildStatsTab()
    {
        var tab = new TabPage("  Stats  ");
        sidebar.TabPages.Add(tab);

        int y = 14;

        // Helper: adds a labelled row and returns the value Label.
        Label AddRow(string title)
        {
            tab.Controls.Add(MakeLabel(title,
                new Font("Segoe UI", 8.5f, FontStyle.Bold),
                new Rectangle(12, y, 84, 20), Color.Gray));

            var value = new Label
            {
                Bounds    = new Rectangle(100, y, 270, 20),
                Text      = "—",
                Font      = new Font("Consolas", 9f),
                ForeColor = Color.Black,
            };
            tab.Controls.Add(value);
            y += 26;
            return value;
        }

        lblHost    = AddRow("Target:");
        lblElapsed = AddRow("Elapsed:"); lblElapsed.Text = "00:00:00";
        lblSpeed   = AddRow("Speed:");   lblSpeed.Text   = "0 ports/s";
        lblOpen    = AddRow("Open:");    lblOpen.Text    = "0";
        lblTested  = AddRow("Tested:");  lblTested.Text  = "0 / 0";
        lblEta     = AddRow("ETA:");
        lblOs      = AddRow("OS guess:");
        lblTtl     = AddRow("TTL:");

        tab.Controls.Add(MakeHLine(12, y + 4, 360));
        y += 18;

        // Copy button — lets the user grab the terminal text in one click
        btnCopy = MakeButton("📋  Copy to Clipboard", 12, y, 185, Color.Black, Color.White);
        btnCopy.Enabled = false;
        btnCopy.Click  += OnCopyToClipboard;
        tab.Controls.Add(btnCopy);
        y += 40;

        tab.Controls.Add(MakeLabel("TXT auto-saved after each scan.",
            new Font("Segoe UI", 7.5f, FontStyle.Italic),
            new Rectangle(12, y, 340, 16), Color.Gray));
    }

    /// <summary>
    /// History tab: list of past scans. Click one to replay its results in the terminal.
    /// </summary>
    private void BuildHistoryTab()
    {
        var tab = new TabPage("  History  ");
        sidebar.TabPages.Add(tab);

        lbHistory = new ListBox
        {
            Bounds      = new Rectangle(0, 0, 400, 340),
            Font        = new Font("Consolas", 8f),
            BorderStyle = BorderStyle.None,
        };
        lbHistory.SelectedIndexChanged += OnHistoryItemSelected;
        tab.Controls.Add(lbHistory);

        var btnClearHistory = MakeButton("🗑  Clear History", 0, 348, 145, White, Color.DarkRed);
        btnClearHistory.FlatAppearance.BorderColor = Color.DarkRed;
        btnClearHistory.Click += (_, __) =>
        {
            if (MessageBox.Show("Clear all history?", "op7",
                MessageBoxButtons.YesNo) != DialogResult.Yes) return;

            _history.Clear();
            _storage.SaveHistory(_history);
            RefreshHistoryList();
        };
        tab.Controls.Add(btnClearHistory);
    }

    /// <summary>
    /// Profiles tab: saved scan configurations the user can reload.
    /// </summary>
    private void BuildProfilesTab()
    {
        var tab = new TabPage("  Profiles  ");
        sidebar.TabPages.Add(tab);

        lbProfiles = new ListBox
        {
            Bounds      = new Rectangle(0, 0, 400, 288),
            Font        = new Font("Consolas", 8f),
            BorderStyle = BorderStyle.None,
        };
        tab.Controls.Add(lbProfiles);

        btnSaveProf = MakeButton("💾  Save Profile",  0,   296, 165, Color.Black,             Color.White);
        btnLoadProf = MakeButton("📂  Load Profile",  173, 296, 165, Color.FromArgb(40,40,40), Color.White);
        btnDelProf  = MakeButton("🗑  Delete",         0,   332, 120, White,                    Color.DarkRed);
        btnDelProf.FlatAppearance.BorderColor = Color.DarkRed;

        btnSaveProf.Click += OnSaveProfile;
        btnLoadProf.Click += OnLoadProfile;
        btnDelProf.Click  += OnDeleteProfile;

        tab.Controls.Add(btnSaveProf);
        tab.Controls.Add(btnLoadProf);
        tab.Controls.Add(btnDelProf);
    }

    #endregion

    #region Scan Logic

    /// <summary>
    /// Called when the user clicks "▶ Scan Range".
    /// Validates the port range and starts a scan.
    /// </summary>
    private async void OnScanClicked(object? sender, EventArgs e)
    {
        int startPort = (int)numStart.Value;
        int endPort   = (int)numEnd.Value;

        if (startPort > endPort)
        {
            MessageBox.Show("Start port must be less than or equal to end port.", "op7");
            return;
        }

        var ports = Enumerable.Range(startPort, endPort - startPort + 1);
        await StartScanAsync(txtHost.Text.Trim(), ports);
    }

    /// <summary>
    /// Called when the user clicks "★ Common Ports".
    /// Uses the predefined list of well-known ports.
    /// </summary>
    private async void OnCommonPortsClicked(object? sender, EventArgs e)
        => await StartScanAsync(txtHost.Text.Trim(), ScanEngine.CommonPorts);

    /// <summary>
    /// Core scan method. Orchestrates host parsing, ping, DNS, scanning, and saving.
    ///
    /// Flow:
    ///   1. Parse the host input into a list of individual hosts
    ///   2. For each host: optionally ping, resolve DNS, then scan all ports
    ///   3. Stream results to the terminal via Progress<T>
    ///   4. Auto-save a TXT report and update history when done
    /// </summary>
    private async Task StartScanAsync(string rawHostInput, IEnumerable<int> ports)
    {
        if (string.IsNullOrWhiteSpace(rawHostInput))
        {
            MessageBox.Show("Please enter a target host or IP address.", "op7");
            return;
        }

        // ── Setup ─────────────────────────────────────────────────────────────
        var hosts    = NetworkUtils.ParseHosts(rawHostInput);
        var portList = ports.ToList();

        _cts = new CancellationTokenSource();
        _openCount   = 0;
        _lastResults.Clear();

        SetScanButtonState(scanning: true);
        rtbLog.Clear();
        pBar.Maximum = portList.Count * hosts.Count;
        pBar.Value   = 0;
        lblOpen.Text = "0";

        _stopwatch.Restart();
        _statsTimer.Start();

        // Read options from UI once, before the async loop.
        bool pingFirst   = chkPing.Checked;
        bool grabBanners = chkBanner.Checked;
        int  timeoutMs   = (int)numTimeout.Value;
        int  concurrency = (int)numThreads.Value;

        // ── Print scan header to terminal ─────────────────────────────────────
        PrintLine($"op7 port scanner v3 — {DateTime.Now:yyyy-MM-dd HH:mm:ss}", Color.Yellow);
        PrintLine($"Hosts    : {HostSummary(hosts)}", Green);
        PrintLine($"Ports    : {portList.Min()}–{portList.Max()} ({portList.Count} total)", Green);
        PrintLine($"Options  : ping={pingFirst}  banner={grabBanners}  timeout={timeoutMs}ms  threads={concurrency}", Green);
        PrintLine(Divider(), Color.FromArgb(40, 160, 40));
        PrintLine($"  {"TIME",-10}  {"PORT",-6}  {"SERVICE",-14}  BANNER", Color.FromArgb(120, 220, 120));
        PrintLine(Divider(), Color.FromArgb(40, 160, 40));

        int totalDone = 0;

        // ── Scan each host in sequence ────────────────────────────────────────
        foreach (var host in hosts)
        {
            if (_cts.Token.IsCancellationRequested) break;

            lblHost.Text = host;

            // Resolve hostname → IP (no-op if already an IP)
            string resolvedIp = await NetworkUtils.ResolveAsync(host);
            if (resolvedIp != host)
                PrintLine($"  DNS  {host} → {resolvedIp}", Color.FromArgb(120, 200, 255));

            // Optional ping — skip the host if it doesn't respond
            if (pingFirst)
            {
                lblStatus.Text = $"Pinging {host}...";
                var (alive, ttl, osGuess) = await NetworkUtils.PingDetailAsync(resolvedIp);

                if (!alive)
                {
                    PrintLine($"  {host} — no ping reply, skipping.", Color.Orange);
                    totalDone += portList.Count;    // Advance progress even for skipped hosts
                    pBar.Value = Math.Min(totalDone, pBar.Maximum);
                    continue;
                }

                // Update the stats sidebar with ping info
                lblOs.Text  = osGuess;
                lblTtl.Text = ttl.ToString();
                PrintLine($"  Ping OK  TTL={ttl}  OS guess: {osGuess}", Color.FromArgb(120, 200, 255));
            }

            // Build the progress callback for this host's scan.
            // Progress<T> captures the UI SynchronizationContext, so the lambda
            // always runs on the UI thread — safe to update controls directly.
            var progress = new Progress<ScanProgress>(report =>
            {
                totalDone++;
                pBar.Value     = Math.Min(totalDone, pBar.Maximum);
                lblStatus.Text = $"Scanning {report.Host}:{report.Port}  ({report.Done}/{report.Total})";
                lblTested.Text = $"{report.Done} / {report.Total}";

                if (report.Open)
                {
                    _openCount++;
                    lblOpen.Text = _openCount.ToString();

                    string shortBanner = report.Banner.Length > 38
                        ? report.Banner[..38] + "…"
                        : report.Banner;

                    PrintLine(
                        $"  {DateTime.Now:HH:mm:ss}    {report.Port,-6}  {report.Service,-14}  {shortBanner}",
                        Green);

                    _lastResults.Add(new ScanResult
                    {
                        Host      = host,
                        Port      = report.Port,
                        Service   = report.Service,
                        Banner    = report.Banner,
                        OsGuess   = lblOs.Text,
                        Ttl       = int.TryParse(lblTtl.Text, out int t) ? t : 0,
                        Timestamp = DateTime.Now,
                    });
                }
            });

            // Run the actual port scan — this is where the parallel async magic happens.
            var results = await _scanner.ScanAsync(
                resolvedIp, portList, progress,
                _cts.Token, timeoutMs, concurrency, grabBanners);

            // Auto-save TXT for this host immediately after its scan finishes.
            if (!_cts.Token.IsCancellationRequested && results.Count > 0)
                await AutoSaveTxtAsync(host, portList.Count, results);
        }

        // ── Finalise ──────────────────────────────────────────────────────────
        _statsTimer.Stop();
        _stopwatch.Stop();
        bool wasCancelled = _cts.Token.IsCancellationRequested;

        PrintLine(Divider(), Color.FromArgb(40, 160, 40));
        PrintLine(
            $"  Scan {(wasCancelled ? "stopped" : "complete")} — {_openCount} open — elapsed {_stopwatch.Elapsed:hh\\:mm\\:ss}",
            Color.Yellow);

        lblStatus.Text = wasCancelled
            ? $"Scan stopped. {_openCount} open port(s) found."
            : $"Done — {_openCount} open port(s) in {_stopwatch.Elapsed:hh\\:mm\\:ss}";

        // Save to history (only on non-cancelled scans)
        if (!wasCancelled)
        {
            _history.Insert(0, new ScanHistoryEntry
            {
                Host      = rawHostInput,
                Date      = DateTime.Now,
                Scanned   = portList.Count * hosts.Count,
                OpenCount = _openCount,
                Results   = new List<ScanResult>(_lastResults),
            });
            _storage.SaveHistory(_history);
            RefreshHistoryList();
        }

        SetScanButtonState(scanning: false);
        EnableExportButtons(_lastResults.Count > 0);
    }

    /// <summary>
    /// Writes a TXT report to the save folder automatically after each host finishes.
    /// Errors are swallowed so a disk issue never crashes the scan.
    /// </summary>
    private async Task AutoSaveTxtAsync(string host, int scannedCount, List<ScanResult> results)
    {
        try
        {
            string safeHost = host.Replace(".", "_").Replace("/", "_");
            string fileName = $"op7_{safeHost}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string path     = Path.Combine(_saveFolder, fileName);
            await ExportService.SaveTxtAsync(path, host, scannedCount, results);
        }
        catch { /* Disk full, permissions issue, etc. — don't interrupt the scan. */ }
    }

    #endregion

    #region Export

    /// <summary>
    /// Exports <see cref="_lastResults"/> to a file in the chosen format.
    /// Shows a confirmation dialog with the file path on success.
    /// </summary>
    private async Task ExportResultsAsync(string format)
    {
        if (_lastResults.Count == 0) return;

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string path      = Path.Combine(_saveFolder, $"op7_export_{timestamp}.{format}");

        try
        {
            switch (format)
            {
                case "txt":  await ExportService.SaveTxtAsync(path,  "—", _lastResults.Count, _lastResults); break;
                case "csv":  await ExportService.SaveCsvAsync(path,  _lastResults); break;
                case "json": await ExportService.SaveJsonAsync(path, "—", _lastResults); break;
            }
            MessageBox.Show($"Saved to:\n{path}", "op7 — Export OK",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "op7 — Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Runs every second during a scan to refresh elapsed time, speed, and ETA.
    /// </summary>
    private void OnStatsTick(object? sender, EventArgs e)
    {
        lblElapsed.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");

        double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        if (elapsedSeconds > 0 && pBar.Maximum > 0)
        {
            double portsPerSecond = pBar.Value / elapsedSeconds;
            lblSpeed.Text = $"{portsPerSecond:F0} ports/s";

            int remaining = pBar.Maximum - pBar.Value;
            lblEta.Text   = portsPerSecond > 0
                ? TimeSpan.FromSeconds(remaining / portsPerSecond).ToString(@"hh\:mm\:ss")
                : "—";
        }
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        rtbLog.Clear();
        _openCount      = 0;
        lblOpen.Text    = "0";
        lblTested.Text  = "0 / 0";
        lblSpeed.Text   = "0 ports/s";
        lblEta.Text     = "—";
        lblElapsed.Text = "00:00:00";
        EnableExportButtons(false);
    }

    private void OnCopyToClipboard(object? sender, EventArgs e)
    {
        if (rtbLog.Text.Length == 0) return;
        Clipboard.SetText(rtbLog.Text);
        MessageBox.Show("Results copied to clipboard.", "op7",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Toggles between light and dark mode.
    /// Applies colours to the form, panels, and sidebar tabs.
    /// </summary>
    private void OnToggleTheme(object? sender, EventArgs e)
    {
        _darkMode = !_darkMode;
        Color bg  = _darkMode ? NearBlack : White;

        BackColor         = bg;
        rtbLog.BackColor  = _darkMode ? Color.FromArgb(4, 4, 4) : TermBg;
        sidebar.BackColor = bg;

        foreach (TabPage tp in sidebar.TabPages)
        {
            tp.BackColor = bg;
            foreach (Control c in tp.Controls)
            {
                if (c is ListBox lb)
                {
                    lb.BackColor = _darkMode ? DarkPanel : White;
                    lb.ForeColor = _darkMode ? Color.WhiteSmoke : Color.Black;
                }
            }
        }

        btnTheme.Text      = _darkMode ? "🌙  Dark" : "☀  Theme";
        btnTheme.BackColor = _darkMode ? Color.FromArgb(210, 210, 210) : Color.FromArgb(30, 30, 30);
        btnTheme.ForeColor = _darkMode ? Color.Black : Color.White;
    }

    private void OnChooseFolder(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            SelectedPath           = _saveFolder,
            UseDescriptionForTitle = true,
            Description            = "Choose folder for scan result files",
        };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _saveFolder    = dialog.SelectedPath;
            lblFolder.Text = _saveFolder;
        }
    }

    /// <summary>
    /// When the user clicks a history entry, replay those results in the terminal.
    /// Also re-enables the export buttons so results can be re-exported.
    /// </summary>
    private void OnHistoryItemSelected(object? sender, EventArgs e)
    {
        if (lbHistory.SelectedItem is not ScanHistoryEntry entry) return;

        rtbLog.Clear();
        PrintLine($"History replay: {entry.Host} — {entry.Date:yyyy-MM-dd HH:mm:ss}", Color.Yellow);
        PrintLine(Divider(), Color.FromArgb(40, 160, 40));

        foreach (var r in entry.Results)
            PrintLine($"  {r.Timestamp:HH:mm:ss}    {r.Port,-6}  {r.Service,-14}  {r.DisplayBanner}", Green);

        PrintLine(Divider(), Color.FromArgb(40, 160, 40));
        PrintLine($"  {entry.OpenCount} open port(s)  |  {entry.Scanned} ports scanned", Color.Yellow);

        // Make history results available for export
        _lastResults = new List<ScanResult>(entry.Results);
        EnableExportButtons(_lastResults.Count > 0);
    }

    private void OnSaveProfile(object? sender, EventArgs e)
    {
        string? name = ShowInputDialog("Profile name:", "Save Profile", "My Profile");
        if (string.IsNullOrWhiteSpace(name)) return;

        _profiles.Add(new ScanProfile
        {
            Name        = name,
            Host        = txtHost.Text,
            StartPort   = (int)numStart.Value,
            EndPort     = (int)numEnd.Value,
            TimeoutMs   = (int)numTimeout.Value,
            Concurrency = (int)numThreads.Value,
            PingFirst   = chkPing.Checked,
            GrabBanners = chkBanner.Checked,
            Created     = DateTime.Now,
        });
        _storage.SaveProfiles(_profiles);
        RefreshProfileList();
    }

    private void OnLoadProfile(object? sender, EventArgs e)
    {
        if (lbProfiles.SelectedItem is not ScanProfile profile) return;

        txtHost.Text      = profile.Host;
        numStart.Value    = profile.StartPort;
        numEnd.Value      = profile.EndPort;
        numTimeout.Value  = profile.TimeoutMs;
        numThreads.Value  = profile.Concurrency;
        chkPing.Checked   = profile.PingFirst;
        chkBanner.Checked = profile.GrabBanners;

        MessageBox.Show($"Profile '{profile.Name}' loaded.", "op7",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void OnDeleteProfile(object? sender, EventArgs e)
    {
        if (lbProfiles.SelectedItem is not ScanProfile profile) return;
        _profiles.Remove(profile);
        _storage.SaveProfiles(_profiles);
        RefreshProfileList();
    }

    #endregion

    #region UI Helpers

    /// <summary>Enables or disables scan/stop buttons based on whether a scan is running.</summary>
    private void SetScanButtonState(bool scanning)
    {
        btnScan.Enabled   = !scanning;
        btnCommon.Enabled = !scanning;
        btnStop.Enabled   =  scanning;
        btnFolder.Enabled = !scanning;
    }

    /// <summary>Enables or disables TXT/CSV/JSON/Copy buttons.</summary>
    private void EnableExportButtons(bool enable)
    {
        btnCopy.Enabled    = enable;
        btnExpTxt.Enabled  = enable;
        btnExpCsv.Enabled  = enable;
        btnExpJson.Enabled = enable;
    }

    /// <summary>
    /// Appends a coloured line to the terminal log.
    /// Uses Invoke() if called from a background thread.
    /// </summary>
    private void PrintLine(string text, Color color)
    {
        if (rtbLog.InvokeRequired)
        {
            rtbLog.Invoke(() => PrintLine(text, color));
            return;
        }
        rtbLog.SelectionStart  = rtbLog.TextLength;
        rtbLog.SelectionLength = 0;
        rtbLog.SelectionColor  = color;
        rtbLog.AppendText(text + "\n");
        rtbLog.ScrollToCaret();
    }

    private void RefreshHistoryList()
    {
        lbHistory.Items.Clear();
        foreach (var entry in _history)
            lbHistory.Items.Add(entry);
    }

    private void RefreshProfileList()
    {
        lbProfiles.Items.Clear();
        foreach (var profile in _profiles)
            lbProfiles.Items.Add(profile);
    }

    /// <summary>Returns a divider line for the terminal output.</summary>
    private static string Divider() => new string('─', 62);

    /// <summary>
    /// Summarises the host list for the terminal header.
    /// Shows up to 4 hosts then "… +N more" to avoid very long lines.
    /// </summary>
    private static string HostSummary(List<string> hosts) =>
        hosts.Count <= 4
            ? string.Join(", ", hosts)
            : string.Join(", ", hosts.Take(4)) + $" … +{hosts.Count - 4} more";

    #endregion

    #region Widget Factories

    // These factory methods remove repetition from the UI builder methods.
    // Instead of setting 8 properties on every Label/Button, we call one method.

    private static Label MakeLabel(
        string text, Font? font, Rectangle bounds, Color foreColor,
        ContentAlignment align = ContentAlignment.MiddleLeft)
    {
        var label = new Label
        {
            Text      = text,
            Bounds    = bounds,
            ForeColor = foreColor,
            TextAlign = align,
        };
        if (font != null) label.Font = font;
        return label;
    }

    private static Button MakeButton(string text, int x, int y, int width, Color back, Color fore)
    {
        var button = new Button
        {
            Text      = text,
            Bounds    = new Rectangle(x, y, width, 28),
            BackColor = back,
            ForeColor = fore,
            FlatStyle = FlatStyle.Flat,
            Cursor    = Cursors.Hand,
            Font      = new Font("Segoe UI", 8.5f),
        };
        button.FlatAppearance.BorderSize  = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 80);
        return button;
    }

    private static NumericUpDown MakeSpinner(int x, int y, int width, int min, int max, int val)
        => new NumericUpDown
        {
            Bounds      = new Rectangle(x, y, width, 22),
            Minimum     = min,
            Maximum     = max,
            Value       = val,
            BorderStyle = BorderStyle.FixedSingle,
        };

    private static Panel MakeHLine(int x, int y, int width, Color? color = null)
        => new Panel
        {
            Bounds    = new Rectangle(x, y, width, 1),
            BackColor = color ?? Color.Black,
        };

    /// <summary>
    /// Simple inline input dialog — used for naming scan profiles.
    /// Returns null if the user cancels.
    /// </summary>
    private static string? ShowInputDialog(string prompt, string title, string defaultText = "")
    {
        using var form = new Form
        {
            Text            = title,
            Size            = new Size(370, 130),
            StartPosition   = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox     = false,
            MinimizeBox     = false,
        };
        var labelCtrl  = new Label  { Left = 10, Top = 14, Width = 340, Text = prompt };
        var textCtrl   = new TextBox{ Left = 10, Top = 34, Width = 340, Text = defaultText };
        var okButton   = new Button { Text = "OK",     Left = 180, Top = 62, Width = 80, DialogResult = DialogResult.OK     };
        var cancelBtn  = new Button { Text = "Cancel", Left = 266, Top = 62, Width = 80, DialogResult = DialogResult.Cancel };

        form.Controls.AddRange(new Control[] { labelCtrl, textCtrl, okButton, cancelBtn });
        form.AcceptButton = okButton;
        form.CancelButton = cancelBtn;

        return form.ShowDialog() == DialogResult.OK ? textCtrl.Text : null;
    }

    #endregion
}
