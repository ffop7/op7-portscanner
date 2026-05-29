using System.Text.Json;
using Op7PortScanner.Models;

namespace Op7PortScanner.Services;

// ──────────────────────────────────────────────────────────────────────────────
//  PersistenceService — saves and loads history and profiles to disk.
//
//  Storage location: %APPDATA%\Op7PortScanner\
//    history.json   — last 30 scan results (auto-saved after each scan)
//    profiles.json  — user-saved scan configurations
//
//  We use System.Text.Json which is built into .NET 8 — no NuGet packages needed.
//  Both files are human-readable JSON so you can open and edit them manually.
// ──────────────────────────────────────────────────────────────────────────────
public class PersistenceService
{
    #region Setup

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // Base folder: C:\Users\<you>\AppData\Roaming\Op7PortScanner\
    private static readonly string StorageDir =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Op7PortScanner");

    private static readonly string HistoryFile  = Path.Combine(StorageDir, "history.json");
    private static readonly string ProfilesFile = Path.Combine(StorageDir, "profiles.json");

    // Maximum history entries to keep. Older ones are trimmed automatically.
    private const int MaxHistoryEntries = 30;

    public PersistenceService()
    {
        // Create the storage folder if it doesn't exist yet.
        // This happens silently on first run.
        try { Directory.CreateDirectory(StorageDir); }
        catch { /* If we can't create it, save/load will simply fail silently. */ }
    }

    #endregion

    #region History

    /// <summary>
    /// Loads scan history from disk.
    /// Returns an empty list if the file doesn't exist or can't be parsed.
    /// </summary>
    public List<ScanHistoryEntry> LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryFile))
                return new List<ScanHistoryEntry>();

            var json = File.ReadAllText(HistoryFile);
            return JsonSerializer.Deserialize<List<ScanHistoryEntry>>(json) ?? new();
        }
        catch
        {
            // Corrupted file or permission error — start fresh.
            return new List<ScanHistoryEntry>();
        }
    }

    /// <summary>
    /// Saves scan history to disk.
    /// Automatically trims to the most recent <see cref="MaxHistoryEntries"/> entries
    /// so the file never grows unbounded.
    /// </summary>
    public void SaveHistory(List<ScanHistoryEntry> history)
    {
        try
        {
            // Keep only the most recent entries, ordered newest-first.
            var trimmed = history
                .OrderByDescending(h => h.Date)
                .Take(MaxHistoryEntries)
                .ToList();

            File.WriteAllText(HistoryFile, JsonSerializer.Serialize(trimmed, JsonOpts));
        }
        catch { /* Disk full or permissions issue — fail silently. */ }
    }

    #endregion

    #region Profiles

    /// <summary>
    /// Loads saved scan profiles from disk.
    /// Returns an empty list if the file doesn't exist or can't be parsed.
    /// </summary>
    public List<ScanProfile> LoadProfiles()
    {
        try
        {
            if (!File.Exists(ProfilesFile))
                return new List<ScanProfile>();

            var json = File.ReadAllText(ProfilesFile);
            return JsonSerializer.Deserialize<List<ScanProfile>>(json) ?? new();
        }
        catch
        {
            return new List<ScanProfile>();
        }
    }

    /// <summary>
    /// Saves all profiles to disk.
    /// Called whenever the user saves, loads, or deletes a profile.
    /// </summary>
    public void SaveProfiles(List<ScanProfile> profiles)
    {
        try
        {
            File.WriteAllText(ProfilesFile, JsonSerializer.Serialize(profiles, JsonOpts));
        }
        catch { }
    }

    #endregion
}
