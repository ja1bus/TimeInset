using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TimeInset.Core;
using TimeInset.Core.Localization;
using TimeInset.Services;
using TimeInset.Views;
using Forms = System.Windows.Forms;

namespace TimeInset;
public sealed class AppHost : IDisposable
{
    private readonly SettingsStore store;
    private readonly OverlayWindow overlay;
    private readonly Forms.NotifyIcon tray;
    private readonly System.Drawing.Icon icon;
    private readonly DispatcherTimer tick;
    private SettingsWindow? settingsWindow;
    private bool disposed;
    public AppSettings Settings { get; private set; }
    public TimeModel Model { get; } = new(new SystemTimeSource());
    public HotkeyService Hotkeys { get; } = new();
    public string SettingsPath => store.Path;
    public string? Notice { get; private set; }
    public event Action? Changed;
    public AppHost(SettingsStore store, AppSettings settings)
    {
        this.store = store; Settings = settings;
        overlay = new(Model);
        overlay.StartRequested += Start;
        overlay.WorkAreaChanged += () => DisplayChanged(null, EventArgs.Empty);
        overlay.PositionChanged += () => Commit(overlay.CapturePosition(Settings), checkSize: false);
        overlay.MenuRequested += p => ShowMenu((int)p.X, (int)p.Y, false);
        using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/icon.ico"))!.Stream;
        icon = new System.Drawing.Icon(stream, 32, 32);
        tray = new Forms.NotifyIcon { Icon = icon, Visible = true, Text = "TimeInset" };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenSettings(); else if (e.Button == Forms.MouseButtons.Right) { var p = Forms.Cursor.Position; ShowMenu(p.X, p.Y, true); } };
        Hotkeys.Pressed += id => { if (id == 0) ToggleTimer(); };
        tick = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Update(), Application.Current.Dispatcher);
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }
    public void Initialize()
    {
        overlay.View.Configure(Settings);
        overlay.Show(); overlay.Apply(Settings, true);
        Hotkeys.Register(Settings);
        Notice = store.Warning;
        // Settings saved on a larger monitor may not fit the one restored to now.
        FitToScreen();
        Refresh();
        if (Notice != null || Hotkeys.Errors.Any(x => x != null)) OpenSettings();
    }
    private void DisplayChanged(object? sender, EventArgs e) => Application.Current.Dispatcher.BeginInvoke(() =>
    {
        if (disposed) return;
        FitToScreen();
    });
    // Shrinks the text until the overlay fits the work area of its monitor, then keeps it on screen.
    private void FitToScreen()
    {
        if (!overlay.Fits(Settings))
        {
            var next = Settings;
            while (next.FontSize > 12 && !overlay.Fits(next)) next = next with { FontSize = next.FontSize - 1 };
            if (Notice == null) Notice = Strings.SizeAdjusted;
            else if (!Notice.Contains(Strings.SizeAdjusted, StringComparison.Ordinal)) Notice += "\n" + Strings.SizeAdjusted;
            Commit(next, false);
        }
        overlay.Constrain();
    }
    public void Update()
    {
        var before = new Size(overlay.View.Width, overlay.View.Height);
        overlay.View.Tick();
        if (before != new Size(overlay.View.Width, overlay.View.Height)) overlay.Constrain();
        tray.Text = $"TimeInset · {StateText} · {TimeModel.Format(Model.Elapsed)}"[..Math.Min(63, $"TimeInset · {StateText} · {TimeModel.Format(Model.Elapsed)}".Length)];
        Changed?.Invoke();
    }
    public string StateText => Model.State switch { TimerState.Running => Strings.Running, TimerState.Paused => Strings.Paused, _ => Strings.Ready };
    public void Start() { Model.Start(); Update(); }
    public void Pause() { Model.Pause(); Update(); }
    public void ToggleTimer() { if (Model.State == TimerState.Running) Pause(); else Start(); }
    /// <param name="anchor">確認を出すモニターを決める点（メニューを開いた点）。省略時はカーソル位置。</param>
    public void Reset(System.Drawing.Point? anchor = null)
    {
        if (confirm.Exiting || disposed) return;
        // While a confirmation is open, a reset is never carried out, whatever the setting (spec §16.4).
        if (confirm.IsOpen) { confirmWindow?.BringToFront(); return; }
        if (Settings.ConfirmReset && !Confirm(Strings.ResetPrompt, warning: false, anchor)) return;
        if (confirm.Exiting || disposed) return;
        Model.Reset(); overlay.View.Configure(Effective); Update();
    }
    // One confirmation at a time, shown on the monitor where the menu was opened (spec §6, §16).
    private readonly ConfirmGate confirm = new();
    private ConfirmWindow? confirmWindow;
    private bool Confirm(string message, bool warning, System.Drawing.Point? anchor)
    {
        if (!confirm.TryOpen(() => confirmWindow?.BringToFront())) return false;
        // ShowDialog disables only the windows visible when it starts, so an overlay shown from the tray
        // during the confirmation would take input; the overlay is blocked and kept below it explicitly.
        overlay.BeginModal();
        try
        {
            var window = confirmWindow = new ConfirmWindow(message, warning, anchor ?? Forms.Cursor.Position);
            window.SourceInitialized += (_, _) => overlay.SetModalWindow(new System.Windows.Interop.WindowInteropHelper(window).Handle);
            return window.ShowDialog() == true;
        }
        finally { confirmWindow = null; confirm.Close(); overlay.EndModal(); }
    }
    public bool Commit(AppSettings candidate, bool checkSize = true)
    {
        try
        {
            if (checkSize && !overlay.Fits(candidate)) throw new ArgumentException(Strings.DoesNotFit);
            Hotkeys.ApplyChanges(Settings, candidate, () => store.Save(candidate));
            Settings = candidate;
            Refresh(); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { ErrorLog.Write(ex); MessageBox.Show($"{ex.Message}\n{store.Path}", Strings.Error, MessageBoxButton.OK, MessageBoxImage.Error); return false; }
    }
    // While the settings window is open it previews its unsaved edits on the real overlay.
    // The override receives the saved settings so tray/drag changes are kept.
    private Func<AppSettings, AppSettings>? preview;
    public void SetPreview(Func<AppSettings, AppSettings>? value) { preview = value; Refresh(); }
    private AppSettings Effective => preview?.Invoke(Settings) ?? Settings;
    public void Refresh()
    {
        var effective = Effective;
        overlay.Apply(effective);
        if (effective.Items != 0) { if (!overlay.IsVisible) overlay.Show(); }
        else overlay.Hide();
        Update();
    }
    public void ResetPosition()
    {
        var next = Settings.RestoreItems() with { Monitor = "", X = 16, Y = 16 };
        if (!Commit(next)) return;
        overlay.Show(); overlay.RestorePosition(next); Refresh();
    }
    public bool ApplyEdited(AppSettings baseline, AppSettings edited, bool specifiedMode, bool modeChanged)
    {
        var merged = AppSettings.Merge(baseline, edited, Settings);
        bool dateChanged = baseline.SpecifiedDate != edited.SpecifiedDate;
        if (!Commit(merged)) return false;
        if (modeChanged || dateChanged && specifiedMode)
        { if (specifiedMode) Model.UseSpecified(merged.SpecifiedDate); else Model.UseCurrent(); }
        Update();
        return true;
    }
    /// <summary>Web ページ・公開リポジトリを既定のブラウザーで開く。</summary>
    public void OpenUrl(string url, Window owner)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { ErrorLog.Write(ex); MessageBox.Show(owner, string.Format(CultureInfo.InvariantCulture, Strings.OpenUrlFailedFormat, url), "TimeInset", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    /// <summary>同梱の LICENSE・THIRD-PARTY-NOTICES.md を表示する（Web だけに頼らない）。</summary>
    public void ShowBundledText(string fileName, string title, Window owner)
    {
        var path = Path.Combine(AppContext.BaseDirectory, fileName);
        try { new TextWindow(title, File.ReadAllText(path)) { Owner = owner }.Show(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ErrorLog.Write(ex); MessageBox.Show(owner, string.Format(CultureInfo.InvariantCulture, Strings.BundledFileUnreadableFormat, path), "TimeInset", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    public void OpenSettings()
    {
        if (confirm.Exiting || disposed) return;
        if (confirm.IsOpen) { confirmWindow?.BringToFront(); return; }
        if (settingsWindow == null)
        {
            settingsWindow = new(this);
            // On exit Shutdown closes every window, the overlay possibly first; re-showing it then throws.
            settingsWindow.Closed += (_, _) => { settingsWindow = null; if (confirm.Exiting || disposed) return; Hotkeys.Resume(Settings); SetPreview(null); };
        }
        settingsWindow.Show();
        if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
        settingsWindow.Activate();
    }
    private Forms.ContextMenuStrip? menu;
    // The overlay menu holds only what is useful while looking at the overlay; the tray menu
    // adds the state line and position reset for recovering an off-screen or hidden overlay.
    private void ShowMenu(int x, int y, bool tray)
    {
        menu?.Dispose(); menu = new Forms.ContextMenuStrip();
        // Only this menu's items use its point; a later call from elsewhere uses the cursor (review R4).
        var point = new System.Drawing.Point(x, y);
        Forms.ToolStripMenuItem Add(string text, Action action, bool? check = null)
        {
            var item = new Forms.ToolStripMenuItem(text, null, (_, _) => action());
            if (check.HasValue) item.Checked = check.Value;
            menu.Items.Add(item);
            return item;
        }
        if (tray)
        {
            menu.Items.Add($"{(Model.IsSpecified ? Strings.SpecifiedTime : Strings.CurrentTime)} · {StateText}").Enabled = false;
            foreach (string error in Hotkeys.Errors.OfType<string>()) menu.Items.Add(Strings.Unavailable + ": " + error).Enabled = false;
        }
        foreach (var (kind, label) in new[] { (DisplayItems.Clock, Strings.Clock), (DisplayItems.Date, Strings.Date), (DisplayItems.Timer, Strings.Timer) })
            Add(label, () => Commit(Settings.WithItems(Settings.Items ^ kind)), Settings.Items.HasFlag(kind));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var timerItem = Add(Model.State == TimerState.Running ? Strings.Pause : Strings.Start, Model.State == TimerState.Running ? Pause : Start);
        // Show the shortcut on the right of the item, only while it is actually registered.
        if (Settings.ToggleTimer is { } key && Hotkeys.Errors[0] == null) timerItem.ShortcutKeyDisplayString = key.ToString();
        Add(Strings.Reset, () => Reset(point));
        menu.Items.Add(new Forms.ToolStripSeparator());
        Add(Strings.Topmost, () => Commit(Settings with { Topmost = !Settings.Topmost }), Settings.Topmost);
        if (tray) Add(Strings.ResetPosition, ResetPosition);
        Add(Strings.Settings, OpenSettings); Add(Strings.Exit, () => Exit(point));
        menu.Show(x, y);
        // The overlay never activates and a tray click leaves the taskbar in front. Unless the menu is
        // the foreground window, its item clicks are not delivered reliably: the menu just closes and the
        // next right-click is spent dismissing it (1.0.0: "Exit" needed several tries).
        SetForegroundWindow(menu.Handle);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    /// <param name="anchor">確認を出すモニターを決める点（メニューを開いた点）。省略時はカーソル位置。</param>
    public void Exit(System.Drawing.Point? anchor = null)
    {
        if (confirm.Exiting || disposed) return;
        if (confirm.IsOpen) { confirmWindow?.BringToFront(); return; }
        if (Model.State != TimerState.Ready && !Confirm(Strings.ExitPrompt, warning: true, anchor)) return;
        ExitForSession();
    }
    // Also called on Windows sign-out/shutdown. Only the first call shuts down; Application.Current
    // is null once shutdown has finished (1.0.1 threw NullReferenceException here).
    public void ExitForSession()
    {
        if (!confirm.BeginExit()) return;
        Application.Current?.Shutdown();
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        tick.Stop(); Hotkeys.Dispose(); menu?.Dispose(); tray.Visible = false; tray.Dispose(); icon.Dispose(); overlay.Close();
    }
}
