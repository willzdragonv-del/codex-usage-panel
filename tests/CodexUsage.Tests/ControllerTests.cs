using CodexUsage.Core;
using System.Diagnostics;

internal static class ControllerTests
{
    private sealed class Fixture : IDisposable
    {
        internal readonly string DirectoryPath = Path.Combine(Path.GetTempPath(),"codex-usage-test-"+Guid.NewGuid().ToString("N"));
        internal Fixture() => Directory.CreateDirectory(DirectoryPath);
        internal void Set(string name) => File.WriteAllText(Path.Combine(DirectoryPath,name),"");
        internal CodexClient Client()
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!);
            info.ArgumentList.Add("--fake-server"); info.ArgumentList.Add(DirectoryPath);
            return new(info);
        }
        public void Dispose() => Directory.Delete(DirectoryPath,true);
    }
    public static async Task RunAsync(Func<string, Func<Task>, Task> check)
    {
        await check("selected refresh interval drives schedule and failure still backs off", async () => {
            using var fixture=new Fixture(); using var panel=new PanelController(fixture.Client);
            var interval=typeof(PanelController).GetProperty("RefreshInterval");
            if (interval==null) throw new Exception("Selectable refresh interval missing");
            foreach (int seconds in new[] {15,60,120,300,600}) {
                interval.SetValue(panel,TimeSpan.FromSeconds(seconds));
                if (panel.NextRefreshDelay!=TimeSpan.FromSeconds(seconds)) throw new Exception("Selected interval ignored");
            }
            interval.SetValue(panel,TimeSpan.FromSeconds(15)); await panel.RefreshAsync();
            if (!panel.Status.Contains("15 秒")) throw new Exception("Success status must show actual interval");
            fixture.Set("fail"); await panel.RefreshAsync();
            if (panel.NextRefreshDelay<TimeSpan.FromMinutes(10)) throw new Exception("Rapid mode must back off on failure");
        });
        await check("successful refresh records account and quota", async () => {
            using var fixture = new Fixture();
            using var panel = new PanelController(fixture.Client);
            await panel.RefreshAsync();
            if (panel.Snapshot?.Buckets[0].Windows[0].RemainingPercent != 75) throw new Exception("Quota missing");
            if (!panel.AccountLabel.Contains("person@example.test")) throw new Exception("Account missing");
            if (panel.IsStale || panel.IsBusy) throw new Exception("Wrong success state");
        });
        await check("network failure retains snapshot and marks stale with backoff", async () => {
            using var fixture = new Fixture();
            using var panel = new PanelController(fixture.Client);
            await panel.RefreshAsync(); var previous = panel.Snapshot;
            fixture.Set("fail"); await panel.RefreshAsync();
            if (previous is null || panel.Snapshot != previous || !panel.IsStale) throw new Exception("Previous data lost or stale state missing");
            if (panel.NextRefreshDelay < TimeSpan.FromMinutes(5)) throw new Exception("Aggressive retry");
        });
        await check("account change clears old snapshot if new account query fails", async () => {
            using var fixture = new Fixture();
            using var panel = new PanelController(fixture.Client);
            await panel.RefreshAsync(); fixture.Set("second"); fixture.Set("fail"); await panel.RefreshAsync();
            if (panel.Snapshot != null) throw new Exception("Old account data leaked into new account");
        });
        await check("overlapping refreshes coalesce", async () => {
            using var fixture = new Fixture();
            using var panel = new PanelController(fixture.Client);
            var a = panel.RefreshAsync(); var b = panel.RefreshAsync();
            if (!ReferenceEquals(a, b)) throw new Exception("Concurrent refreshes not coalesced");
            await Task.WhenAll(a,b);
        });
        await check("tray refresh keeps pending browser login alive", async () => {
            using var fixture = new Fixture(); fixture.Set("logged-out");
            using var panel = new PanelController(fixture.Client);
            await panel.LoginAsync();
            int pid = int.Parse(File.ReadAllText(Path.Combine(fixture.DirectoryPath,"login-pid")));
            using var child = Process.GetProcessById(pid);
            await panel.RefreshAsync();
            if (child.HasExited) throw new Exception("Refresh killed login callback process");
            fixture.Set("login-complete");
            var deadline = DateTime.UtcNow.AddSeconds(6);
            while (panel.Snapshot == null && DateTime.UtcNow < deadline) await Task.Delay(50);
            if (panel.Snapshot == null) throw new Exception("Completed login did not refresh");
        });
        await check("account change during query triggers a fresh read", async () => {
            using var fixture = new Fixture(); fixture.Set("account-change");
            using var panel = new PanelController(fixture.Client);
            await panel.RefreshAsync();
            int count = int.Parse(File.ReadAllText(Path.Combine(fixture.DirectoryPath,"usage-count")));
            if (count < 2 || !panel.AccountLabel.Contains("second@example.test")) throw new Exception("Account-change notification was lost");
        });
        await check("initial account notification can be verified without endless retries", async () => {
            using var fixture = new Fixture(); fixture.Set("startup-update");
            using var panel = new PanelController(fixture.Client);
            await panel.RefreshAsync();
            if (panel.Snapshot == null || panel.IsStale) throw new Exception("Initial account notification invalidated all queries");
        });
        await check("temporary missing reset details receive one bounded retry", async () => {
            using var fixture = new Fixture(); fixture.Set("retry-details");
            using var panel = new PanelController(fixture.Client);
            await panel.RefreshAsync();
            if (panel.Snapshot?.ResetCredits.Cards.Count != 1) throw new Exception("Available details were not retried");
        });
    }
}
