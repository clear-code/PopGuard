using System.Windows.Forms;

namespace WindowFilter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppConfig config = RulesStore.Load();
        var guard = new WindowFilter(config.Rules);

        // Always restore pushed-back windows on failure/exit so none are left behind.
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            try { guard.RestoreAll(); } catch { /* ignore restore failure */ }
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { guard.RestoreAll(); } catch { /* ignore restore failure */ }
        };

        Logger.Line("WindowFilter: starting");

        try
        {
            var reporter = new WindowReporter();
            var poller = new TopLevelPoller(reporter, guard);
            poller.Start();

            // Focus-session sync (subscribe on the STA thread; auto-suppress via events).
            var focus = new FocusSessionWatcher(guard, config.AutoSuppressDuringFocus);
            focus.Start();

            Logger.Line("WindowFilter: started");

            // Reside in the tray. Suppression is enabled manually (menu) and via focus-session sync.
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext(guard, config.Durations));

            poller.Stop();
            guard.RestoreAll();
        }
        catch (Exception ex)
        {
            Logger.Line("WindowFilter: fatal " + ex);
            guard.RestoreAll();
        }
    }
}
