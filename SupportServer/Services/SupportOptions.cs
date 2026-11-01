namespace VisionRP.SupportServer.Services;

public sealed class SupportOptions
{
    public string AdminToken { get; set; } = string.Empty;

    public string? DataPath { get; set; }

    public string[] TrustedProxies { get; set; } = [];
}
