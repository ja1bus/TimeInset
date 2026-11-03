using System.Windows;
using TimeInset.Core;
using TimeInset.Services;

namespace TimeInset;
public partial class App : Application
{
    private SingleInstance? instance;
    private AppHost? host;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            instance = SingleInstance.TryAcquire();
            if (instance == null) { Shutdown(); return; }
            // The tray and overlay menus are WinForms ContextMenuStrips running in WPF's message loop.
            // Without this, WinForms' menu message handling is skipped and item clicks are sometimes lost.
            System.Windows.Forms.Integration.WindowsFormsHost.EnableWindowsFormsInterop();
            var store = new SettingsStore(DataPaths.Settings);
            var settings = store.Load();
            host = new AppHost(store, settings);
            instance.ListenForActivation(() => Dispatcher.BeginInvoke(() => host?.OpenSettings()));
            SessionEnding += (_, _) => host?.ExitForSession();
            DispatcherUnhandledException += (_, args) => { ErrorLog.Write(args.Exception); };
            host.Initialize();
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
            MessageBox.Show(ex.Message, "TimeInset", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e) { host?.Dispose(); instance?.Dispose(); base.OnExit(e); }
}
