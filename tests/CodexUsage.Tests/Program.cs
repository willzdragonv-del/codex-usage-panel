using System.Text.Json;
using CodexUsage.Core;

internal static class Program
{
    private static int failures;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1791800000);
    private static UsageSnapshot Parse(string json) => UsageParser.Parse(JsonDocument.Parse(json).RootElement, Now);
    private static void Equal<T>(T actual, T expected)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected)) throw new Exception($"Expected {expected}; got {actual}");
    }
    private static void Check(string name, Action test)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    private static async Task CheckAsync(string name, Func<Task> test)
    {
        try { await test(); Console.WriteLine("PASS " + name); }
        catch (Exception e) { failures++; Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    public static async Task<int> Main(string[] args)
    {
        if (args.Contains("--fake-server")) { await FakeServer.RunAsync(args.Length > 1 ? args[1] : null); return 0; }
        if (args.Contains("--live"))
        {
            using var panel = new PanelController(() => new CodexClient(new System.Diagnostics.ProcessStartInfo(CodexLocator.Find() ?? throw new Exception("Codex not found"),"app-server --listen stdio://")));
            await panel.RefreshAsync();
            if (panel.Snapshot is null) { Console.WriteLine("FAIL live query: " + panel.Status); return 1; }
            Console.WriteLine(JsonSerializer.Serialize(new { Executable = CodexLocator.Find(), Buckets = panel.Snapshot.Buckets.Count, AvailableResetCards = panel.Snapshot.ResetCredits.AvailableCount, Expirations = panel.Snapshot.ResetCredits.Cards.Select(c => DisplayTime.At(c.ExpiresAt)) }));
            return 0;
        }
        Check("prefer multi-bucket over legacy", () => {
            var s = Parse("""{"rateLimits":{"primary":{"usedPercent":80}},"rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":25,"windowDurationMins":300,"resetsAt":1792699217}},"other":{"primary":{"usedPercent":40}}}}""");
            Equal(s.Buckets.Count, 2); Equal(s.Buckets[0].Windows[0].RemainingPercent, 75d);
        });
        Check("legacy fallback and missing percentage remains unknown", () => {
            var s = Parse("""{"rateLimits":{"primary":{"windowDurationMins":10080},"secondary":{"usedPercent":110}}}""");
            Equal(s.Buckets.Count, 1); Equal(s.Buckets[0].Windows[0].RemainingPercent, null); Equal(s.Buckets[0].Windows[1].RemainingPercent, 0d);
        });
        Check("clamp negative used percentage", () => Equal(Parse("""{"rateLimits":{"primary":{"usedPercent":-5}}}""").Buckets[0].Windows[0].RemainingPercent, 100d));
        Check("empty multi-bucket falls back", () => Equal(Parse("""{"rateLimitsByLimitId":{},"rateLimits":{"primary":{"usedPercent":30}}}""").Buckets.Count, 1));
        Check("Beijing conversion independent of machine timezone", () => Equal(DisplayTime.At(1792699217), "2026-10-23 04:00:17"));
        Check("expired countdown and invalid timestamp", () => {
            Equal(DisplayTime.Remaining(1791799999, Now), "等待刷新"); Equal(DisplayTime.At(long.MaxValue), "未提供");
        });
        Check("reset total is authoritative, details sorted, expired filtered", () => {
            var r = Parse("""{"rateLimitResetCredits":{"availableCount":5,"credits":[{"title":"later","status":"available","expiresAt":1794002656},{"title":"expired","status":"available","expiresAt":1},{"title":"unknown","status":"available","expiresAt":null},{"title":"earlier","status":"available","expiresAt":1792699217},{"title":"used","status":"redeemed","expiresAt":1792699217}]}}""").ResetCredits;
            Equal(r.AvailableCount, 5); Equal(r.Cards.Count, 3); Equal(r.Cards[0].Name, "earlier"); Equal(r.Cards[2].ExpiresAt, null);
            Equal(r.Note.Contains("3"), true); Equal(r.Note.Contains("5"), true);
        });
        Check("null credit field is unavailable, not zero", () => {
            var r = Parse("""{"rateLimitResetCredits":null}""").ResetCredits;
            Equal(r.AvailableCount, null); Equal(r.DataAvailable, false);
        });
        Check("count without detail", () => {
            var r = Parse("""{"rateLimitResetCredits":{"availableCount":3,"credits":null}}""").ResetCredits;
            Equal(r.AvailableCount, 3); Equal(r.Note.Contains("未提供"), true);
        });
        Check("unknown count stays unknown", () => Equal(Parse("""{"rateLimitResetCredits":{"credits":[]}}""").ResetCredits.AvailableCount, null));
        Check("zero reset cards", () => {
            var r = Parse("""{"rateLimitResetCredits":{"availableCount":0,"credits":[]}}""").ResetCredits;
            Equal(r.AvailableCount, 0); Equal(r.Note.Contains("暂无"), true);
        });
        Check("zero count without details is still no usable cards", () => {
            var r = Parse("""{"rateLimitResetCredits":{"availableCount":0,"credits":null}}""").ResetCredits;
            Equal(r.Note.Contains("暂无"), true);
        });
        Check("contradicting zero count does not display usable cards", () => {
            var r = Parse("""{"rateLimitResetCredits":{"availableCount":0,"credits":[{"status":"available","expiresAt":1792699217}]}}""").ResetCredits;
            Equal(r.Cards.Count, 0); Equal(r.Note.Contains("待刷新"), true);
        });
        await ProtocolTests.RunAsync(CheckAsync);
        await ControllerTests.RunAsync(CheckAsync);
        Console.WriteLine($"Failures: {failures}");
        return failures == 0 ? 0 : 1;
    }
}
