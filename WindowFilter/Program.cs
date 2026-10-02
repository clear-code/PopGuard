using System.Windows.Forms;

namespace WindowFilter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppConfig config = RulesStore.Load();
        var windowFilter = new WindowFilter(config.Rules);

        // Always restore pushed-back windows on failure/exit so none are left behind.
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            try { windowFilter.RestoreAll(); } catch { /* ignore restore failure */ }
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { windowFilter.RestoreAll(); } catch { /* ignore restore failure */ }
        };

        Logger.Line("WindowFilter: starting");

        try
        {
            var reporter = new WindowReporter();
            var poller = new TopLevelPoller(reporter, windowFilter);
            poller.Start();

            // Immediate detection of new windows via WinEvent hook (installed on this STA thread,
            // delivered once Application.Run pumps messages). The poller remains the safety net.
            var eventWatcher = new WindowEventWatcher(windowFilter);
            eventWatcher.Start();

            // Focus-session sync (subscribe on the STA thread; auto-suppress via events).
            var focus = new FocusSessionWatcher(windowFilter, config.AutoSuppressDuringFocus);
            focus.Start();

            Logger.Line("WindowFilter: started");

            // Reside in the tray. Suppression is enabled manually (menu) and via focus-session sync.
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext(windowFilter, config.Durations));

            eventWatcher.Stop();
            poller.Stop();
            windowFilter.RestoreAll();
        }
        catch (Exception ex)
        {
            Logger.Line("WindowFilter: fatal " + ex);
            windowFilter.RestoreAll();
        }
    }
}
