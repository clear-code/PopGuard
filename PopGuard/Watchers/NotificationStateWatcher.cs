namespace PopGuard;

/// <summary>
/// Diagnostic: polls the Windows notification state (SHQueryUserNotificationState) and logs it on
/// change. Used to confirm whether manual "Do Not Disturb" maps to QUNS_QUIET_TIME on this machine
/// before wiring it into auto-suppression.
/// </summary>
internal sealed class NotificationStateWatcher
{
    private int _last = int.MinValue;

    public void Poll()
    {
        int hr = NativeMethods.SHQueryUserNotificationState(
            out NativeMethods.QUERY_USER_NOTIFICATION_STATE state);

        if (hr != 0) // non S_OK: query failed
        {
            return;
        }

        int value = (int)state;
        if (value != _last)
        {
            _last = value;
            Logger.Line($"notification-state: {state} ({value})");
        }
    }
}
