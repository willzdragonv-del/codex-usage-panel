namespace CodexUsage.Core;

public static class CodexLocator
{
    public static string? Find()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try { var path = Path.Combine(directory.Trim('"'), "codex.exe"); if (File.Exists(path)) return Path.GetFullPath(path); }
            catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { }
        }
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        try
        {
            if (Directory.Exists(root))
                return Directory.EnumerateFiles(root,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }
}
