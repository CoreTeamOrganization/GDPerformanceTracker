using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Portfolio-wide cold-start timing utility. Measures elapsed time from actual
/// OS process creation (not Unity engine init) to two milestones:
///   1. Unity taking control (managed code first running) — captured
///      automatically, no dev integration needed.
///   2. The game becoming interactive/playable — dev calls
///      CaptureInteractiveTime() at the appropriate point for this game.
///
/// Only CaptureInteractiveTime() and GetAnalyticsPayload() are public;
/// Unity-control capture is internal and requires no setup.
/// </summary>
internal static class GDStartupTime
{
    /// <summary>Kill switch (OFF by default, like the FPS tracker). Milestones are
    /// still captured in RAM either way (cost: two timestamp reads) so that enabling
    /// later — e.g. after remote config arrives — still has the data; but while
    /// disabled, GetAnalyticsPayload() returns NULL so nothing gets logged.</summary>
    internal static bool Enabled = false;

    public const float NotCaptured = -1f;

    // Pipeline keys for the loadTime event:
    //   coldStartTimeMs — process start → Unity takes control (the cold start itself)
    //   appLoadTimeMs   — process start → game genuinely playable (the loading)
    public const string KeyColdStartTimeMs = "coldStartTimeMs";
    public const string KeyAppLoadTimeMs   = "appLoadTimeMs";

#if UNITY_ANDROID && !UNITY_EDITOR
    private static readonly AndroidJavaClass _processClass = new AndroidJavaClass("android.os.Process");
    private static readonly AndroidJavaClass _clockClass   = new AndroidJavaClass("android.os.SystemClock");
    private static readonly bool _supportsGetStartUptime =
        new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT") >= 24;
#endif

    private static float _unityControlMs = NotCaptured;
    private static float _interactiveMs  = NotCaptured;
    private static bool  _unityControlCaptured;
    private static bool  _interactiveCaptured;

    /// <summary>
    /// Auto-invoked by Unity at the earliest available hook — no dev call needed.
    /// Idempotent: only the first invocation is recorded.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void CaptureUnityControlTime()
    {
        if (_unityControlCaptured) return;
        _unityControlMs = SinceProcessStartMs();
        _unityControlCaptured = true;
    }

    /// <summary>
    /// Call the moment the game is genuinely playable for this title — first
    /// input enabled, menu interactive, first level ready, etc. Definition of
    /// "interactive" is per-game; call site is not standardized across the portfolio.
    /// Idempotent: only the first call is recorded.
    /// </summary>
    public static void CaptureInteractiveTime()
    {
        if (_interactiveCaptured) return;
        _interactiveMs = SinceProcessStartMs();
        _interactiveCaptured = true;
    }

    /// <summary>
    /// Snapshot for the analytics layer (Firebase/Metica) to log. Uncaptured
    /// values come back as NotCaptured (-1) so downstream logging can tell
    /// "not measured" apart from "0ms" — never silently sends zeros.
    /// </summary>
    public static Dictionary<string, object> GetAnalyticsPayload()
    {
        if (!Enabled) return null;   // kill switch: no payload, nothing to log
        return new Dictionary<string, object>
        {
            { KeyColdStartTimeMs, _unityControlCaptured ? _unityControlMs : NotCaptured },
            { KeyAppLoadTimeMs,   _interactiveCaptured  ? _interactiveMs  : NotCaptured }
        };
    }

    /// <summary>
    /// Milliseconds from OS process creation to right now. Android: uses
    /// android.os.Process.getStartUptimeMillis() paired with SystemClock.uptimeMillis()
    /// (same clock base, both exclude deep sleep). Falls back to
    /// Time.realtimeSinceStartup on editor / pre-API-24 devices — note this fallback
    /// measures from engine init, not process creation, so it is not directly
    /// comparable to real Android-branch readings.
    /// </summary>
    private static float SinceProcessStartMs()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (_supportsGetStartUptime)
        {
            try
            {
                long start = _processClass.CallStatic<long>("getStartUptimeMillis");
                long now   = _clockClass.CallStatic<long>("uptimeMillis");
                return now - start;
            }
            catch { }
        }
#endif
        return Time.realtimeSinceStartup * 1000f;
    }
}
