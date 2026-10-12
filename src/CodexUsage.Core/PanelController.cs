using System.Text.Json;
namespace CodexUsage.Core;

public sealed class PanelController : IDisposable
{
    private readonly Func<ICodexConnection> factory;
    private readonly object gate = new();
    private readonly SemaphoreSlim operations = new(1,1);
    private ICodexConnection? connection;
    private Task? refreshing;
    private string? accountKey, loginId;
    private int failures, accountVersion;
    private bool disposed, refreshRequested;
    private Timer? loginTimeout;
    public UsageSnapshot? Snapshot { get; private set; }
    public string AccountLabel { get; private set; } = "正在连接账号";
    public string Status { get; private set; } = "等待查询";
    public bool IsStale { get; private set; }
    public bool IsBusy { get; private set; }
    public bool NeedsLogin { get; private set; }
    public bool IsLoginPending { get; private set; }
    private TimeSpan refreshInterval=TimeSpan.FromMinutes(5);
    public TimeSpan RefreshInterval
    {
        get=>refreshInterval;
        set
        {
            var bounded=TimeSpan.FromSeconds(Math.Clamp(value.TotalSeconds,15,600));
            if (bounded==refreshInterval) return;
            refreshInterval=bounded;
            if (Snapshot!=null && !IsStale && !IsBusy && !IsLoginPending) Status=SyncedStatus;
            Changed?.Invoke();
        }
    }
    private string SyncedStatus=>"已同步 · 每 "+(refreshInterval.TotalSeconds<60 ? $"{refreshInterval.TotalSeconds:0} 秒" : $"{refreshInterval.TotalMinutes:0} 分钟")+"自动刷新";
    public TimeSpan NextRefreshDelay => failures==0 ? refreshInterval : TimeSpan.FromMinutes(Math.Min(30,5 * Math.Pow(2,Math.Min(failures,3))));
    public event Action? Changed;
    public PanelController(Func<ICodexConnection> factory) { this.factory = factory; }
    private async Task<ICodexConnection> ConnectAsync()
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        connection = factory();
        connection.NotificationReceived += OnNotification;
        await connection.StartAsync().ConfigureAwait(false);
        return connection;
    }
    private void OnNotification(string method, JsonElement data)
    {
        if (method == "account/updated")
        {
            lock (gate) { accountVersion++; IsStale=true; refreshRequested=true; }
            _ = RefreshAsync();
        }
        else if (method == "account/login/completed")
        {
            IsLoginPending = false; loginTimeout?.Dispose();
            bool success = UsageParser.Get(data,"success").ValueKind == JsonValueKind.True;
            if (success) _ = RefreshAsync();
            else { Status = "登录未完成，请重试"; NeedsLogin = true; Changed?.Invoke(); }
        }
    }
    public Task RefreshAsync()
    {
        lock (gate)
        {
            if (disposed || IsLoginPending) return Task.CompletedTask;
            if (refreshing is { IsCompleted: false }) return refreshing;
            return refreshing = Task.Run(RefreshLoopAsync);
        }
    }
    private async Task RefreshLoopAsync()
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed || IsLoginPending) return;
            for (int attempt=0; attempt<3; attempt++)
            {
                lock (gate) refreshRequested = false;
                await RefreshCoreAsync().ConfigureAwait(false);
                lock (gate) if (!refreshRequested || disposed || IsLoginPending) { refreshing=null; return; }
            }
            lock (gate) refreshing=null;
        }
        finally { operations.Release(); }
    }
    private async Task RefreshCoreAsync()
    {
        IsBusy = true; Status = "正在更新…"; Changed?.Invoke();
        try
        {
            // Fresh processes avoid account/read's count-only credit cache in current Codex.
            // Dispose the previous query/login connection before creating its replacement.
            ResetConnection();
            var client = await ConnectAsync().ConfigureAwait(false);
            int version = Volatile.Read(ref accountVersion);
            JsonElement usage = default;
            Exception? usageError = null;
            try
            {
                usage=await client.CallAsync("account/rateLimits/read",new { excludeResetCreditDetails=false }).ConfigureAwait(false);
                var credits=UsageParser.Get(usage,"rateLimitResetCredits");
                if (UsageParser.Integer(UsageParser.Get(credits,"availableCount")) > 0 && UsageParser.Get(credits,"credits").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    // Detail lookup can fail independently. Retry once before account/read changes cache state.
                    try { usage=await client.CallAsync("account/rateLimits/read",new { excludeResetCreditDetails=false }).ConfigureAwait(false); }
                    catch { /* Preserve the first valid count if the optional detail retry fails. */ }
                }
            }
            catch (Exception e) { usageError = e; }
            // Always check identity, even if usage fails, to invalidate another account's data.
            var read = await client.CallAsync("account/read",new { refreshToken=false }).ConfigureAwait(false);
            var account = UsageParser.Get(read,"account");
            if (account.ValueKind != JsonValueKind.Object)
            {
                Snapshot = null; accountKey = null; AccountLabel = "尚未登录"; NeedsLogin = true;
                throw new InvalidOperationException("请点击「登录账号」完成登录");
            }
            var type = UsageParser.Text(UsageParser.Get(account,"type"));
            string email = UsageParser.Text(UsageParser.Get(account,"email")) ?? "当前账号";
            string plan = UsageParser.Text(UsageParser.Get(account,"planType")) ?? "";
            string key = $"{type}|{email}|{plan}";
            if (accountKey != key) Snapshot = null;
            accountKey = key;
            AccountLabel = string.IsNullOrEmpty(plan) ? email : $"{email} · {plan.ToUpperInvariant()}";
            NeedsLogin = false;
            if (type == "apiKey") { NeedsLogin=true; throw new InvalidOperationException("当前使用 API Key，请登录 ChatGPT 账号查询套餐额度"); }
            if (usageError == null && version != Volatile.Read(ref accountVersion))
            {
                // account/read also emits an initial account/updated. Verify response account IDs
                // to distinguish this from an actual identity change without discarding details.
                version=Volatile.Read(ref accountVersion);
                var confirmation=await client.CallAsync("account/rateLimits/read",new { excludeResetCreditDetails=true }).ConfigureAwait(false);
                string? firstId=UsageParser.Text(UsageParser.Get(usage,"accountId"));
                string? confirmedId=UsageParser.Text(UsageParser.Get(confirmation,"accountId"));
                if (firstId == null || firstId != confirmedId)
                {
                    Snapshot=null; IsStale=true; Status="账号发生变化，正在重新查询…";
                    lock (gate) refreshRequested=true;
                    return;
                }
            }
            if (usageError != null) throw usageError;
            if (disposed) return;
            lock (gate)
            {
                if (version != accountVersion) { Snapshot=null; IsStale=true; refreshRequested=true; Status="账号发生变化，正在重新查询…"; return; }
                Snapshot=UsageParser.Parse(usage,DateTimeOffset.UtcNow);
                IsStale=false; failures=0; refreshRequested=false; Status=SyncedStatus;
            }
        }
        catch (Exception e)
        {
            IsStale = true; failures++;
            var message = e is TimeoutException ? "查询超时，请检查网络后刷新" : e.Message;
            Status = (Snapshot == null ? "查询失败：" : "数据已过时：") + message.Replace('\n',' ').Replace('\r',' ');
            if (Status.Length > 180) Status = Status[..180];
            if (message.Contains("401") || message.Contains("Unauthorized",StringComparison.OrdinalIgnoreCase)) NeedsLogin=true;
        }
        finally { ResetConnection(); IsBusy=false; if (!disposed) Changed?.Invoke(); }
    }
    public async Task<string?> LoginAsync()
    {
        lock (gate)
        {
            if (disposed || IsLoginPending) return null;
            IsLoginPending=true;
        }
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return null;
            IsBusy=true; Changed?.Invoke(); ResetConnection();
            var client = await ConnectAsync().ConfigureAwait(false);
            var result = await client.CallAsync("account/login/start",new { type="chatgpt" }).ConfigureAwait(false);
            loginId = UsageParser.Text(UsageParser.Get(result,"loginId"));
            var url = UsageParser.Text(UsageParser.Get(result,"authUrl"));
            if (url == null) throw new InvalidOperationException("未取得登录链接，请重试");
            Status="请在浏览器完成登录，完成后自动刷新";
            loginTimeout?.Dispose();
            if (IsLoginPending) loginTimeout=new Timer(_ => { _ = CancelLoginAsync(); },null,TimeSpan.FromMinutes(5),Timeout.InfiniteTimeSpan);
            return url;
        }
        catch (Exception e) { IsLoginPending=false; Status="登录未完成："+e.Message; ResetConnection(); return null; }
        finally { IsBusy=false; operations.Release(); if (!disposed) Changed?.Invoke(); }
    }
    public async Task CancelLoginAsync()
    {
        await operations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsLoginPending) return;
            if (connection != null && loginId != null)
                try { await connection.CallAsync("account/login/cancel",new { loginId }).ConfigureAwait(false); } catch { }
            IsLoginPending=false; Status="已取消登录，可以重新登录"; NeedsLogin=true;
            loginTimeout?.Dispose(); ResetConnection();
        }
        finally { operations.Release(); if (!disposed) Changed?.Invoke(); }
    }
    public void ResetConnection()
    {
        var previous=Interlocked.Exchange(ref connection,null);
        if (previous != null) { previous.NotificationReceived-=OnNotification; previous.Dispose(); }
    }
    public void Dispose() { disposed=true; loginTimeout?.Dispose(); ResetConnection(); }
}
