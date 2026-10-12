using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsage.Core;
namespace CodexUsage.Desktop;
public sealed class MainWindow : Window
{
    private readonly PanelController controller;
    private readonly StackPanel body = new();
    private readonly TextBlock account = Theme.Label("正在连接账号",12,Theme.Muted);
    private readonly TextBlock status = Theme.Label("等待查询",12,Theme.Muted);
    private readonly TextBlock updated = Theme.Label("尚未获取数据 · 北京时间",11,Theme.Muted);
    private readonly TextBlock count = Theme.Label("—",32);
    private readonly TextBlock expiry = Theme.Label("—",19);
    private readonly TextBlock expiryHint = Theme.Label("过期时间未提供",11,Theme.Muted);
    private readonly Button refresh = Theme.Button("刷新用量",true), login = Theme.Button("登录账号"), choose = Theme.Button("选择 Codex"), pin = Theme.Button("置顶");
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly ScrollViewer scroll;
    public event Action? ChooseCodex;
    public bool AllowClose { get; set; }
    public MainWindow(PanelController controller)
    {
        this.controller = controller;
        NameScope.SetNameScope(this,new NameScope());
        Title = "Codex 用量面板"; Width = 460; Height = Math.Min(850,SystemParameters.WorkArea.Height-32); MinWidth = 410; MinHeight = 540;
        Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/CodexUsage;component/app.ico"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Theme.Background; Foreground = Theme.Text; FontFamily = new FontFamily("Microsoft YaHei UI");
        var root = new Grid { Margin = new Thickness(22,18,22,18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel();
        var titleRow = new DockPanel();
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        pin.Margin = new Thickness(0,0,6,0); tools.Children.Add(pin);
        var hide = Theme.Button("收起"); hide.Click += (_,_) => Hide(); tools.Children.Add(hide);
        DockPanel.SetDock(tools,Dock.Right); titleRow.Children.Add(tools);
        titleRow.Children.Add(Theme.Label("CODEX",12,Theme.Accent)); header.Children.Add(titleRow);
        var title = Theme.Label("用量面板",27); title.FontWeight = FontWeights.SemiBold; title.Margin = new Thickness(0,3,0,4); header.Children.Add(title);
        header.Children.Add(Theme.Label("额度与重置卡，一眼掌握",12,Theme.Muted));
        account.Margin = new Thickness(0,15,0,14); header.Children.Add(account);
        var overview = new Grid(); overview.ColumnDefinitions.Add(new ColumnDefinition()); overview.ColumnDefinitions.Add(new ColumnDefinition());
        var cardCount = new StackPanel(); cardCount.Children.Add(Theme.Label("可用重置卡",12,Theme.Muted));
        count.Margin = new Thickness(0,7,0,0); count.FontWeight = FontWeights.SemiBold; cardCount.Children.Add(count);
        var countBox = Theme.Box(cardCount); countBox.Margin = new Thickness(0,0,6,14); overview.Children.Add(countBox);
        var cardExpiry = new StackPanel(); cardExpiry.Children.Add(Theme.Label("最近到期",12,Theme.Muted));
        expiry.Margin = new Thickness(0,11,0,4); expiry.FontWeight = FontWeights.SemiBold; cardExpiry.Children.Add(expiry); cardExpiry.Children.Add(expiryHint);
        var expiryBox = Theme.Box(cardExpiry); expiryBox.Margin = new Thickness(6,0,0,14); Grid.SetColumn(expiryBox,1); overview.Children.Add(expiryBox);
        header.Children.Add(overview); root.Children.Add(header);
        scroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0,0,7,0) };
        Grid.SetRow(scroll,1); root.Children.Add(scroll);
        var footer = new StackPanel { Margin = new Thickness(0,10,0,0) };
        footer.Children.Add(new Border { Height=1, Background=Theme.Line, Margin=new Thickness(0,0,0,12) });
        footer.Children.Add(status); updated.Margin = new Thickness(0,5,0,12); footer.Children.Add(updated);
        var actions = new Grid(); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto }); actions.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
        refresh.Margin = new Thickness(0,0,7,0); login.Margin = new Thickness(0,0,7,0);
        actions.Children.Add(refresh); Grid.SetColumn(login,1); actions.Children.Add(login); Grid.SetColumn(choose,2); actions.Children.Add(choose);
        footer.Children.Add(actions); Grid.SetRow(footer,2); root.Children.Add(footer); Content = root;
        RegisterName("ResetCountText",count); RegisterName("PinButton",pin);
        refresh.Click += async (_,_) => await controller.RefreshAsync();
        login.Click += async (_,_) => {
            if (controller.IsLoginPending) { await controller.CancelLoginAsync(); return; }
            var url = await controller.LoginAsync();
            if (Uri.TryCreate(url,UriKind.Absolute,out var uri) && uri.Scheme == "https" && (uri.Host == "chatgpt.com" || uri.Host == "auth.openai.com" || uri.Host.EndsWith(".openai.com",StringComparison.OrdinalIgnoreCase)))
                try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute=true }); }
                catch (Win32Exception) { MessageBox.Show(this,"无法自动打开浏览器。请检查默认浏览器设置。","登录账号"); }
        };
        choose.Click += (_,_) => ChooseCodex?.Invoke();
        pin.Click += (_,_) => { Topmost = !Topmost; pin.Content = Topmost ? "已置顶" : "置顶"; };
        controller.Changed += OnChanged;
        clock.Tick += (_,_) => Render();
        Loaded += (_,_) => { clock.Start(); Render(); };
        Closing += OnClosing;
        Closed += (_,_) => { controller.Changed -= OnChanged; clock.Stop(); };
        Render();
    }
    private void OnChanged()
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() => {
            Render();
        });
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!AllowClose) { e.Cancel=true; Hide(); }
    }
    private void Render()
    {
        double offset = scroll?.VerticalOffset ?? 0;
        account.Text = controller.AccountLabel;
        status.Text = controller.Status;
        status.Foreground = controller.IsStale ? Theme.Warning : Theme.Accent;
        refresh.IsEnabled = choose.IsEnabled = !controller.IsBusy && !controller.IsLoginPending;
        login.IsEnabled = !controller.IsBusy;
        login.Content = controller.IsLoginPending ? "取消登录" : "登录账号";
        refresh.Content = controller.IsBusy ? "正在刷新…" : "刷新用量";
        var snapshot = controller.Snapshot;
        updated.Text = snapshot == null ? "尚未获取数据 · 北京时间" : "最近同步 " + snapshot.FetchedAt.ToOffset(TimeSpan.FromHours(8)).ToString("MM-dd HH:mm:ss") + " · 北京时间";
        var credits = snapshot?.ResetCredits;
        count.Text = credits?.AvailableCount is { } number ? $"{number} 张" : "—";
        count.Foreground = controller.IsStale ? Theme.Warning : Theme.Text;
        var nearest = credits?.Cards.FirstOrDefault(c => c.ExpiresAt.HasValue);
        string date = DisplayTime.At(nearest?.ExpiresAt);
        expiry.Text = date.Length >= 10 && date != "未提供" ? date[..10] : "—";
        expiryHint.Text = nearest == null ? "过期时间未提供" : DisplayTime.Remaining(nearest.ExpiresAt,DateTimeOffset.UtcNow) + "后";
        body.Children.Clear();
        Section("额度窗口", "剩余比例");
        if (snapshot == null || snapshot.Buckets.Count == 0)
            body.Children.Add(Theme.Box(Theme.Label(controller.NeedsLogin ? "登录后显示账号额度与重置卡" : "暂未获取额度窗口",13,Theme.Muted)));
        else foreach (var bucket in snapshot.Buckets)
        {
            var panel = new StackPanel(); var bucketName = Theme.Label(bucket.Name,12,Theme.Muted); bucketName.Margin = new Thickness(0,0,0,10); panel.Children.Add(bucketName);
            if (bucket.Windows.Count == 0) panel.Children.Add(Theme.Label("接口未提供额度窗口",12,Theme.Muted));
            foreach (var window in bucket.Windows)
            {
                var row = new DockPanel();
                var percentage = Theme.Label(window.RemainingPercent.HasValue ? $"{window.RemainingPercent.Value:0.#}%" : "未提供",19,window.RemainingPercent is <= 15 ? Theme.Warning : Theme.Text);
                percentage.FontWeight = FontWeights.SemiBold; percentage.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(percentage,Dock.Right); row.Children.Add(percentage);
                var label = Theme.Label(window.Label); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label); panel.Children.Add(row);
                panel.Children.Add(new ProgressBar { Minimum=0, Maximum=100, Value=window.RemainingPercent ?? 0, Height=5, Background=Theme.Line, Foreground=window.RemainingPercent is <= 15 ? Theme.Warning : Theme.Accent, BorderThickness=new Thickness(0), Margin=new Thickness(0,8,0,8) });
                string reset = "重置 " + DisplayTime.At(window.ResetsAt);
                if (window.ResetsAt.HasValue) reset += "  ·  " + DisplayTime.Remaining(window.ResetsAt,DateTimeOffset.UtcNow);
                var resetText = Theme.Label(reset,11,Theme.Muted); resetText.Margin = new Thickness(0,0,0,12); panel.Children.Add(resetText);
            }
            body.Children.Add(Theme.Box(panel));
        }
        Section("重置卡明细", "最早到期优先");
        var detail = new StackPanel();
        var note = Theme.Label(credits?.Note ?? "重置卡信息暂不可用",11,Theme.Muted); note.Margin = new Thickness(0,0,0,credits?.Cards.Count > 0 ? 12 : 0); detail.Children.Add(note);
        if (credits != null) foreach (var card in credits.Cards)
        {
            var row = new StackPanel { Margin = new Thickness(0,0,0,13) };
            row.Children.Add(Theme.Label(card.Name,13));
            var expires = Theme.Label(card.ExpiresAt.HasValue ? "过期 " + DisplayTime.At(card.ExpiresAt) : "过期时间未提供",11,Theme.Muted); expires.Margin = new Thickness(0,5,0,3); row.Children.Add(expires);
            if (card.ExpiresAt.HasValue)
            {
                bool expired = card.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                row.Children.Add(Theme.Label(expired ? "已到期，等待刷新" : "有效期剩余 " + DisplayTime.Remaining(card.ExpiresAt,DateTimeOffset.UtcNow),11,expired || card.ExpiresAt-DateTimeOffset.UtcNow.ToUnixTimeSeconds() < 86400*3 ? Theme.Warning : Theme.Accent));
            }
            detail.Children.Add(row);
        }
        body.Children.Add(Theme.Box(detail));
        scroll?.ScrollToVerticalOffset(offset);
    }
    private void Section(string label, string hint)
    {
        var row = new DockPanel { Margin = new Thickness(1,5,1,12) };
        var hintText = Theme.Label(hint,11,Theme.Muted); hintText.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(hintText,Dock.Right); row.Children.Add(hintText);
        var name = Theme.Label(label,14); name.FontWeight = FontWeights.SemiBold; row.Children.Add(name); body.Children.Add(row);
    }
}
