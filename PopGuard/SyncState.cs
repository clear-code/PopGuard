namespace PopGuard;

/// <summary>Effective state of an auto-suppress source (focus session / microphone), for display.</summary>
internal enum SyncState
{
    Off,         // Disabled by config
    On,          // Enabled and working on this machine
    Unavailable, // Enabled by config, but not available here (e.g. OS too old / not supported)
}
