using System.Windows.Forms;

namespace PopGuard;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppConfig config = RulesStore.Load();
        var engine = new GuardEngine(config.Rules);

        // Always restore pushed-back windows on failure/exit so none are left behind.
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            try { engine.RestoreAll(); } catch { /* ignore restore failure */ }
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { engine.RestoreAll(); } catch { /* ignore restore failure */ }
        };

        Logger.Line("PopGuard: starting");

        try
        {
            var reporter = new WindowReporter();
            var poller = new TopLevelPoller(reporter, engine);
            poller.Start();

            // Immediate detection of new windows via WinEvent hook (installed on this STA thread,
            // delivered once Application.Run pumps messages). The poller remains the safety net.
            var eventWatcher = new WindowEventWatcher(engine);
            eventWatcher.Start();

            // Focus-session sync (subscribe on the STA thread; auto-suppress via events).
            var focus = new FocusSessionWatcher(engine, config.AutoSuppressDuringFocus);
            focus.Start();

            // Microphone sync (poll; auto-suppress while the mic is in use, e.g. during a meeting).
            var mic = new MicrophoneWatcher(engine, config.AutoSuppressDuringMicrophone);
            mic.Start();

            Logger.Line("PopGuard: started");

            // Reside in the tray. Suppression is enabled manually (menu) and via focus-session sync.
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayContext(engine, config.Durations));

            mic.Stop();
            eventWatcher.Stop();
            poller.Stop();
            engine.RestoreAll();
        }
        catch (Exception ex)
        {
            Logger.Line("PopGuard: fatal " + ex);
            engine.RestoreAll();
        }
    }
}
