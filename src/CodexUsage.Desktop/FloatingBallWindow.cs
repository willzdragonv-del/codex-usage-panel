using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsage.Core;

namespace CodexUsage.Desktop;

public sealed class FloatingBallWindow : Window
{
    private readonly PanelController controller;
    private readonly AppSettings settings;
    private readonly FloatingSettingsWindow settingsWindow;
    private readonly Popup popup;
    private readonly Border surface, card;
    private readonly TextBlock five = Theme.Label("—",15), week = Theme.Label("—",15);
    private readonly TextBlock count = Theme.Label("重置卡 — 张",13);
    private readonly TextBlock status = Theme.Label("等待查询",11,Theme.Muted);
    private readonly StackPanel details = new();
    private readonly Button refresh = Theme.Button("刷新",true);
    private readonly DispatcherTimer collapse = new() { Interval=TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer clock = new() { Interval=TimeSpan.FromMinutes(1) };
    private Point? dragStart;
    private Point dragOrigin;
    private bool dragging;
    public bool AllowClose { get; set; }
    public event Action? PreferencesChanged;
    public event Func<bool>? SaveRequested;
    public void ReportSettingsSaved(bool success)=>settingsWindow.ReportSaved(success);

    public FloatingBallWindow(PanelController controller, Action openPanel, Action exit) : this(controller,openPanel,exit,new AppSettings()) { }
    public FloatingBallWindow(PanelController controller, Action openPanel, Action exit, AppSettings settings)
    {
        this.controller=controller;
        this.settings=settings; settings.Normalize();
        controller.RefreshInterval=TimeSpan.FromSeconds(settings.RefreshIntervalSeconds);
        NameScope.SetNameScope(this,new NameScope());
        Title="Codex 悬浮球"; Width=96; Height=96; ResizeMode=ResizeMode.NoResize;
        WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
        ShowInTaskbar=false; ShowActivated=false; Topmost=settings.BallTopmost;
        FontFamily=new FontFamily("Microsoft YaHei UI"); Foreground=Theme.Text;
        var lines = new StackPanel { VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Center };
        var hint=Theme.Label("剩余",9,Theme.Muted); hint.HorizontalAlignment=HorizontalAlignment.Center; hint.Margin=new Thickness(0,0,0,2); lines.Children.Add(hint);
        lines.Children.Add(CompactRow("5h",five,Theme.Accent));
        lines.Children.Add(CompactRow("周",week,Theme.Brush("#9DBBFF")));
        surface=new Border { Width=88,Height=88,CornerRadius=new CornerRadius(44),Background=Theme.Background,BorderBrush=Theme.Accent,BorderThickness=new Thickness(2),Child=lines,Cursor=Cursors.SizeAll };
        Content=new Grid { Children={ new Viewbox { Child=surface,Margin=new Thickness(4) } } };
        Width=Height=settings.BallSize+8; surface.Opacity=settings.BallOpacity;
        Left=SystemParameters.WorkArea.Right-Width-20;
        Top=SystemParameters.WorkArea.Top+SystemParameters.WorkArea.Height*.6;
        System.Windows.Automation.AutomationProperties.SetName(surface,"Codex 剩余额度，按住拖动，双击打开面板");

        var content=new StackPanel();
        var heading=new DockPanel { Margin=new Thickness(0,0,0,12) };
        var brand=Theme.Label("CODEX",12,Theme.Accent); brand.FontWeight=FontWeights.SemiBold; heading.Children.Add(brand);
        var caption=Theme.Label("额度详情 · 北京时间",11,Theme.Muted); caption.HorizontalAlignment=HorizontalAlignment.Right; heading.Children.Add(caption); content.Children.Add(heading);
        content.Children.Add(new ScrollViewer { Content=details,MaxHeight=Math.Max(180,Math.Min(480,SystemParameters.WorkArea.Height-210)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });
        status.Margin=new Thickness(0,8,0,12); content.Children.Add(status);
        var actions=new Grid(); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        refresh.Margin=new Thickness(0,0,6,0); actions.Children.Add(refresh);
        var open=Theme.Button("打开完整面板"); Grid.SetColumn(open,1); actions.Children.Add(open); content.Children.Add(actions);
        var displayActions=new Grid { Margin=new Thickness(0,8,0,0) }; displayActions.ColumnDefinitions.Add(new ColumnDefinition()); displayActions.ColumnDefinitions.Add(new ColumnDefinition());
        var configure=Theme.Button("悬浮球设置"); configure.Margin=new Thickness(0,0,6,0); displayActions.Children.Add(configure);
        RegisterName("ConfigureButton",configure);
        var hide=Theme.Button("隐藏悬浮球"); Grid.SetColumn(hide,1); displayActions.Children.Add(hide); content.Children.Add(displayActions);
        var help=Theme.Label("按住拖动 · 双击打开面板 · 右键调整显示",10,Theme.Muted); help.Margin=new Thickness(0,10,0,0); content.Children.Add(help);
        card=Theme.Box(content,new Thickness(18)); card.Width=350; card.Margin=new Thickness(0);
        popup=new Popup { Child=card,PlacementTarget=surface,Placement=PlacementMode.Custom,AllowsTransparency=true,StaysOpen=true };
        popup.CustomPopupPlacementCallback=(size,target,offset)=>new[] {
            new CustomPopupPlacement(new Point(-size.Width-8,(target.Height-size.Height)/2),PopupPrimaryAxis.Vertical),
            new CustomPopupPlacement(new Point(target.Width+8,(target.Height-size.Height)/2),PopupPrimaryAxis.Vertical)
        };
        RegisterName("FiveHourText",five); RegisterName("WeekText",week); RegisterName("BallSurface",surface);
        RegisterName("UsagePopup",popup); RegisterName("DetailCard",card); RegisterName("FloatingResetCount",count);
        RegisterName("FloatingStatus",status); RegisterName("OpenPanelButton",open); RegisterName("HideBallButton",hide);
        collapse.Tick+=(_,_)=> { collapse.Stop(); popup.IsOpen=false; };
        surface.MouseEnter+=(_,_)=> { if (!dragging && dragStart is null) { collapse.Stop(); Render(); popup.IsOpen=true; } };
        surface.MouseLeave+=(_,_)=>ArmCollapse();
        card.MouseEnter+=(_,_)=>collapse.Stop(); card.MouseLeave+=(_,_)=>ArmCollapse();
        refresh.Click+=async (_,_)=>await controller.RefreshAsync();
        open.Click+=(_,_)=> { CloseDetails(); openPanel(); };
        hide.Click+=(_,_)=>Hide();
        surface.MouseLeftButtonDown+=(_,e)=> {
            if (e.ClickCount==2) { CloseDetails(); openPanel(); e.Handled=true; return; }
            dragStart=PointToScreen(e.GetPosition(this)); dragOrigin=new Point(Left,Top); surface.CaptureMouse(); e.Handled=true;
        };
        surface.MouseMove+=(_,e)=> {
            if (dragStart is not { } start || e.LeftButton!=MouseButtonState.Pressed) return;
            var current=PointToScreen(e.GetPosition(this)); var dpi=VisualTreeHelper.GetDpi(this);
            var delta=new Vector((current.X-start.X)/dpi.DpiScaleX,(current.Y-start.Y)/dpi.DpiScaleY);
            if (!dragging && Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance) return;
            dragging=true; CloseDetails(); Left=dragOrigin.X+delta.X; Top=dragOrigin.Y+delta.Y;
        };
        surface.MouseLeftButtonUp+=(_,e)=> { dragStart=null; dragging=false; surface.ReleaseMouseCapture(); KeepOnScreen(); e.Handled=true; };
        surface.LostMouseCapture+=(_,_)=> { dragStart=null; dragging=false; };
        var menu=new ContextMenu();
        var showItem=new MenuItem { Header="打开完整面板" }; showItem.Click+=(_,_)=>openPanel(); menu.Items.Add(showItem);
        menu.Items.Add(new Separator());
        var topmost=new MenuItem { Header="保持置顶",IsCheckable=true,IsChecked=settings.BallTopmost };
        settingsWindow=new FloatingSettingsWindow(settings,()=> {
            Width=Height=settings.BallSize+8; surface.Opacity=settings.BallOpacity; Topmost=settings.BallTopmost;
            topmost.IsChecked=settings.BallTopmost;
            controller.RefreshInterval=TimeSpan.FromSeconds(settings.RefreshIntervalSeconds);
            if (IsVisible) KeepOnScreen();
            PreferencesChanged?.Invoke();
        },()=>SaveRequested?.Invoke()??false);
        RegisterName("BallSizeSlider",settingsWindow.SizeSlider); RegisterName("BallOpacitySlider",settingsWindow.OpacitySlider);
        RegisterName("RefreshIntervalCombo",settingsWindow.IntervalCombo); RegisterName("TopmostToggle",settingsWindow.TopmostToggle);
        Action showSettings=()=> { CloseDetails(); settingsWindow.Show(); settingsWindow.Activate(); };
        var settingsItem=new MenuItem { Header="悬浮球设置…" }; settingsItem.Click+=(_,_)=>showSettings(); menu.Items.Add(settingsItem);
        topmost.Checked+=(_,_)=>settingsWindow.TopmostToggle.IsChecked=true;
        topmost.Unchecked+=(_,_)=>settingsWindow.TopmostToggle.IsChecked=false;
        menu.Items.Add(topmost); menu.Items.Add(new Separator());
        var hideItem=new MenuItem { Header="隐藏悬浮球（从托盘恢复）" }; hideItem.Click+=(_,_)=>Hide(); menu.Items.Add(hideItem);
        menu.Items.Add(new Separator()); var exitItem=new MenuItem { Header="退出" }; exitItem.Click+=(_,_)=>exit(); menu.Items.Add(exitItem);
        surface.ContextMenu=menu; menu.Opened+=(_,_)=>CloseDetails();
        configure.Click+=(_,_)=>showSettings();
        IsVisibleChanged+=(_,_)=> { if (!IsVisible) { CloseDetails(); settingsWindow.Hide(); } };
        LocationChanged+=(_,_)=> { if (popup.IsOpen) CloseDetails(); };
        controller.Changed+=OnChanged;
        clock.Tick+=(_,_)=> { if (popup.IsOpen) Render(); };
        Loaded+=(_,_)=>clock.Start();
        Closing+=(_,e)=> { if (!AllowClose) { e.Cancel=true; Hide(); } };
        Closed+=(_,_)=> { CloseDetails(); settingsWindow.AllowClose=true; settingsWindow.Close(); clock.Stop(); controller.Changed-=OnChanged; };
        Render();
    }

    private static Grid CompactRow(string label,TextBlock percentage,Brush color)
    {
        var row=new Grid { Width=64 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(19) }); row.ColumnDefinitions.Add(new ColumnDefinition());
        var name=Theme.Label(label,10,color); name.VerticalAlignment=VerticalAlignment.Center; row.Children.Add(name);
        percentage.FontWeight=FontWeights.SemiBold; percentage.HorizontalAlignment=HorizontalAlignment.Right;
        Grid.SetColumn(percentage,1); row.Children.Add(percentage); return row;
    }
    private void ArmCollapse() { collapse.Stop(); collapse.Start(); }
    private void CloseDetails() { collapse.Stop(); popup.IsOpen=false; }
    private void OnChanged()
    {
        if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(Render);
    }
    private void KeepOnScreen()
    {
        // Screen works in physical pixels; WPF window positions use logical units.
        var center=PointToScreen(new Point(Width/2,Height/2));
        var work=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)center.X,(int)center.Y)).WorkingArea;
        var source=PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not { } target) return;
        var topLeft=target.TransformFromDevice.Transform(new Point(work.Left,work.Top));
        var bottomRight=target.TransformFromDevice.Transform(new Point(work.Right,work.Bottom));
        Left=Math.Clamp(Left,topLeft.X,Math.Max(topLeft.X,bottomRight.X-Width));
        Top=Math.Clamp(Top,topLeft.Y,Math.Max(topLeft.Y,bottomRight.Y-Height));
    }
    private void Render()
    {
        var snapshot=controller.Snapshot;
        var bucket=snapshot?.Buckets.FirstOrDefault(b=>b.Id.Equals("codex",StringComparison.OrdinalIgnoreCase));
        // Only a single bucket is a safe fallback when the API uses another identifier.
        bucket ??= snapshot?.Buckets.Count==1 ? snapshot.Buckets[0] : null;
        UpdatePercentage(five,bucket?.Windows.FirstOrDefault(w=>w.WindowMins==300));
        UpdatePercentage(week,bucket?.Windows.FirstOrDefault(w=>w.WindowMins==10080));
        surface.BorderBrush=controller.IsStale ? Theme.Warning : Theme.Accent;
        refresh.IsEnabled=!controller.IsBusy && !controller.IsLoginPending;
        refresh.Content=controller.IsBusy ? "刷新中…" : "刷新";
        status.Text=(controller.IsStale ? "数据过时 · " : "")+controller.Status;
        if (snapshot!=null) status.Text+="\n最近同步 "+snapshot.FetchedAt.ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm:ss");
        status.Foreground=controller.IsStale ? Theme.Warning : Theme.Muted;
        details.Children.Clear();
        if (snapshot==null) details.Children.Add(Theme.Label(controller.NeedsLogin ? "请打开完整面板登录账号" : "正在获取额度…",13,Theme.Muted));
        else
        {
            foreach (var quota in snapshot.Buckets)
            {
                var title=Theme.Label(quota.Name+" · 剩余",12,Theme.Muted); title.Margin=new Thickness(0,0,0,8); details.Children.Add(title);
                foreach (var window in quota.Windows)
                {
                    var row=new DockPanel();
                    var percent=Theme.Label(Percent(window),18,window.RemainingPercent is <=15 ? Theme.Warning : Theme.Text);
                    percent.HorizontalAlignment=HorizontalAlignment.Right; percent.FontWeight=FontWeights.SemiBold;
                    DockPanel.SetDock(percent,Dock.Right); row.Children.Add(percent); row.Children.Add(Theme.Label(window.Label)); details.Children.Add(row);
                    details.Children.Add(new ProgressBar { Minimum=0,Maximum=100,Value=window.RemainingPercent??0,Height=4,Background=Theme.Line,Foreground=window.RemainingPercent is <=15 ? Theme.Warning : Theme.Accent,BorderThickness=new Thickness(0),Margin=new Thickness(0,6,0,7) });
                    var time=Theme.Label("重置 "+DisplayTime.At(window.ResetsAt)+"\n"+DisplayTime.Remaining(window.ResetsAt,DateTimeOffset.UtcNow),11,Theme.Muted);
                    time.Margin=new Thickness(0,0,0,14); details.Children.Add(time);
                }
            }
        }
        details.Children.Add(new Border { Height=1,Background=Theme.Line,Margin=new Thickness(0,0,0,12) });
        var credits=snapshot?.ResetCredits;
        count.Text="可用重置卡 "+(credits?.AvailableCount?.ToString()??"—")+" 张"; count.FontWeight=FontWeights.SemiBold; details.Children.Add(count);
        var note=Theme.Label(credits?.Note??"重置卡信息暂不可用",11,Theme.Muted); note.Margin=new Thickness(0,5,0,8); details.Children.Add(note);
        if (credits!=null) foreach (var reset in credits.Cards)
        {
            var label=Theme.Label(reset.Name+"\n过期 "+DisplayTime.At(reset.ExpiresAt),11,Theme.Muted); label.Margin=new Thickness(0,0,0,8); details.Children.Add(label);
        }
    }
    private static string Percent(QuotaWindow? window)=>window?.RemainingPercent is { } percent ? $"{percent:0.#}%" : "—";
    private void UpdatePercentage(TextBlock label,QuotaWindow? window)
    {
        label.Text=Percent(window); label.Foreground=controller.IsStale || window?.RemainingPercent is <=15 ? Theme.Warning : Theme.Text;
    }
}
