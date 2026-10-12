using System;
using System.IO;
using System.Text.Json;

namespace CodexUsage.Desktop;

public sealed class AppSettings
{
    public string? CodexPath { get; set; }
    public double BallSize { get; set; } = 88;
    public double BallOpacity { get; set; } = 1;
    public bool BallTopmost { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 300;
    internal void Normalize()
    {
        BallSize=double.IsFinite(BallSize) ? Math.Clamp(BallSize,64,144) : 88;
        BallOpacity=double.IsFinite(BallOpacity) ? Math.Clamp(BallOpacity,.35,1) : 1;
        if (RefreshIntervalSeconds is not (15 or 60 or 120 or 300 or 600)) RefreshIntervalSeconds=300;
    }
}

public sealed class AppSettingsStore
{
    private readonly string path;
    private static readonly JsonSerializerOptions options=new() { PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true,WriteIndented=true };
    public AppSettingsStore(string path) { this.path=path; }
    public AppSettings Load()
    {
        try
        {
            var settings=JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path),options) ?? new AppSettings();
            settings.Normalize(); return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new AppSettings(); }
    }
    public void Save(AppSettings settings)
    {
        settings.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary=path+".tmp";
        try { File.WriteAllText(temporary,JsonSerializer.Serialize(settings,options)); File.Move(temporary,path,true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
