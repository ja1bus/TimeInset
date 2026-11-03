using System.Runtime.InteropServices;
using System.Windows.Interop;
using TimeInset.Core;

namespace TimeInset.Services;
public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource source = new(new HwndSourceParameters("TimeInset.Hotkeys") { ParentWindow = new nint(-3) });
    private readonly HashSet<int> registered = [];
    private bool suspended, disposed;
    public event Action<int>? Pressed;
    public const int Count = 1;
    public string?[] Errors { get; private set; } = new string?[Count];
    public HotkeyService() => source.AddHook(Hook);
    private nint Hook(nint hwnd, int msg, nint wp, nint lp, ref bool handled)
    { if (msg == 0x312 && (int)wp is >= 1 and <= Count) { Pressed?.Invoke((int)wp - 1); handled = true; } return 0; }
    public void Clear() { foreach (int id in registered) UnregisterHotKey(source.Handle, id); registered.Clear(); }
    public void Suspend() { suspended = true; Clear(); }
    public void Resume(AppSettings settings) { suspended = false; Register(settings); }
    public void Register(AppSettings s)
    {
        if (disposed) return;
        if (suspended) { Clear(); return; }
        RegisterCore(s);
    }
    private void RegisterCore(AppSettings s)
    {
        Clear(); Errors = new string?[Count];
        for (int i = 0; i < Count; i++)
        {
            if (s.Keys[i] is not { } key) continue;
            if (!key.IsValid || !RegisterHotKey(source.Handle, i + 1, key.Modifiers | 0x4000, key.Key))
                Errors[i] = Failure(key, Marshal.GetLastWin32Error());
            else { registered.Add(i + 1); RegisteredKey = key; }
        }
    }
    // Probe only changed combinations while retaining all old bindings. Changed settings
    // are not committed until every new registration and the file save have succeeded.
    public void ValidateChanges(AppSettings old, AppSettings next)
    {
        var keys = next.Keys.Where(k => k != null).ToArray();
        if (keys.Any(k => !k!.IsValid) || keys.Distinct().Count() != keys.Length) throw new ArgumentException(Core.Localization.Strings.InvalidHotkey);
        var probes = new List<int>();
        try
        {
            for (int i = 0; i < Count; i++)
            {
                var key = next.Keys[i];
                if (key == null || old.Keys.Contains(key)) continue;
                int id = 100 + i;
                if (!RegisterHotKey(source.Handle, id, key.Modifiers | 0x4000, key.Key))
                    throw new ArgumentException(Failure(key, Marshal.GetLastWin32Error()));
                probes.Add(id);
            }
        }
        finally { foreach (int id in probes) UnregisterHotKey(source.Handle, id); }
    }
    public void ApplyChanges(AppSettings old, AppSettings next, Action save)
    {
        // Position, color and other edits keep the keys: re-registering would release them for a
        // moment, so only save. A failed registration is still retried on the next commit.
        if (!suspended && old.Keys.SequenceEqual(next.Keys) && Errors.All(e => e == null)
            && registered.Count == next.Keys.Count(k => k != null)) { save(); return; }
        ValidateChanges(old, next);
        RegisterCore(next);
        try
        {
            for (int i = 0; i < Count; i++)
                if (next.Keys[i] != old.Keys[i] && Errors[i] != null) throw new ArgumentException(Errors[i]);
            save();
        }
        catch { RegisterCore(old); throw; }
        finally { if (suspended) Clear(); }
    }
    // Same wording as AreaShot: an in-use key is the common case, other codes are shown as numbers.
    public static string Failure(Hotkey key, int code) => code == 1409
        ? string.Format(System.Globalization.CultureInfo.InvariantCulture, Core.Localization.Strings.HotkeyInUseFormat, key)
        : string.Format(System.Globalization.CultureInfo.InvariantCulture, Core.Localization.Strings.HotkeyErrorFormat, key, code);
    /// <summary>Tries the key once and releases it. Returns null when it can be registered, otherwise the reason.</summary>
    public string? Probe(Hotkey key)
    {
        if (disposed) return null;
        if (registered.Contains(1) && RegisteredKey == key) return null;
        if (!RegisterHotKey(source.Handle, 200, key.Modifiers | 0x4000, key.Key)) return Failure(key, Marshal.GetLastWin32Error());
        UnregisterHotKey(source.Handle, 200);
        return null;
    }
    private Hotkey? RegisteredKey { get; set; }
    public void Dispose() { if (disposed) return; Clear(); source.Dispose(); disposed = true; }
    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hwnd, int id);
}
