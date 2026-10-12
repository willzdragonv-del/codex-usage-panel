using System.Diagnostics;
using CodexUsage.Core;

internal static class ProtocolTests
{
    public static CodexClient Client(TimeSpan? timeout = null) => new(new ProcessStartInfo(Environment.ProcessPath!, "--fake-server"), timeout);
    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("handshake, stderr drain, out-of-order replies and notification", async () => {
            using var c = Client();
            int notifications = 0;
            c.NotificationReceived += (_, _) => Interlocked.Increment(ref notifications);
            await c.StartAsync();
            var slow = c.CallAsync("slow"); var fast = c.CallAsync("fast");
            if ((await fast).GetProperty("value").GetString() != "fast") throw new Exception("Wrong response ID");
            if ((await slow).GetProperty("value").GetString() != "slow") throw new Exception("Wrong response ID");
            if (notifications != 2) throw new Exception("Notification missing");
        });
        await check("server errors surface", async () => {
            using var c = Client(); await c.StartAsync();
            try { await c.CallAsync("error"); throw new Exception("Error swallowed"); }
            catch (CodexRpcException) { }
        });
        await check("hung request is bounded", async () => {
            using var c = Client(TimeSpan.FromMilliseconds(800)); await c.StartAsync();
            var clock = Stopwatch.StartNew();
            try { await c.CallAsync("hang"); throw new Exception("Timeout missing"); } catch (TimeoutException) { }
            if (clock.Elapsed > TimeSpan.FromSeconds(4)) throw new Exception("Unbounded timeout");
        });
        await check("early server exit fails pending requests promptly", async () => {
            using var c = Client(); await c.StartAsync();
            var pending = c.CallAsync("exit");
            try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("Exit swallowed"); }
            catch (IOException) { }
        });
        await check("dispose kills only owned process", async () => {
            var c = Client(); await c.StartAsync();
            int pid = (await c.CallAsync("pid")).GetProperty("pid").GetInt32();
            using var process = Process.GetProcessById(pid);
            c.Dispose();
            if (!process.WaitForExit(3000)) throw new Exception("Child process leaked");
        });
    }
}
