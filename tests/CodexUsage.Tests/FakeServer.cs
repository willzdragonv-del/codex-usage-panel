using System.Text.Json;

internal static class FakeServer
{
    private static readonly object OutputLock = new();
    private static void Send(object value) { lock (OutputLock) Console.WriteLine(JsonSerializer.Serialize(value)); }
    public static async Task RunAsync(string? stateDirectory)
    {
        bool initialized = false, failUsage = false, announcedAccount = false;
        string account = "person@example.test";
        while (await Console.In.ReadLineAsync() is { } line)
        {
            if (stateDirectory != null)
            {
                failUsage = File.Exists(Path.Combine(stateDirectory,"fail"));
                account = File.Exists(Path.Combine(stateDirectory,"second")) ? "second@example.test" : "person@example.test";
            }
            var msg = JsonDocument.Parse(line).RootElement;
            string? method = msg.GetProperty("method").GetString();
            if (method == "initialized") { initialized = true; continue; }
            int id = msg.GetProperty("id").GetInt32();
            if (method == "initialize") { Send(new { id, result = new { userAgent = "fake" } }); continue; }
            if (!initialized) { Send(new { id, error = new { code = -1, message = "Not initialized" } }); continue; }
            switch (method)
            {
                case "exit": return;
                case "hang": break;
                case "error": Send(new { id, error = new { code = -1, message = "deliberate failure" } }); break;
                case "pid": Send(new { id, result = new { pid = Environment.ProcessId } }); break;
                case "set/fail": failUsage = true; Send(new { id, result = new { } }); break;
                case "set/account": account = "second@example.test"; Send(new { id, result = new { } }); break;
                case "account/login/start":
                    if (stateDirectory == null) throw new Exception("Login test requires state directory");
                    File.WriteAllText(Path.Combine(stateDirectory,"login-pid"),Environment.ProcessId.ToString());
                    Send(new { id, result = new { loginId = "test-login", authUrl = "https://auth.openai.com/test-login" } });
                    _ = Task.Run(async () => {
                        while (!File.Exists(Path.Combine(stateDirectory,"login-complete"))) await Task.Delay(25);
                        File.Delete(Path.Combine(stateDirectory,"logged-out"));
                        Send(new { method="account/login/completed", @params=new { success=true, loginId="test-login", error=(string?)null } });
                    });
                    break;
                case "account/login/cancel": Send(new { id, result = new { } }); break;
                case "account/read":
                    if (!announcedAccount && stateDirectory != null && File.Exists(Path.Combine(stateDirectory,"startup-update")))
                    {
                        announcedAccount=true;
                        Send(new { method="account/updated", @params=new { authMode="chatgpt", planType="pro" } });
                    }
                    if (stateDirectory != null && File.Exists(Path.Combine(stateDirectory,"logged-out"))) Send(new { id, result = new { account = (object?)null } });
                    else Send(new { id, result = new { account = new { type = "chatgpt", email = account, planType = "pro" } } });
                    break;
                case "account/rateLimits/read":
                    if (stateDirectory != null)
                    {
                        string counter = Path.Combine(stateDirectory,"usage-count");
                        int count = File.Exists(counter) ? int.Parse(File.ReadAllText(counter)) : 0;
                        File.WriteAllText(counter,(count+1).ToString());
                        string changing = Path.Combine(stateDirectory,"account-change");
                        if (File.Exists(changing))
                        {
                            File.Delete(changing); File.WriteAllText(Path.Combine(stateDirectory,"second"),"");
                            Send(new { method="account/updated", @params=new { authMode="chatgpt", planType="pro" } });
                        }
                    }
                    if (failUsage) Send(new { id, error = new { code = -1, message = "offline" } });
                    else
                    {
                        bool needsRetry = stateDirectory != null && File.Exists(Path.Combine(stateDirectory,"retry-details"));
                        bool detailsReady = needsRetry && int.Parse(File.ReadAllText(Path.Combine(stateDirectory!,"usage-count"))) >= 2;
                        object? cards = detailsReady ? new[] { new { status="available", expiresAt=1794002656L } } : null;
                        Send(new { id, result = new { accountId = account == "person@example.test" ? "account-one" : "account-two", rateLimits = new { primary = new { usedPercent = 25, windowDurationMins = 300, resetsAt = 1792699217 } }, rateLimitResetCredits = new { availableCount = 3, credits = cards } } });
                    }
                    break;
                default:
                    Console.Error.WriteLine(new string('x', 10000));
                    Send(new { method = "account/rateLimits/updated", @params = new { test = true } });
                    var delay = method == "slow" ? 100 : 1;
                    _ = Task.Run(async () => { await Task.Delay(delay); Send(new { id, result = new { value = method } }); });
                    break;
            }
        }
    }
}
