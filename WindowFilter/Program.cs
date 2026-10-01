using System.Windows.Forms;

namespace WindowFilter;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppConfig config = RulesStore.Load();
        var guard = new WindowFilter(config.Rules);

        // 裏へ送ったウィンドウを取り残さないよう、異常時・終了時に必ず復元する。
        AppDomain.CurrentDomain.UnhandledException += (_, _) =>
        {
            try { guard.RestoreAll(); } catch { /* 復元失敗は無視 */ }
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { guard.RestoreAll(); } catch { /* 復元失敗は無視 */ }
        };

        Logger.Line("WindowFilter: starting");

        try
        {
            var reporter = new WindowReporter();
            var poller = new TopLevelPoller(reporter, guard);
            poller.Start();
            Logger.Line("WindowFilter: started");

            // タスクトレイに常駐。抑止は既定オフで、メニューから期限付きに有効化する。
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
