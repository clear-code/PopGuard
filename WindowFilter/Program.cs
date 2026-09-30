namespace WindowFilter;

internal static class Program
{
    private static void Main()
    {
        var guard = new WindowFilter(RulesStore.Load());

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

            // トップレベル列挙は別スレッド（Timer）で動く。メインスレッドは常駐のため待機し続ける。
            new ManualResetEvent(false).WaitOne();
        }
        catch (Exception ex)
        {
            Logger.Line("WindowFilter: fatal " + ex);
            guard.RestoreAll();
        }
    }
}
