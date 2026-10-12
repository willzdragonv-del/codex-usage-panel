using System;
using System.IO;
using System.Windows.Threading;

namespace CodexUsage.Desktop;

public sealed class SettingsSaver : IDisposable
{
    private readonly AppSettingsStore store;
    private readonly AppSettings settings;
    private readonly DispatcherTimer delay=new() { Interval=TimeSpan.FromMilliseconds(350) };
    private bool pending;
    public event Action<bool>? Saved;
    public SettingsSaver(AppSettingsStore store,AppSettings settings)
    {
        this.store=store; this.settings=settings;
        delay.Tick+=(_,_)=>SaveNow();
    }
    public void ScheduleSave() { pending=true; delay.Stop(); delay.Start(); }
    public bool TrySaveForExit()=>!pending || SaveNow();
    public bool SaveNow()
    {
        delay.Stop();
        bool success;
        try { store.Save(settings); pending=false; success=true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { pending=true; success=false; }
        Saved?.Invoke(success); return success;
    }
    public void Dispose() { if (pending) SaveNow(); delay.Stop(); }
}
