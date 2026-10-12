using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexUsage.Desktop;

internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon tray;
    private readonly Icon icon;
    private readonly ContextMenuStrip menu;
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
    internal TrayService(Action show, Action refresh, Action exit, Action showBall)
    {
        using var bitmap = new Bitmap(32,32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var background = new SolidBrush(Color.FromArgb(25,34,46)); graphics.FillEllipse(background,1,1,30,30);
            using var pen = new Pen(Color.FromArgb(105,220,193),3); graphics.DrawArc(pen,5,5,22,22,35,290);
            using var dot = new SolidBrush(Color.FromArgb(105,220,193)); graphics.FillEllipse(dot,15,13,5,5);
        }
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); icon = (Icon)borrowed.Clone(); } finally { DestroyIcon(handle); }
        menu = new ContextMenuStrip();
        menu.Items.Add("打开用量面板",null,(_,_)=>show());
        menu.Items.Add("显示悬浮球",null,(_,_)=>showBall());
        menu.Items.Add("刷新用量",null,(_,_)=>refresh());
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add("退出",null,(_,_)=>exit());
        tray = new NotifyIcon { Icon=icon, Text="Codex 用量面板", ContextMenuStrip=menu, Visible=true };
        tray.DoubleClick += (_,_) => show();
    }
    internal void Update(string summary)
    {
        tray.Text = summary.Length > 63 ? summary[..63] : summary;
    }
    public void Dispose() { tray.Visible=false; tray.Dispose(); menu.Dispose(); icon.Dispose(); }
}
