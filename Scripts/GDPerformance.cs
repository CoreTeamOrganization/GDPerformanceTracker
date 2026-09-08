using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GD Performance Tracker — the ONLY class developers call.
/// Everything else in the package is internal implementation.
///
///   1. Configure(enabled, interval)   — once, after remote config is fetched
///   2. ConsumePerfPayload()           — at your logging moment ("perfStats" event)
///   3. MarkGameInteractive()          — once, when the game is genuinely playable
///   4. GetStartupPayload()            — when logging the "loadingTime" event
/// </summary>
public static class GDPerformance
{
    /// <summary>Kill switches + sample rate, typically from remote config.
    /// perfEnabled: FPS/memory tracker on/off (off = records nothing).
    /// sampleIntervalSeconds: seconds per FPS sample (1 = one per second), clamped 1–30.
    /// startupEnabled: startup-time reporting on/off (off = GetStartupPayload()
    /// returns null, so no loadingTime event gets logged).</summary>
    public static void Configure(bool perfEnabled, float sampleIntervalSeconds = 1f, bool startupEnabled = false)
    {
        GDStartupTime.Enabled = startupEnabled;
        if (TryGetTracker(out var tracker))
            tracker.Configure(perfEnabled, sampleIntervalSeconds);
    }

    /// <summary>Analytics-ready FPS/memory payload of everything recorded since the
    /// last call, then resets the window (recording continues). Returns NULL when
    /// tracking is off or nothing was recorded — skip logging on null.
    /// Add your required base fields (adid/appToken) before sending.</summary>
    public static Dictionary<string, object> ConsumePerfPayload()
    {
        return TryGetTracker(out var tracker) ? tracker.ConsumePayload() : null;
    }

    /// <summary>Call ONCE, the moment this game is genuinely playable (menu ready /
    /// first input enabled / first level loaded). Idempotent — extra calls are no-ops.
    /// Unity-control time is captured automatically; nothing to call for it.</summary>
    public static void MarkGameInteractive()
    {
        GDStartupTime.CaptureInteractiveTime();
    }

    /// <summary>Cold-start payload for the "loadingTime" event:
    /// coldStartTimeMs (process start → Unity takes control) and appLoadTimeMs
    /// (process start → game playable); -1 = that milestone was never captured.
    /// Returns NULL when startup tracking is disabled via Configure() — skip
    /// logging on null. Null-strip values before Metica on iOS.</summary>
    public static Dictionary<string, object> GetStartupPayload()
    {
        return GDStartupTime.GetAnalyticsPayload();
    }

    static bool TryGetTracker(out GDPerfTracker tracker)
    {
        tracker = GDPerfTracker.I;
        if (tracker == null)
            Debug.LogWarning("[GDPerformance] GDPerfTracker prefab is not in any loaded scene. " +
                             "Add it via Tools > GD Performance Tracker > Performance Tracker Wizard.");
        return tracker != null;
    }
}
