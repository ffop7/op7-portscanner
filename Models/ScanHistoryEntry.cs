namespace Op7PortScanner.Models;

public class ScanHistoryEntry
{
    public string   Host      { get; set; } = "";
    public DateTime Date      { get; set; } = DateTime.Now;
    public int      Scanned   { get; set; }
    public int      OpenCount { get; set; }
    public List<ScanResult> Results { get; set; } = new();

    public override string ToString() =>
        $"[{Date:MM/dd HH:mm}]  {Host,-22}  {OpenCount} open / {Scanned} scanned";
}
