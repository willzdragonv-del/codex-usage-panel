using System.Text.Json;

namespace CodexUsage.Core;

public sealed record QuotaWindow(string Label, double? RemainingPercent, long? ResetsAt, int? WindowMins);
public sealed record QuotaBucket(string Id, string Name, IReadOnlyList<QuotaWindow> Windows);
public sealed record ResetCard(string Name, long? ExpiresAt);
public sealed record ResetSummary(int? AvailableCount, IReadOnlyList<ResetCard> Cards, string Note, bool DataAvailable);
public sealed record UsageSnapshot(IReadOnlyList<QuotaBucket> Buckets, ResetSummary ResetCredits, DateTimeOffset FetchedAt);

public static class UsageParser
{
    internal static JsonElement Get(JsonElement value, string name) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child) ? child : default;
    internal static string? Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    internal static long? Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var n) ? n : null;
    private static long? Timestamp(JsonElement value)
    {
        var n = Integer(value);
        return n is >= -62135596800 and <= 253402300799 ? n : null;
    }
    public static UsageSnapshot Parse(JsonElement result, DateTimeOffset now)
    {
        var buckets = new List<QuotaBucket>();
        var multi = Get(result, "rateLimitsByLimitId");
        if (multi.ValueKind == JsonValueKind.Object)
            foreach (var property in multi.EnumerateObject())
                if (property.Value.ValueKind == JsonValueKind.Object) buckets.Add(ParseBucket(property.Name, property.Value));
        var legacy = Get(result, "rateLimits");
        if (buckets.Count == 0 && legacy.ValueKind == JsonValueKind.Object)
            buckets.Add(ParseBucket(Text(Get(legacy,"limitId")) ?? "codex", legacy));
        return new(buckets, ParseCredits(Get(result,"rateLimitResetCredits"), now), now);
    }
    private static QuotaBucket ParseBucket(string id, JsonElement value)
    {
        var windows = new List<QuotaWindow>();
        foreach (var key in new[] { "primary", "secondary" })
        {
            var window = Get(value,key);
            if (window.ValueKind != JsonValueKind.Object) continue;
            var rawDuration = Integer(Get(window,"windowDurationMins"));
            int? duration = rawDuration is > 0 and <= int.MaxValue ? (int)rawDuration : null;
            var used = Get(window,"usedPercent");
            double? remaining = used.ValueKind == JsonValueKind.Number && used.TryGetDouble(out var percent) && double.IsFinite(percent) ? Math.Clamp(100-percent,0,100) : null;
            string label = duration switch {
                null => key == "primary" ? "主要额度" : "次要额度",
                int d when d % 10080 == 0 => $"{d/10080} 周额度",
                int d when d % 1440 == 0 => $"{d/1440} 天额度",
                int d when d % 60 == 0 => $"{d/60} 小时额度",
                _ => $"{duration} 分钟额度"
            };
            windows.Add(new(label, remaining, Timestamp(Get(window,"resetsAt")), duration));
        }
        return new(id, Text(Get(value,"limitName")) ?? (id == "codex" ? "Codex" : id), windows);
    }
    private static ResetSummary ParseCredits(JsonElement value, DateTimeOffset now)
    {
        if (value.ValueKind != JsonValueKind.Object) return new(null, [], "重置卡信息暂不可用", false);
        var rawCount = Integer(Get(value,"availableCount"));
        int? count = rawCount is >= 0 and <= int.MaxValue ? (int)rawCount : null;
        var detail = Get(value,"credits");
        if (detail.ValueKind != JsonValueKind.Array) return new(count, [], count == 0 ? "暂无可用重置卡" : "接口未提供卡片明细", true);
        var cards = new List<ResetCard>();
        foreach (var card in detail.EnumerateArray())
        {
            if (Text(Get(card,"status")) != "available") continue;
            var expires = Timestamp(Get(card,"expiresAt"));
            if (expires.HasValue && expires.Value <= now.ToUnixTimeSeconds()) continue;
            cards.Add(new(Text(Get(card,"title")) ?? "", expires));
        }
        cards = cards.OrderBy(c => c.ExpiresAt ?? long.MaxValue).Select((c,i) => c with { Name = string.IsNullOrWhiteSpace(c.Name) ? $"重置卡 {i+1}" : c.Name }).ToList();
        string note = "按过期时间排序 · 北京时间";
        if (count == 0)
        {
            note = cards.Count > 0 ? "数量与明细不一致，数据待刷新" : "暂无可用重置卡";
            cards.Clear();
        }
        else if (count.HasValue && cards.Count != count)
            note = cards.Count < count ? $"已返回 {cards.Count} 张明细，共 {count} 张可用" : "数量与明细不一致，数据待刷新";
        else if (!count.HasValue) note = "总数量未提供 · 以下为接口返回明细";
        return new(count, cards, note, true);
    }
}
public static class DisplayTime
{
    public static string At(long? timestamp)
    {
        if (!timestamp.HasValue) return "未提供";
        try { return DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss"); }
        catch (ArgumentOutOfRangeException) { return "未提供"; }
    }
    public static string Remaining(long? timestamp, DateTimeOffset now)
    {
        if (!timestamp.HasValue) return "时间未提供";
        TimeSpan remaining;
        try { remaining = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value) - now; }
        catch (ArgumentOutOfRangeException) { return "时间未提供"; }
        if (remaining <= TimeSpan.Zero) return "等待刷新";
        if (remaining.TotalDays >= 1) return $"{(int)remaining.TotalDays} 天 {remaining.Hours} 小时";
        if (remaining.TotalHours >= 1) return $"{(int)remaining.TotalHours} 小时 {remaining.Minutes} 分钟";
        return $"{Math.Max(1,(int)Math.Ceiling(remaining.TotalMinutes))} 分钟";
    }
}
