using System.Diagnostics;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Text;

namespace CodexUsage.Core;

public interface ICodexConnection : IDisposable
{
    event Action<string, JsonElement>? NotificationReceived;
    Task StartAsync();
    Task<JsonElement> CallAsync(string method, object? parameters = null);
}

public sealed class CodexClient : ICodexConnection
{
    private readonly ProcessStartInfo startInfo;
    private readonly TimeSpan timeout;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writer = new(1,1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly object startLock = new();
    private Process? process;
    private Task? starting;
    private int requestId, disposed;
    public event Action<string, JsonElement>? NotificationReceived;
    public CodexClient(ProcessStartInfo startInfo, TimeSpan? timeout = null)
    {
        this.startInfo = startInfo;
        this.timeout = timeout ?? TimeSpan.FromSeconds(25);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = startInfo.RedirectStandardOutput = startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = startInfo.StandardErrorEncoding = Encoding.UTF8;
        startInfo.StandardInputEncoding = new UTF8Encoding(false);
    }
    public Task StartAsync()
    {
        lock (startLock) return starting ??= InitializeAsync();
    }
    private async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        process = new Process { StartInfo = startInfo };
        process.Start();
        _ = ReadLoopAsync(process);
        _ = DrainErrorsAsync(process);
        await CallAsync("initialize", new { clientInfo = new { name = "codex_usage_panel", title = "Codex Usage Panel", version = "1.0.0" } }).ConfigureAwait(false);
        await SendAsync(new { method = "initialized", @params = new { } }).ConfigureAwait(false);
    }
    private async Task SendAsync(object message)
    {
        await writer.WaitAsync(lifetime.Token).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed != 0, this);
            if (process == null || process.HasExited) throw new IOException("Codex 后台未运行");
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message)).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
        }
        finally { writer.Release(); }
    }
    public async Task<JsonElement> CallAsync(string method, object? parameters = null)
    {
        ObjectDisposedException.ThrowIf(disposed != 0, this);
        int id = Interlocked.Increment(ref requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try
        {
            await SendAsync(new { id, method, @params = parameters }).ConfigureAwait(false);
            return await completion.Task.WaitAsync(timeout, lifetime.Token).ConfigureAwait(false);
        }
        finally { pending.TryRemove(id,out _); }
    }
    private async Task ReadLoopAsync(Process owned)
    {
        try
        {
            while (await owned.StandardOutput.ReadLineAsync(lifetime.Token).ConfigureAwait(false) is { } line)
            {
                JsonDocument document;
                try { document = JsonDocument.Parse(line); } catch (JsonException) { continue; }
                using (document)
                {
                    var message = document.RootElement;
                    if (message.TryGetProperty("method",out var method))
                    {
                        if (message.TryGetProperty("id",out var serverId))
                            await SendAsync(new { id = serverId.Clone(), error = new { code = -32601, message = "Unsupported client method" } }).ConfigureAwait(false);
                        else
                        {
                            try { NotificationReceived?.Invoke(method.GetString() ?? "", UsageParser.Get(message,"params").Clone()); }
                            catch { /* A subscriber cannot break protocol response handling. */ }
                        }
                        continue;
                    }
                    if (!message.TryGetProperty("id",out var id) || !id.TryGetInt32(out int number) || !pending.TryRemove(number,out var completion)) continue;
                    if (message.TryGetProperty("error",out var error)) completion.TrySetException(new CodexRpcException(UsageParser.Text(UsageParser.Get(error,"message")) ?? "Codex 请求失败"));
                    else completion.TrySetResult(UsageParser.Get(message,"result").Clone());
                }
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
        finally
        {
            foreach (var entry in pending)
                if (pending.TryRemove(entry.Key,out var completion)) completion.TrySetException(new IOException("Codex 后台已退出，请重新刷新"));
        }
    }
    private async Task DrainErrorsAsync(Process owned)
    {
        try { while (await owned.StandardError.ReadLineAsync(lifetime.Token).ConfigureAwait(false) != null) { } }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException or InvalidOperationException) { }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed,1) != 0) return;
        lifetime.Cancel();
        foreach (var entry in pending)
            if (pending.TryRemove(entry.Key,out var completion)) completion.TrySetException(new IOException("Codex 查询已停止"));
        if (process != null)
        {
            try { if (!process.HasExited) { process.Kill(entireProcessTree:true); process.WaitForExit(2000); } }
            catch (InvalidOperationException) { }
            process.Dispose();
        }
    }
}
