using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CodexUsage.Desktop;

public sealed class FloatingSettingsWindow : Window
{
    private readonly TextBlock status=Theme.Label("已加载保存的设置",12,Theme.Muted);
    private readonly Func<bool> save;
    public Slider SizeSlider { get; }
    public Slider OpacitySlider { get; }
    public ComboBox IntervalCombo { get; }
    public CheckBox TopmostToggle { get; }
    public bool AllowClose { get; set; }
    public FloatingSettingsWindow(AppSettings settings,Action changed,Func<bool> save)
    {
        this.save=save;
        NameScope.SetNameScope(this,new NameScope());
        Title="悬浮球设置"; Width=400; SizeToContent=SizeToContent.Height; ResizeMode=ResizeMode.NoResize;
        WindowStartupLocation=WindowStartupLocation.CenterScreen; ShowInTaskbar=false;
        Background=Theme.Background; Foreground=Theme.Text; FontFamily=new FontFamily("Microsoft YaHei UI");
        var root=new StackPanel { Margin=new Thickness(22) };
        var title=Theme.Label("悬浮球设置",23); title.FontWeight=FontWeights.SemiBold; root.Children.Add(title);
        var intro=Theme.Label("调整立即生效，并自动保存到本机",12,Theme.Muted); intro.Margin=new Thickness(0,6,0,20); root.Children.Add(intro);
        var sizeLabel=Theme.Label($"大小  {settings.BallSize:0} px"); root.Children.Add(sizeLabel);
        SizeSlider=new Slider { Minimum=64,Maximum=144,Value=settings.BallSize,TickFrequency=8,IsSnapToTickEnabled=true,Margin=new Thickness(0,10,0,18) }; root.Children.Add(SizeSlider);
        var opacityLabel=Theme.Label($"不透明度  {settings.BallOpacity*100:0}%"); root.Children.Add(opacityLabel);
        OpacitySlider=new Slider { Minimum=35,Maximum=100,Value=settings.BallOpacity*100,TickFrequency=5,IsSnapToTickEnabled=true,Margin=new Thickness(0,10,0,5) }; root.Children.Add(OpacitySlider);
        var opacityHint=Theme.Label("数值越小越透明，详情卡片始终保持清晰",11,Theme.Muted); opacityHint.Margin=new Thickness(0,0,0,18); root.Children.Add(opacityHint);
        root.Children.Add(Theme.Label("自动刷新"));
        IntervalCombo=new ComboBox { SelectedValuePath="Tag",MinHeight=32,Margin=new Thickness(0,9,0,18),Foreground=Brushes.Black };
        foreach (var option in new[] { (15,"快速（15 秒轮询）"),(60,"1 分钟"),(120,"2 分钟"),(300,"5 分钟"),(600,"10 分钟") })
            IntervalCombo.Items.Add(new ComboBoxItem { Content=option.Item2,Tag=option.Item1 });
        IntervalCombo.SelectedValue=settings.RefreshIntervalSeconds; root.Children.Add(IntervalCombo);
        TopmostToggle=new CheckBox { Content="保持悬浮球置顶",IsChecked=settings.BallTopmost,Foreground=Theme.Text,Margin=new Thickness(0,0,0,20) }; root.Children.Add(TopmostToggle);
        root.Children.Add(new Border { Height=1,Background=Theme.Line,Margin=new Thickness(0,0,0,14) });
        status.Margin=new Thickness(0,0,0,15); root.Children.Add(status);
        var actions=new Grid(); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition());
        var saveClose=Theme.Button("保存并关闭",true); saveClose.Margin=new Thickness(0,0,8,0); actions.Children.Add(saveClose);
        var close=Theme.Button("关闭设置"); Grid.SetColumn(close,1); actions.Children.Add(close); root.Children.Add(actions);
        Content=root;
        RegisterName("SaveStatusText",status); RegisterName("SaveAndCloseButton",saveClose); RegisterName("CloseSettingsButton",close);
        RegisterName("BallSizeSlider",SizeSlider); RegisterName("BallOpacitySlider",OpacitySlider); RegisterName("RefreshIntervalCombo",IntervalCombo); RegisterName("TopmostToggle",TopmostToggle);
        void Changed() { status.Text="正在自动保存…"; status.Foreground=Theme.Muted; changed(); }
        SizeSlider.ValueChanged+=(_,e)=> { settings.BallSize=e.NewValue; sizeLabel.Text=$"大小  {e.NewValue:0} px"; Changed(); };
        OpacitySlider.ValueChanged+=(_,e)=> { settings.BallOpacity=e.NewValue/100; opacityLabel.Text=$"不透明度  {e.NewValue:0}%"; Changed(); };
        IntervalCombo.SelectionChanged+=(_,_)=> { if (IntervalCombo.SelectedValue is int seconds) { settings.RefreshIntervalSeconds=seconds; Changed(); } };
        TopmostToggle.Checked+=(_,_)=> { settings.BallTopmost=true; Changed(); };
        TopmostToggle.Unchecked+=(_,_)=> { settings.BallTopmost=false; Changed(); };
        saveClose.Click+=(_,_)=> { bool ok=save(); ReportSaved(ok); if (ok) Hide(); };
        close.Click+=(_,_)=>Close();
        Closing+=(_,e)=> { if (!AllowClose) { e.Cancel=true; ReportSaved(save()); Hide(); } };
    }
    public void ReportSaved(bool success)
    {
        status.Text=success ? "已保存 · "+DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(8)).ToString("HH:mm:ss") : "保存失败，请检查设置目录权限后重试";
        status.Foreground=success ? Theme.Accent : Theme.Warning;
    }
}
