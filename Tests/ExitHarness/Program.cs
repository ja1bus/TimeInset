using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TimeInset;
using TimeInset.Core;
using TimeInset.Core.Localization;
using TimeInset.Views;
using Forms = System.Windows.Forms;

// 本物の AppHost を使い、終了処理の開始後の要求（試験表 T-07・T-38）、確認中の表示の入力と Z 順（T-14・T-37・T-51）、
// 確認の位置と基準点（T-13）を確かめる。1プロセス1場面。合成キー・マウスは使わず、メッセージは自プロセスのウィンドウにだけ送る。
// 使い方: TimeInset.ExitHarness <場面> <設定フォルダー> [--settings]  終了コード 0=合格 1=不合格 3=時間切れ
static class Program
{
    static readonly List<string> failures = new();
    static Action<ConfirmWindow>? onShown;
    static bool sessionEnding;
    static Application app = null!;
    static AppHost host = null!;
    static Timer? watchdog;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "OK   " : "FAIL ") + what);
        if (!ok) failures.Add(what);
    }

    [STAThread]
    static int Main(string[] args)
    {
        string scenario = args[0], folder = args[1];
        bool withSettings = args.Contains("--settings");
        shots = args.Contains("--shots"); shotFolder = folder;
        string file = Path.Combine(folder, "settings.json");
        watchdog = new Timer(_ => { Console.WriteLine("FAIL 時間切れ"); Console.Out.Flush(); Environment.Exit(3); }, null, 50000, Timeout.Infinite);

        // pack://application:,,,/Resources/icon.ico の解決先を TimeInset にする（既定は起動した exe で、公開の setter は初期化済みとして拒否する）。
        typeof(Application).GetField("_resourceAssembly", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, typeof(AppHost).Assembly);
        app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Exception? unhandled = null;
        app.DispatcherUnhandledException += (_, e) => { unhandled = e.Exception; e.Handled = true; };
        System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();
        // Placement is checked after the first frame, as in the real dialog.
        EventManager.RegisterClassHandler(typeof(ConfirmWindow), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            var w = (ConfirmWindow)s;
            w.ContentRendered += (_, _) => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            { var a = onShown; onShown = null; if (a != null) a(w); else Check(false, "想定外の確認ダイアログ"); });
        }));

        var store = new SettingsStore(file);
        var settings = store.Load() with { ConfirmReset = scenario is "session-reset-confirm" or "anchor" or "monitors" };
        if (scenario == "modal-input") settings = settings.WithItems(DisplayItems.None);
        app.Startup += (_, _) =>
        {
            host = new AppHost(store, settings);
            app.SessionEnding += (_, _) => { sessionEnding = true; host.ExitForSession(); }; // App.xaml.cs と同じ
            host.Initialize();
            if (withSettings) host.OpenSettings();
            host.Start();
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => Run(scenario));
        };
        app.Run();
        host.Dispose();

        Check(unhandled == null, "未処理の例外なし" + (unhandled == null ? "" : ": " + unhandled));
        if (scenario.StartsWith("session-") || scenario == "approve-exit") Check(!File.Exists(file), "設定を保存していない");
        Console.WriteLine(failures.Count == 0 ? "PASS" : $"FAILED {failures.Count}");
        return failures.Count == 0 ? 0 : 1;
    }

    static void Run(string scenario)
    {
        switch (scenario)
        {
            // 承認による終了: Exit が ExitForSession を呼び、Shutdown が待っている間の要求。
            case "approve-exit":
                onShown = w => Press(w.YesButton);
                host.Exit();
                Requests(confirms: 0);
                break;
            // 確認なしで OS の終了（WM_QUERYENDSESSION → SessionEnding）。リセットの確認は OFF。
            case "session-idle":
                QueryEndSession();
                Requests(confirms: 0);
                break;
            // 確認を開いている間に OS の終了。Shutdown が確認を閉じ、Exit/Reset は何もせずに戻る。
            case "session-exit-confirm":
            case "session-reset-confirm":
                onShown = w => { CheckCentered(w); QueryEndSession(); Requests(confirms: 1); };
                if (scenario == "session-exit-confirm") host.Exit(); else host.Reset();
                Check(host.Model.State == TimerState.Running, "確認が閉じた後も計測中のまま");
                break;
            case "modal-input": ModalInput(); break;
            case "modal-zorder": ModalZOrder(); break;
            case "anchor": Anchor(); break;
            case "monitors": Monitors(0); break;
            default: Check(false, "不明な場面 " + scenario); app.Shutdown(); break;
        }
    }

    // 終了開始後の要求は何もしない（§6、AC-2・AC-8）。
    static void Requests(int confirms)
    {
        Check(sessionEndingExpected == sessionEnding, "SessionEnding の到達 " + sessionEnding);
        int settings = app.Windows.OfType<SettingsWindow>().Count();
        host.Exit(); host.Reset(); host.OpenSettings(); host.ExitForSession(); host.Exit(); host.Reset();
        Check(app.Windows.OfType<ConfirmWindow>().Count() == confirms, $"確認ダイアログは {confirms} 個のまま");
        Check(app.Windows.OfType<SettingsWindow>().Count() == settings, $"設定画面は {settings} 個のまま");
        Check(host.Model.State == TimerState.Running, "計測中のまま（リセット・停止されない）");
    }
    static bool sessionEndingExpected;

    static void QueryEndSession()
    {
        sessionEndingExpected = true;
        var parking = typeof(Application).GetField("_parkingHwnd", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(app);
        var handle = parking?.GetType().GetProperty("Handle")?.GetValue(parking) as nint? ?? 0;
        Check(handle != 0, "WPF の非表示ウィンドウ");
        SendMessage(handle, 0x11, 0, unchecked((nint)0x80000000)); // WM_QUERYENDSESSION, ENDSESSION_LOGOFF
    }

    static void CheckCentered(ConfirmWindow w)
    {
        var (r, a) = w.Placement;
        var p = Positioning.Center(new WorkArea("", a.Left, a.Top, a.Width, a.Height, 1), r.Width, r.Height);
        Check(r.Left == (int)p.Left && r.Top == (int)p.Top, $"確認は作業領域の中央 {r} / {a}");
    }

    // R1: 全項目 OFF で確認を開き、トレイの［時計］・［表示位置をリセット］で表示を戻しても入力を受けない。
    static void ModalInput()
    {
        var overlay = Field<OverlayWindow>(host, "overlay");
        nint visualHandle = new WindowInteropHelper(overlay).Handle;
        Check(!overlay.IsVisible && !IsWindowVisible(visualHandle) && !IsWindowVisible(overlay.InputHandle), "開始時は表示なし（表示・入力面とも Win32 で不可視）");
        foreach (var revive in new (string Name, Action Act)[]
        {
            ("時計", () => host.Commit(host.Settings.WithItems(host.Settings.Items ^ DisplayItems.Clock))),
            ("表示位置をリセット", host.ResetPosition),
        })
        {
            onShown = w =>
            {
                revive.Act();
                Check(overlay.IsVisible && IsWindowVisible(visualHandle) && IsWindowVisible(overlay.InputHandle), $"［{revive.Name}］で表示が戻る（Win32 で可視）");
                Check(!IsWindowEnabled(overlay.InputHandle), "確認中は入力面が無効");
                SendMessage(overlay.InputHandle, 0x204, 0, 0); SendMessage(overlay.InputHandle, 0x205, 0, 0); // 右クリック
                Check(Field<Forms.ContextMenuStrip?>(host, "menu") == null, "確認中の右クリックでメニューが開かない");
                host.Pause(); Check(host.Model.State == TimerState.Paused, "確認中もトレイ相当の一時停止が効く");
                host.ToggleTimer(); Check(host.Model.State == TimerState.Running, "確認中もショートカット相当の再開が効く");
                Press(w.NoButton);
            };
            host.Exit();
            Check(IsWindowEnabled(overlay.InputHandle), "確認を閉じると入力面が有効に戻る");
            host.Commit(host.Settings.WithItems(DisplayItems.None));
            Check(!IsWindowVisible(visualHandle) && !IsWindowVisible(overlay.InputHandle), "全項目OFFで表示・入力面とも不可視");
        }
        host.ExitForSession();
    }

    // R2: 確認中に最前面の OFF→ON・表示項目・文字サイズ・位置リセットをしても、表示は確認の背後のまま。
    static void ModalZOrder()
    {
        var overlay = Field<OverlayWindow>(host, "overlay");
        nint visual = new WindowInteropHelper(overlay).Handle;
        onShown = w =>
        {
            nint dialog = new WindowInteropHelper(w).Handle;
            void Below(string when) => Check(IsBelow(dialog, visual) && IsBelow(dialog, overlay.InputHandle), $"{when}: 表示と入力面は確認の背後");
            Below("開いた直後");
            host.Commit(host.Settings with { Topmost = false }); Below("最前面OFF");
            host.Commit(host.Settings with { Topmost = true }); Below("最前面ON");
            host.Commit(host.Settings.WithItems(host.Settings.Items ^ DisplayItems.Timer)); Below("表示項目の切り替え");
            host.Commit(host.Settings with { FontSize = host.Settings.FontSize + 12 }); Below("文字サイズの変更");
            host.ResetPosition(); Below("表示位置をリセット");
            Press(w.NoButton);
        };
        host.Exit();
        const int topmost = 0x8;
        Check((GetWindowLongPtr(visual, -20) & topmost) != 0 && (GetWindowLongPtr(overlay.InputHandle, -20) & topmost) != 0, "確認後も表示は最前面の設定どおり");
        host.ExitForSession();
    }

    // R4: メニュー以外からの呼び出しは現在のカーソル位置、メニューの項目はそのメニューを開いた点。
    static void Anchor()
    {
        var show = typeof(AppHost).GetMethod("ShowMenu", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var menuPoint = new System.Drawing.Point(50, 50);
        System.Drawing.Point AnchorOf(ConfirmWindow w) => (System.Drawing.Point)typeof(ConfirmWindow).GetField("anchor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;

        show.Invoke(host, [menuPoint.X, menuPoint.Y, true]);
        Field<Forms.ContextMenuStrip>(host, "menu").Close();
        var cursor = Forms.Cursor.Position;
        onShown = w => { Check(AnchorOf(w) == cursor && AnchorOf(w) != menuPoint, $"直接の呼び出しはカーソル位置 {AnchorOf(w)}"); Press(w.NoButton); };
        host.Exit();
        cursor = Forms.Cursor.Position;
        onShown = w => { Check(AnchorOf(w) == cursor, $"直接のリセットもカーソル位置 {AnchorOf(w)}"); Press(w.NoButton); };
        host.Reset();

        foreach (var text in new[] { Strings.Exit, Strings.Reset })
        {
            show.Invoke(host, [menuPoint.X, menuPoint.Y, true]);
            var menu = Field<Forms.ContextMenuStrip>(host, "menu");
            var item = menu.Items.OfType<Forms.ToolStripMenuItem>().First(i => i.Text == text);
            onShown = w => { Check(AnchorOf(w) == menuPoint, $"［{text}］はメニューを開いた点 {AnchorOf(w)}"); Press(w.NoButton); };
            menu.Close();
            item.PerformClick();
        }
        Check(host.Model.State == TimerState.Running, "［いいえ］では終了・リセットしない");
        host.ExitForSession();
    }

    // T-44・T-46 の自動化部分（R3）: 各モニターについて、基準点をそのモニターに置いた確認と、表示をそのモニターへ
    // 動かして表示の右クリックのメニュー（ShowMenu の tray=false）から選んだ［終了］・［タイマーをリセット］の確認が、
    // そのモニターの作業領域の中央に、そのモニターの DPI で、内容が切れずに出ること。モニターを往復する順で行う。
    // モニターが1台なら1台分だけ行う。--shots なら確認の画面を設定フォルダーへ PNG で保存する。
    static bool shots;
    static void Monitors(int step)
    {
        var screens = Forms.Screen.AllScreens;
        var order = screens.Concat(screens.Reverse().Skip(1)).ToArray(); // 1→2→…→1 の往復
        if (step >= order.Length)
        {
            Check(host.Model.State == TimerState.Running, "［いいえ］では終了・リセットしない");
            host.ExitForSession(); return;
        }
        var screen = order[step];
        var a = screen.WorkingArea;
        Console.WriteLine($"-- {step}: {screen.DeviceName} work={a}");
        // 基準点を直接渡す（トレイのメニューの点に相当）。
        onShown = w => { CheckOn(w, screen, $"{step}-direct"); Press(w.NoButton); };
        host.Exit(new System.Drawing.Point(a.Left + a.Width / 2, a.Top + a.Height / 2));
        // 表示をそのモニターの左上寄りへ動かし、DPI の変更が処理されてからメニューを開く。
        var overlay = Field<OverlayWindow>(host, "overlay");
        nint visual = new WindowInteropHelper(overlay).Handle;
        SetWindowPos(visual, 0, a.Left + 60, a.Top + 60, 0, 0, 0x15);
        app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            GetWindowRect(visual, out var r);
            Check(Forms.Screen.FromRectangle(System.Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom)).DeviceName == screen.DeviceName, $"表示が {screen.DeviceName} にある {r.Left},{r.Top}");
            var show = typeof(AppHost).GetMethod("ShowMenu", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var text in new[] { Strings.Exit, Strings.Reset })
            {
                show.Invoke(host, [(r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2, false]);
                var menu = Field<Forms.ContextMenuStrip>(host, "menu");
                var item = menu.Items.OfType<Forms.ToolStripMenuItem>().First(i => i.Text == text);
                onShown = w => { CheckOn(w, screen, $"{step}-overlay-{(text == Strings.Exit ? "exit" : "reset")}"); Press(w.NoButton); };
                menu.Close();
                item.PerformClick();
            }
            app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => Monitors(step + 1));
        });
    }
    static void CheckOn(ConfirmWindow w, Forms.Screen screen, string name)
    {
        var (r, area) = w.Placement;
        nint hwnd = new WindowInteropHelper(w).Handle;
        uint dpi = GetDpiForWindow(hwnd);
        GetDpiForMonitor(MonitorFromWindow(hwnd, 2), 0, out uint mdpi, out _);
        var root = (FrameworkElement)w.Content;
        Console.WriteLine($"   {name}: window={r} dpi={dpi} monitorDpi={mdpi} content={root.ActualWidth:0.#}x{root.ActualHeight:0.#} desired={root.DesiredSize.Width:0.#}x{root.DesiredSize.Height:0.#}");
        Check(area == screen.WorkingArea, $"{name}: 基準のモニターが {screen.DeviceName}");
        CheckCentered(w);
        Check(area.Contains(r), $"{name}: 作業領域からはみ出さない");
        Check(dpi == mdpi, $"{name}: 確認の DPI {dpi} がモニターの DPI {mdpi} と一致");
        Check(root.ActualWidth + 0.5 >= root.DesiredSize.Width && root.ActualHeight + 0.5 >= root.DesiredSize.Height, $"{name}: 内容が切れない");
        double scale = dpi / 96.0;
        Check(Math.Abs(r.Width - Math.Round(w.ActualWidth * scale)) <= 2, $"{name}: 物理の幅 {r.Width} が DIP×倍率 {w.ActualWidth * scale:0.#} と一致");
        if (shots)
        {
            using var bmp = new System.Drawing.Bitmap(r.Width, r.Height);
            // The window fades in, so the screen is not copied; the window renders itself (PW_RENDERFULLCONTENT).
            using (var g = System.Drawing.Graphics.FromImage(bmp)) { nint dc = g.GetHdc(); PrintWindow(hwnd, dc, 2); g.ReleaseHdc(dc); }
            bmp.Save(Path.Combine(shotFolder, $"confirm-{name}.png"));
        }
    }
    static string shotFolder = "";
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] static extern bool PrintWindow(nint hwnd, nint dc, uint flags);
    [DllImport("user32.dll")] static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    struct RECT { public int Left, Top, Right, Bottom; }

    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(o)!;
    // Through the automation peer so Button.OnClick runs (IsCancel closes the dialog there).
    static void Press(System.Windows.Controls.Button b) =>
        ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(b)
            .GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
    static bool IsBelow(nint top, nint target)
    {
        for (nint h = GetWindow(top, 2); h != 0; h = GetWindow(h, 2)) if (h == target) return true; // GW_HWNDNEXT
        return false;
    }
    [DllImport("user32.dll")] static extern nint SendMessage(nint hwnd, int msg, nint wp, nint lp);
    [DllImport("user32.dll")] static extern bool IsWindowEnabled(nint hwnd);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] static extern nint GetWindow(nint hwnd, uint cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern nint GetWindowLongPtr(nint hwnd, int index);
}
