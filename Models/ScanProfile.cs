namespace Op7PortScanner.Models;

public class ScanProfile
{
    public string   Name        { get; set; } = "New Profile";
    public string   Host        { get; set; } = "127.0.0.1";
    public int      StartPort   { get; set; } = 1;
    public int      EndPort     { get; set; } = 1024;
    public int      TimeoutMs   { get; set; } = 300;
    public int      Concurrency { get; set; } = 1500;
    public bool     PingFirst   { get; set; } = true;
    public bool     GrabBanners { get; set; } = true;
    public DateTime Created     { get; set; } = DateTime.Now;

    public override string ToString() => $"{Name}  [{Host}  {StartPort}-{EndPort}]";
}
