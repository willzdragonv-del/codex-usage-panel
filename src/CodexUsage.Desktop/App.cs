using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using CodexUsage.Core;
namespace CodexUsage.Desktop;
public sealed class App : Application
{
    private Mutex? instance;
    private EventWaitHandle? activate;
    private RegisteredWaitHandle? activationWait;
    private TrayService? tray;
    private PanelController? controller;
    private readonly DispatcherTimer poll = new() { Interval=TimeSpan.FromMinutes(5) };
    private readonly AppSettingsStore settingsStore = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsagePanel","settings.json"));
    private SettingsSaver? settingsSaver;
    private AppSettings preferences = new();
    [STAThread]
    public static void Main()
    {
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Run();
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        instance = new Mutex(true,"Local\\CodexUsagePanel-v1",out bool first);
        if (!first)
        {
            try { using var existing = EventWaitHandle.OpenExisting("Local\\CodexUsagePanel-activate-v1"); existing.Set(); } catch (WaitHandleCannotBeOpenedException) { }
            Shutdown(); return;
        }
        preferences=settingsStore.Load();
        controller = new PanelController(() => {
            string path = preferences.CodexPath != null && File.Exists(preferences.CodexPath) ? preferences.CodexPath : CodexLocator.Find() ?? throw new InvalidOperationException("未找到 Codex，请点击「选择 Codex」指定 codex.exe");
            return new CodexClient(new ProcessStartInfo(path,"app-server --listen stdio://"));
        }) { RefreshInterval=TimeSpan.FromSeconds(preferences.RefreshIntervalSeconds) };
        var window = new MainWindow(controller); MainWindow=window;
        Action show = () => Dispatcher.BeginInvoke(() => { window.Show(); window.WindowState=WindowState.Normal; window.Activate(); });
        FloatingBallWindow? ball = null;
        Action exit = () => {
            if (settingsSaver?.TrySaveForExit()==false && MessageBox.Show("设置无法保存，退出后本次改动可能丢失。仍然退出吗？","保存失败",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
            poll.Stop(); if (ball!=null) { ball.AllowClose=true; ball.Close(); } window.AllowClose=true; window.Close(); Shutdown();
        };
        ball = new FloatingBallWindow(controller,show,exit,preferences);
        settingsSaver=new SettingsSaver(settingsStore,preferences);
        settingsSaver.Saved+=ball.ReportSettingsSaved;
        ball.PreferencesChanged+=settingsSaver.ScheduleSave;
        ball.SaveRequested+=settingsSaver.SaveNow;
        Action showBall = () => Dispatcher.BeginInvoke(() => ball.Show());
        tray = new TrayService(show,()=> Dispatcher.BeginInvoke(async () => await controller.RefreshAsync()),exit,showBall);
        poll.Tick += async (_,_) => { poll.Stop(); await controller.RefreshAsync(); };
        controller.Changed += () => Dispatcher.BeginInvoke(() => {
            var snapshot = controller.Snapshot;
            string quota = snapshot?.Buckets.FirstOrDefault()?.Windows.FirstOrDefault()?.RemainingPercent?.ToString("0.#") ?? "—";
            tray.Update($"Codex 剩余 {quota}% · 重置卡 {snapshot?.ResetCredits.AvailableCount?.ToString() ?? "—"} 张" + (controller.IsStale ? " · 数据过时" : ""));
            if (!controller.IsBusy) { poll.Stop(); poll.Interval=controller.NextRefreshDelay; poll.Start(); }
        });
        window.ChooseCodex += () => {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter="Codex 可执行文件 (codex.exe)|codex.exe|可执行文件 (*.exe)|*.exe", Title="选择 Codex 后台程序" };
            if (dialog.ShowDialog(window) != true) return;
            preferences.CodexPath = dialog.FileName; SaveSettings();
            controller.ResetConnection(); _ = controller.RefreshAsync();
        };
        activate = new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\CodexUsagePanel-activate-v1");
        activationWait = ThreadPool.RegisterWaitForSingleObject(activate,(_,_)=>showBall(),null,Timeout.Infinite,false);
        ball.Show();
        _ = controller.RefreshAsync();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        settingsSaver?.Dispose();
        poll.Stop(); activationWait?.Unregister(null); activate?.Dispose(); tray?.Dispose(); controller?.Dispose(); instance?.Dispose();
        base.OnExit(e);
    }
    private void SaveSettings()
    {
        if (settingsSaver?.SaveNow()==false) MessageBox.Show("设置本次已生效，但无法保存；重启后将恢复上次设置。","保存设置");
    }
}
