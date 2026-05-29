# op7 port scanner v3

> Fast parallel TCP port scanner for Windows — clean code, full comments, optimized engine.

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Language](https://img.shields.io/badge/language-C%23-purple)
![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)
![License](https://img.shields.io/badge/license-MIT-green)

---

## What changed in v3 (vs v2)

| Area | Change |
|---|---|
| **Comments** | Every method has an XML doc comment explaining what it does AND why |
| **Constants** | No magic numbers — `BannerBufferSize`, `BannerReadDelayMs`, `MaxHistoryEntries` etc. |
| **Memory** | `ArrayPool<byte>.Shared` reuses banner buffers instead of allocating new arrays |
| **Structure** | Every file uses `#region` blocks to navigate large files easily |
| **Methods** | Long methods broken into smaller, well-named ones (`BuildHeaderPanel`, `AutoSaveTxtAsync`, etc.) |
| **Naming** | Clearer variable names throughout (`totalDone`, `resolvedIp`, `wasCancelled`) |
| **Factories** | Widget factory methods (`MakeLabel`, `MakeButton`, `MakeSpinner`) remove UI repetition |
| **DNS** | Now prefers IPv4 when a hostname resolves to multiple addresses |
| **OS guess** | Extracted into its own `GuessOsFromTtl()` method with a clear explanation |

---

## Quick Start

### Requirements
- Windows 10 / 11 (64-bit)
- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build & Run
Double-click **`BUILD_AND_RUN.bat`**

---

## File Structure

```
op7-portscanner/
├── Models/
│   ├── ScanResult.cs         — What we know about one open port
│   ├── ScanProfile.cs        — A saved scan configuration
│   └── ScanHistoryEntry.cs   — One past scan stored in history
├── Services/
│   ├── ScanEngine.cs         — Parallel async TCP scanner + banner grabber
│   ├── NetworkUtils.cs       — Ping, DNS, OS fingerprint, host range parser
│   ├── ExportService.cs      — Save results as TXT / CSV / JSON
│   └── PersistenceService.cs — Load/save history & profiles to AppData
├── MainForm.cs               — GUI — all UI code with full comments
├── FlowerArt.cs              — B&W Aster/Flowey illustration (GDI+)
├── Program.cs                — Entry point ([STAThread] + Application.Run)
├── Op7PortScanner.csproj     — Project file (no NuGet packages needed)
└── BUILD_AND_RUN.bat         — One-click build + launch
```

---

## Manual Build

```bash
dotnet publish Op7PortScanner.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o dist
```
