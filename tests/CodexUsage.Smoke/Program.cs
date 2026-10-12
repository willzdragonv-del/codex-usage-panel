using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using CodexUsage.Core;
using CodexUsage.Desktop;

internal static class Program
{
    private sealed class ObservedConnection(CodexClient inner) : ICodexConnection
    {
        public event Action<string,JsonElement>? NotificationReceived { add => inner.NotificationReceived+=value; remove => inner.NotificationReceived-=value; }
        public Task StartAsync() => inner.StartAsync();
        public async Task<JsonElement> CallAsync(string method,object? parameters=null)
        {
            var result=await inner.CallAsync(method,parameters).ConfigureAwait(false);
            if (method=="account/rateLimits/read" && result.TryGetProperty("rateLimitResetCredits",out var credits))
                Console.WriteLine("Live detail response: " + (credits.TryGetProperty("credits",out var cards) && cards.ValueKind==JsonValueKind.Array ? cards.GetArrayLength().ToString() : "not supplied"));
            return result;
        }
        public void Dispose() => inner.Dispose();
    }
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--floating")) return FloatingUi.Run();
        int result = 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var controller = new PanelController(() => new ObservedConnection(new CodexClient(new ProcessStartInfo(CodexLocator.Find() ?? throw new Exception("Codex not found"), "app-server --listen stdio://"))));
        var window = new MainWindow(controller);
        window.Loaded += async (_, _) => {
            try {
                await controller.RefreshAsync();
                if (controller.Snapshot is null) throw new Exception("Live UI query failed: " + controller.Status);
                // Changed is marshalled to the UI Dispatcher; allow the queued render to finish.
                await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var cardText = (TextBlock)window.FindName("ResetCountText");
                string expected = controller.Snapshot.ResetCredits.AvailableCount?.ToString() ?? "—";
                if (!cardText.Text.Contains(expected)) throw new Exception("Reset count not visible");
                var pin = (Button)window.FindName("PinButton");
                pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!window.Topmost) throw new Exception("Pin failed");
                var dir = args.FirstOrDefault() ?? "artifacts";
                Directory.CreateDirectory(dir);
                window.UpdateLayout();
                var dpi = VisualTreeHelper.GetDpi(window);
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth*dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight*dpi.DpiScaleY), 96*dpi.DpiScaleX, 96*dpi.DpiScaleY, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.Combine(dir,"panel.png"))) encoder.Save(file);
                var ball = new FloatingBallWindow(controller, () => {}, () => {});
                try
                {
                    ball.Show();
                    await ball.Dispatcher.InvokeAsync(() => {}, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    var bucket = controller.Snapshot.Buckets.FirstOrDefault(b => b.Id.Equals("codex",StringComparison.OrdinalIgnoreCase));
                    bucket ??= controller.Snapshot.Buckets.Count == 1 ? controller.Snapshot.Buckets[0] : null;
                    foreach (var (name,minutes) in new[] { ("FiveHourText",300),("WeekText",10080) })
                    {
                        var value = bucket?.Windows.FirstOrDefault(w=>w.WindowMins==minutes)?.RemainingPercent;
                        string text = value.HasValue ? $"{value:0.#}%" : "—";
                        if (((TextBlock)ball.FindName(name)).Text != text) throw new Exception("Live floating quota mismatch: "+name);
                    }
                    ((FrameworkElement)ball.FindName("BallSurface")).RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent=Mouse.MouseEnterEvent });
                    await ball.Dispatcher.InvokeAsync(() => {}, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    if (!((Popup)ball.FindName("UsagePopup")).IsOpen) throw new Exception("Live hover did not open");
                    Save((FrameworkElement)ball.Content,Path.Combine(dir,"floating-ball-live.png"));
                    Save((FrameworkElement)ball.FindName("DetailCard"),Path.Combine(dir,"floating-details-live.png"));
                }
                finally { ball.AllowClose=true; ball.Close(); }
                window.Close();
                if (window.IsVisible) throw new Exception("Close should hide to tray");
                window.Show();
                if (!window.IsVisible) throw new Exception("Reopen failed");
                Console.WriteLine("PASS live UI data, floating 5h/week, hover details, pin, close-hide, reopen, screenshots");
                result = 0;
            } catch (Exception e) { Console.WriteLine("FAIL UI: " + e); }
            finally { window.AllowClose = true; window.Close(); app.Shutdown(); }
        };
        app.Run(window);
        return result;
    }
    private static void Save(FrameworkElement visual,string path)
    {
        visual.UpdateLayout(); var dpi=VisualTreeHelper.GetDpi(visual);
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(visual.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path); encoder.Save(file);
    }
}
