using Microsoft.Win32;

namespace PopGuard;

/// <summary>
/// Detects whether the microphone is currently in use and relays changes to <see cref="GuardEngine"/>.
/// While enabled, suppression is turned on while the mic is in use (e.g. during a call/meeting) and off
/// afterwards (manual actions are respected).
///
/// Detection reads the per-app "capability access" records that Windows keeps under the ConsentStore.
/// Each app that has used the microphone has LastUsedTimeStart / LastUsedTimeStop (FILETIME) values;
/// while an app is actively capturing, LastUsedTimeStop == 0. We poll these records on a timer — there is
/// no public change notification, and the registry/WASAPI are read-only observations (no settings are changed).
/// </summary>
internal sealed class MicrophoneWatcher
{
    // ConsentStore path, relative to the hive root. Desktop (Win32) apps live under the "NonPackaged" subkey;
    // packaged (Store/UWP) apps are direct subkeys.
    private const string ConsentStorePath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    private const int PollIntervalMs = 2000;

    private readonly GuardEngine _guardEngine;
    private readonly bool _enabled;

    private System.Threading.Timer? _timer;
    private bool _lastInUse;

    public MicrophoneWatcher(GuardEngine guardEngine, bool enabled)
    {
        _guardEngine = guardEngine;
        _enabled = enabled;
    }

    public void Start()
    {
        if (!_enabled)
        {
            Logger.Line("mic-watch: disabled by config (autoSuppressDuringMicrophone=false)");
            return;
        }

        // Reflect the current state once at startup, then poll for changes.
        _lastInUse = IsMicrophoneInUse();
        Logger.Line($"mic-watch: started, inUse={_lastInUse}, intervalMs={PollIntervalMs}");
        if (_lastInUse)
        {
            _guardEngine.OnMicrophoneChanged(true);
        }

        _timer = new System.Threading.Timer(_ => Poll(), null, PollIntervalMs, PollIntervalMs);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Poll()
    {
        try
        {
            bool inUse = IsMicrophoneInUse();
            if (inUse == _lastInUse)
            {
                return;
            }

            _lastInUse = inUse;
            Logger.Line($"mic-watch: inUse={inUse}");
            _guardEngine.OnMicrophoneChanged(inUse);
        }
        catch (Exception ex)
        {
            // Never let a transient registry read failure take down the timer thread.
            Logger.Line("mic-watch: poll failed: " + ex.Message);
        }
    }

    /// <summary>True if any app currently holds the microphone (LastUsedTimeStop == 0).</summary>
    private static bool IsMicrophoneInUse()
    {
        // Per-user records (HKCU) plus machine-wide records (HKLM) for apps that run across users/services.
        return StoreHasActiveUse(Registry.CurrentUser) || StoreHasActiveUse(Registry.LocalMachine);
    }

    private static bool StoreHasActiveUse(RegistryKey hive)
    {
        try
        {
            using RegistryKey? store = hive.OpenSubKey(ConsentStorePath);
            if (store is null)
            {
                return false;
            }

            if (AnyAppInUse(store))
            {
                return true; // packaged apps (direct subkeys)
            }

            using RegistryKey? nonPackaged = store.OpenSubKey("NonPackaged");
            return nonPackaged is not null && AnyAppInUse(nonPackaged); // desktop apps
        }
        catch
        {
            return false;
        }
    }

    private static bool AnyAppInUse(RegistryKey parent)
    {
        foreach (string childName in parent.GetSubKeyNames())
        {
            // "NonPackaged" is a container, not an app; it is handled separately.
            if (childName.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using RegistryKey? app = parent.OpenSubKey(childName);
                if (app is null)
                {
                    continue;
                }

                // In use == the app started capturing and has not recorded a stop time yet.
                if (app.GetValue("LastUsedTimeStart") is long start && start != 0 &&
                    app.GetValue("LastUsedTimeStop") is long stop && stop == 0)
                {
                    return true;
                }
            }
            catch
            {
                // Skip an unreadable subkey and keep scanning.
            }
        }
        return false;
    }
}
