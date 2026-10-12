using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CodexUsage.Core;
using CodexUsage.Desktop;

internal static class FloatingUi
{
    private sealed class Connection : ICodexConnection
    {
        public event Action<string,JsonElement>? NotificationReceived { add {} remove {} }
        public Task StartAsync() => Task.CompletedTask;
        public bool Fail;
        public Task<JsonElement> CallAsync(string method, object? parameters = null)
        {
            if (Fail) throw new IOException("offline");
            var json = method == "account/read" ? """{"account":{"type":"chatgpt","email":"fixture@example.test","planType":"plus"}}""" : """
                {"rateLimitsByLimitId":{
                  "other":{"limitName":"Other","primary":{"usedPercent":99,"windowDurationMins":300}},
                  "codex":{"limitName":"Codex","primary":{"usedPercent":18,"windowDurationMins":300,"resetsAt":1791900000},"secondary":{"usedPercent":36,"windowDurationMins":10080,"resetsAt":1792300000}}},
                "rateLimitResetCredits":{"availableCount":2,"credits":[{"status":"available","title":"重置卡 1","expiresAt":1893456000},{"status":"available","title":"重置卡 2","expiresAt":1893542400}]}}
                """;
            return Task.FromResult(JsonDocument.Parse(json).RootElement.Clone());
        }
        public void Dispose() { }
    }
    internal static int Run()
    {
        var type = typeof(MainWindow).Assembly.GetType("CodexUsage.Desktop.FloatingBallWindow");
        if (type is null) { Console.WriteLine("FAIL floating UI: floating ball is not implemented"); return 1; }
        int result = 1;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var connection = new Connection();
        using var controller = new PanelController(() => connection);
        int opened = 0;
        var ball = (Window)Activator.CreateInstance(type, controller, (Action)(() => opened++), (Action)(() => {}))!;
        ball.Left = 100; ball.Top = 100;
        ball.Loaded += async (_,_) => {
            try
            {
                var five = (TextBlock)ball.FindName("FiveHourText");
                var week = (TextBlock)ball.FindName("WeekText");
                Check(five.Text.Contains("—") && week.Text.Contains("—"), "Missing usage must stay unknown");
                await controller.RefreshAsync();
                await ball.Dispatcher.InvokeAsync(() => {}, DispatcherPriority.ApplicationIdle);
                Check(five.Text == "82%" && week.Text == "64%", "Ball must select Codex 5h/week, not another bucket");
                var surface = (FrameworkElement)ball.FindName("BallSurface");
                var popup = (Popup)ball.FindName("UsagePopup");
                var card = (FrameworkElement)ball.FindName("DetailCard");
                surface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.MouseEnterEvent });
                Check(popup.IsOpen, "Hover should open details");
                var count = (TextBlock)ball.FindName("FloatingResetCount");
                Check(count.Text.Contains("2"), "Hover card must show reset count");
                Check(Descendants(card).OfType<TextBlock>().Any(t => t.Text.Contains("2030-01-01")), "Hover must show reset card expiry");
                surface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.MouseLeaveEvent });
                card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.MouseEnterEvent });
                await Task.Delay(500);
                Check(popup.IsOpen, "Crossing from ball to details should keep it open");
                ((Button)ball.FindName("OpenPanelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(opened == 1 && !popup.IsOpen, "Open panel should work and close hover card");
                surface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.MouseEnterEvent });
                card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.MouseLeaveEvent });
                await Task.Delay(500);
                Check(!popup.IsOpen, "Leaving details should collapse it");
                var size = ball.FindName("BallSizeSlider") as Slider;
                Check(size != null, "Ball must offer a size slider");
                size!.Value=64;
                Check(ball.Width==72 && ball.Height==72, "Size control must resize the ball");
                size.Value=88;
                var opacity = (Slider)ball.FindName("BallOpacitySlider"); opacity.Value=45;
                Check(Math.Abs(surface.Opacity-.45)<.001 && card.Opacity==1, "Opacity changes ball while details stay readable");
                opacity.Value=100;
                var topmost = ball.FindName("TopmostToggle") as CheckBox;
                Check(topmost!=null,"Settings should use a normal checkbox in a dedicated window"); topmost!.IsChecked=false;
                Check(!ball.Topmost, "Ball topmost must be switchable off");
                topmost.IsChecked=true; Check(ball.Topmost, "Ball topmost must be switchable on");
                var cadence=ball.FindName("RefreshIntervalCombo") as ComboBox;
                Check(cadence!=null,"Refresh frequency selection missing");
                cadence!.SelectedIndex=0;
                Check(controller.NextRefreshDelay==TimeSpan.FromSeconds(15),"Quick mode must use 15 second polling");
                cadence.SelectedIndex=1;
                Check(controller.NextRefreshDelay==TimeSpan.FromMinutes(1),"One minute mode must reschedule polling");
                cadence.SelectedIndex=3;
                await VerifyAutoSave(ball,type,size,opacity,topmost,cadence);
                ((Button)ball.FindName("HideBallButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!ball.IsVisible && !popup.IsOpen, "Hide button must hide ball and details");
                ball.Show();
                VerifySettings();
                connection.Fail = true;
                await controller.RefreshAsync();
                await ball.Dispatcher.InvokeAsync(() => {}, DispatcherPriority.ApplicationIdle);
                Check(five.Text == "82%" && controller.IsStale, "Failure must retain last percentages");
                Check(((TextBlock)ball.FindName("FloatingStatus")).Text.Contains("过时"), "Stale cached data must be identified");
                connection.Fail = false;
                await controller.RefreshAsync();
                await ball.Dispatcher.InvokeAsync(() => {}, DispatcherPriority.ApplicationIdle);
                surface.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0) { RoutedEvent = Mouse.MouseEnterEvent });
                await ball.Dispatcher.InvokeAsync(() => {}, DispatcherPriority.ApplicationIdle);
                Directory.CreateDirectory("artifacts");
                Save((FrameworkElement)ball.Content,"artifacts/floating-ball.png");
                Save(card,"artifacts/floating-details.png");
                ball.Hide(); Check(!popup.IsOpen, "Hiding the ball must hide its popup");
                ball.Show(); ball.Close();
                Check(!ball.IsVisible, "Closing the ball should hide it");
                ball.Show(); Check(ball.IsVisible, "Closed ball must remain restorable from tray");
                Console.WriteLine("PASS floating: unknown values, correct quotas, hover, transition, collapse, open panel, reset expiry, stale data, hide, screenshots");
                result = 0;
            }
            catch (Exception e) { Console.WriteLine("FAIL floating UI: " + e); }
            finally { type.GetProperty("AllowClose")?.SetValue(ball,true); ball.Close(); app.Shutdown(); }
        };
        app.Run(ball);
        return result;
    }
    private static void Check(bool ok,string message) { if (!ok) throw new Exception(message); }
    private static async Task VerifyAutoSave(Window ball,Type ballType,Slider size,Slider opacity,CheckBox topmost,ComboBox cadence)
    {
        var settingsType=typeof(MainWindow).Assembly.GetType("CodexUsage.Desktop.FloatingSettingsWindow");
        Check(settingsType!=null,"Dedicated settings window missing");
        var saverType=typeof(MainWindow).Assembly.GetType("CodexUsage.Desktop.SettingsSaver");
        Check(saverType!=null,"Shared automatic-save controller missing");
        var path=Path.Combine(Path.GetTempPath(),"codex-usage-ui-"+Guid.NewGuid()+".json");
        try
        {
            var settingsField=ballType.GetField("settings",BindingFlags.NonPublic|BindingFlags.Instance)!;
            var settings=(AppSettings)settingsField.GetValue(ball)!;
            using var saver=(IDisposable)Activator.CreateInstance(saverType!,new AppSettingsStore(path),settings)!;
            Action queue=()=>saverType!.GetMethod("ScheduleSave")!.Invoke(saver,null);
            Func<bool> save=()=>(bool)saverType!.GetMethod("SaveNow")!.Invoke(saver,null)!;
            ballType.GetEvent("PreferencesChanged")!.AddEventHandler(ball,queue);
            ballType.GetEvent("SaveRequested")!.AddEventHandler(ball,save);
            Action<bool> reported=ok=>ballType.GetMethod("ReportSettingsSaved")!.Invoke(ball,new object[]{ok});
            saverType!.GetEvent("Saved")!.AddEventHandler(saver,reported);
            try
            {
                ((Button)ball.FindName("ConfigureButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var settingsWindow=Application.Current.Windows.Cast<Window>().Single(w=>w.GetType()==settingsType);
                Check(settingsWindow.IsVisible,"Settings button should open independent window");
                size.Value=72; opacity.Value=70; topmost.IsChecked=false; cadence.SelectedIndex=1;
                await Task.Delay(700);
                var loaded=new AppSettingsStore(path).Load();
                Check(loaded.BallSize==72 && loaded.BallOpacity==.7 && !loaded.BallTopmost && loaded.RefreshIntervalSeconds==60,"User edits must save automatically without clicking a button");
                using (var restoredController=new PanelController(()=>new Connection()))
                {
                    var restoredBall=new FloatingBallWindow(restoredController,()=>{},()=>{},loaded);
                    try { Check(restoredBall.Width==80 && !restoredBall.Topmost && ((FrameworkElement)restoredBall.FindName("BallSurface")).Opacity==.7 && restoredController.NextRefreshDelay==TimeSpan.FromMinutes(1),"A new window must restore saved preferences after restart"); }
                    finally { restoredBall.AllowClose=true; restoredBall.Close(); }
                }
                Check(((TextBlock)settingsWindow.FindName("SaveStatusText")).Text.Contains("已保存"),"Successful automatic save needs visible feedback");
                size.Value=80;
                ((Button)settingsWindow.FindName("SaveAndCloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!settingsWindow.IsVisible && new AppSettingsStore(path).Load().BallSize==80,"Save and close must flush pending settings");
                ((Button)ball.FindName("ConfigureButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Directory.CreateDirectory("artifacts");
                await settingsWindow.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                settingsWindow.UpdateLayout(); Save(settingsWindow,"artifacts/floating-settings.png");
                var blockedPath=path+".blocked"; Directory.CreateDirectory(blockedPath);
                try
                {
                    using var blockedSaver=new SettingsSaver(new AppSettingsStore(blockedPath),settings);
                    Func<bool> failedSave=blockedSaver.SaveNow;
                    ballType.GetEvent("SaveRequested")!.RemoveEventHandler(ball,save);
                    ballType.GetEvent("SaveRequested")!.AddEventHandler(ball,failedSave);
                    try
                    {
                        ((Button)settingsWindow.FindName("SaveAndCloseButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Check(settingsWindow.IsVisible && ((TextBlock)settingsWindow.FindName("SaveStatusText")).Text.Contains("失败"),"Failed save must be visible and keep settings open");
                        var exitCheck=typeof(SettingsSaver).GetMethod("TrySaveForExit");
                        Check(exitCheck!=null,"Exit needs an explicit save result before windows close");
                        Check(!(bool)exitCheck!.Invoke(blockedSaver,null)!,"Exit must detect unsaved edits when saving fails");
                    }
                    finally { ballType.GetEvent("SaveRequested")!.RemoveEventHandler(ball,failedSave); ballType.GetEvent("SaveRequested")!.AddEventHandler(ball,save); }
                }
                finally { Directory.Delete(blockedPath); }
                ((Button)settingsWindow.FindName("CloseSettingsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!settingsWindow.IsVisible,"Close settings button must close only settings");
                size.Value=88; opacity.Value=100; topmost.IsChecked=true; cadence.SelectedIndex=3;
                save();
            }
            finally { ballType.GetEvent("PreferencesChanged")!.RemoveEventHandler(ball,queue); ballType.GetEvent("SaveRequested")!.RemoveEventHandler(ball,save); }
        }
        finally { File.Delete(path); }
    }
    private static void VerifySettings()
    {
        using var geometryController=new PanelController(()=>new Connection());
        var largeBall=new FloatingBallWindow(geometryController,()=>{},()=>{},new AppSettings { BallSize=144 });
        try { Check(largeBall.Left+largeBall.Width<=SystemParameters.WorkArea.Right,"Restored large ball must fit inside screen"); }
        finally { largeBall.AllowClose=true; largeBall.Close(); }
        var storeType=typeof(MainWindow).Assembly.GetType("CodexUsage.Desktop.AppSettingsStore");
        Check(storeType!=null,"Settings persistence must be implemented");
        var file=Path.Combine(Path.GetTempPath(),"codex-usage-settings-"+Guid.NewGuid()+".json");
        try
        {
            File.WriteAllText(file,"""{"codexPath":"C:\\tools\\codex.exe"}""");
            var store=Activator.CreateInstance(storeType!,file)!;
            var load=storeType!.GetMethod("Load")!; var save=storeType.GetMethod("Save")!;
            var settings=load.Invoke(store,null)!; var type=settings.GetType();
            Check((double)type.GetProperty("BallSize")!.GetValue(settings)! ==88,"Old settings should migrate with defaults");
            type.GetProperty("BallSize")!.SetValue(settings,64d);
            type.GetProperty("BallOpacity")!.SetValue(settings,.45d);
            type.GetProperty("BallTopmost")!.SetValue(settings,false);
            type.GetProperty("RefreshIntervalSeconds")!.SetValue(settings,60);
            save.Invoke(store,new[] { settings }); var restored=load.Invoke(store,null)!;
            Check((double)type.GetProperty("BallSize")!.GetValue(restored)! ==64 && (double)type.GetProperty("BallOpacity")!.GetValue(restored)! ==.45 && !(bool)type.GetProperty("BallTopmost")!.GetValue(restored)!,"Preferences must survive reload");
            Check((string)type.GetProperty("CodexPath")!.GetValue(restored)! =="C:\\tools\\codex.exe","Saving display settings must retain Codex path");
            Check((int)type.GetProperty("RefreshIntervalSeconds")!.GetValue(restored)! ==60,"Refresh choice must survive reload");
            File.WriteAllText(file,"""{"ballSize":-100,"ballOpacity":8}"""); restored=load.Invoke(store,null)!;
            Check((double)type.GetProperty("BallSize")!.GetValue(restored)! >=64 && (double)type.GetProperty("BallOpacity")!.GetValue(restored)! <=1,"Out-of-range settings must be bounded");
            File.WriteAllText(file,"broken-json"); restored=load.Invoke(store,null)!;
            Check((double)type.GetProperty("BallSize")!.GetValue(restored)! ==88,"Invalid settings should use defaults");
        }
        finally { File.Delete(file); }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i=0; i<VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child=VisualTreeHelper.GetChild(parent,i); yield return child; foreach (var nested in Descendants(child)) yield return nested; }
    }
    private static void Save(FrameworkElement visual,string path)
    {
        visual.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(visual);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth*dpi.DpiScaleX),(int)Math.Ceiling(visual.ActualHeight*dpi.DpiScaleY),96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path); encoder.Save(file);
    }
}
