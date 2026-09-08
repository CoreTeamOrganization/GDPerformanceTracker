using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Profiling;

/// <summary>
/// Aggregated performance summary for the recording window consumed so far.
/// Valid is false when tracking is disabled or nothing was recorded.
/// FPS percentiles: P01 = worst 1% (stutter metric), P50 = median, P99 = best.
/// Memory: Allocated = memory Unity is actively using; Reserved = memory Unity
/// has claimed from the OS (allocated + pooled); SystemUsed = whole process
/// footprint as the OS sees it. TotalDeviceRamMB = the phone's physical RAM.
/// </summary>
internal struct PerfSummary
{
    public bool Valid;

    // FPS
    public float AvgFps;
    public float MinFps;
    public float FpsP01;
    public float FpsP05;
    public float FpsP25;
    public float FpsP50;
    public float FpsP75;
    public float FpsP95;
    public float FpsP99;
    public float WorstFrameMs;

    // Memory (MB)
    public int TotalDeviceRamMB;
    public int PeakAllocMB;
    public int AvgAllocMB;
    public int PeakReservedMB;
    public int AvgReservedMB;
    public int PeakSystemMB;
    public int AvgSystemMB;

    public int RecordedSeconds;

    /// <summary>Analytics-ready payload. Returns null when the summary is not
    /// valid (tracking disabled / nothing recorded) — skip logging in that case.</summary>
    internal Dictionary<string, object> ToPayload()
    {
        if (!Valid) return null;
        return new Dictionary<string, object>
        {
            { "fpsAvg",       Mathf.RoundToInt(AvgFps) },
            { "fpsMin",       Mathf.RoundToInt(MinFps) },
            { "fpsP01",       Mathf.RoundToInt(FpsP01) },
            { "fpsP05",       Mathf.RoundToInt(FpsP05) },
            { "fpsP25",       Mathf.RoundToInt(FpsP25) },
            { "fpsP50",       Mathf.RoundToInt(FpsP50) },
            { "fpsP75",       Mathf.RoundToInt(FpsP75) },
            { "fpsP95",       Mathf.RoundToInt(FpsP95) },
            { "fpsP99",       Mathf.RoundToInt(FpsP99) },
            { "worstMs",      Mathf.RoundToInt(WorstFrameMs) },
            { "ramTotal",     TotalDeviceRamMB },
            { "ramAllocPeak", PeakAllocMB },
            { "ramAllocAvg",  AvgAllocMB },
            { "ramResvPeak",  PeakReservedMB },
            { "ramResvAvg",   AvgReservedMB },
            { "ramSysPeak",   PeakSystemMB },
            { "ramSysAvg",    AvgSystemMB },
            { "seconds",      RecordedSeconds },
            { "tierFps",      Application.targetFrameRate },
        };
    }
}

/// <summary>
/// RAM-only FPS + memory recorder. While the kill switch (TrackingEnabled) is
/// ON it continuously records samples into in-memory lists — nothing is ever
/// written to disk. WHEN to consume the data is entirely the caller's choice:
/// call ConsumePayload() at any moment (level end, session end, anywhere) to
/// get an analytics-ready dictionary of everything recorded since the last
/// consume; the window then resets and recording continues.
///
/// Kill switch — any of these turns tracking off and clears recorded data:
///   - the trackingEnabled checkbox in the Inspector (initial value)
///   - GDPerfTracker.I.TrackingEnabled = false;         // from code
///   - GDPerfTracker.I.Configure(enabled, interval);    // from remote config, e.g.:
///
///     GDPerfTracker.I.Configure(
///         MonetizationServices.Remote.GetRemoteValue&lt;bool&gt;("perf_tracking_enabled"),
///         MonetizationServices.Remote.GetRemoteValue&lt;float&gt;("perf_sample_interval"));
///
/// Consume example (wherever the developer logs their event):
///
///     var perf = GDPerfTracker.I.ConsumePayload();   // null when nothing recorded
///     if (perf != null)
///         YourAnalyticsService.SendEvent("Perf", perf);
/// </summary>
public class GDPerfTracker : MonoBehaviour
{
    internal static GDPerfTracker I { get; private set; }

    [Header("Initial values — GDPerformance.Configure() (remote config) overrides these at runtime")]
    [Tooltip("FPS/memory tracking on/off. OFF by default; nothing records while off.")]
    [SerializeField] bool trackingEnabled = false;

    [Tooltip("Seconds per FPS sample bucket. 1 = one FPS value per second.")]
    [SerializeField, Range(1f, 30f)] float sampleIntervalSeconds = 1f;

    [Tooltip("Startup-time reporting on/off. OFF by default; while off, GetStartupPayload() returns null so no loadTime event is logged. Milestones are still captured in RAM either way.")]
    [SerializeField] bool startupTrackingEnabled = false;

    const float MemorySampleEverySeconds = 5f;
    const int MaxFpsSamples = 4096;   // ~68 min at 1s interval; oldest dropped beyond this
    const int MaxMemSamples = 1024;

    readonly List<float> _fpsSamples = new List<float>(1024);
    readonly List<long> _allocSamples = new List<long>(256);
    readonly List<long> _reservedSamples = new List<long>(256);
    readonly List<long> _systemSamples = new List<long>(256);

    float _bucketTime;
    int _bucketFrames;
    float _worstMs;
    float _memTimer;
    int _skip;              // frames ignored after load/pause/focus (garbage frame times)

    ProfilerRecorder _memAlloc;      // "Total Used Memory"     — Unity allocated
    ProfilerRecorder _memReserved;   // "Total Reserved Memory" — Unity reserved from OS
    ProfilerRecorder _memSystem;     // "System Used Memory"    — whole process (OS view)
    bool _memCountersWork;

    /// <summary>The kill switch. Setting false stops tracking and clears all
    /// recorded data; setting true starts a fresh window immediately.</summary>
    internal bool TrackingEnabled
    {
        get => trackingEnabled;
        set
        {
            if (trackingEnabled == value) return;
            trackingEnabled = value;
            ClearBuffers();
            if (value) SkipFrames();
        }
    }

    internal bool MemoryCountersAvailable => _memCountersWork;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);
        GDStartupTime.Enabled = startupTrackingEnabled;   // Inspector default; Configure() overrides
        _memAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
        _memReserved = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Reserved Memory");
        _memSystem = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "System Used Memory");
        SceneManager.sceneLoaded += OnSceneLoaded;
        SkipFrames();
    }

    void OnDestroy()
    {
        if (I == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        if (_memAlloc.Valid) _memAlloc.Dispose();
        if (_memReserved.Valid) _memReserved.Dispose();
        if (_memSystem.Valid) _memSystem.Dispose();
    }

    // ---- Remote control ---------------------------------------------------

    /// <summary>Apply remote-config values. Disabling clears recorded data.</summary>
    internal void Configure(bool enabled, float intervalSeconds)
    {
        sampleIntervalSeconds = Mathf.Clamp(intervalSeconds, 1f, 30f);
        TrackingEnabled = enabled;
    }

    // ---- Consuming ----------------------------------------------------------

    /// <summary>Analytics-ready payload of everything recorded since the last
    /// consume, then resets the window (recording continues). Returns NULL when
    /// tracking is off or nothing was recorded — skip logging on null.</summary>
    internal Dictionary<string, object> ConsumePayload()
    {
        return Consume().ToPayload();
    }

    /// <summary>Same as ConsumePayload but returns the raw summary struct, for
    /// callers that want the numbers rather than a dictionary.</summary>
    internal PerfSummary Consume()
    {
        var s = BuildSummary();
        ClearBuffers();
        return s;
    }

    /// <summary>Discard everything recorded so far and start a fresh window —
    /// e.g. right after a loading screen, so load spikes aren't counted.</summary>
    internal void ResetWindow()
    {
        ClearBuffers();
        SkipFrames();
    }

    // ---- Sampling ----------------------------------------------------------

    void Update()
    {
        if (!trackingEnabled) return;
        if (_skip > 0) { _skip--; return; }

        float dt = Time.unscaledDeltaTime;
        if (dt <= 0f) return;

        float ms = dt * 1000f;
        if (ms > _worstMs) _worstMs = ms;

        _bucketTime += dt;
        _bucketFrames++;
        if (_bucketTime >= sampleIntervalSeconds)
        {
            if (_fpsSamples.Count >= MaxFpsSamples) _fpsSamples.RemoveAt(0);
            _fpsSamples.Add(_bucketFrames / _bucketTime);
            _bucketTime = 0f;
            _bucketFrames = 0;
        }

        _memTimer += dt;
        if (_memTimer >= MemorySampleEverySeconds)
        {
            _memTimer = 0f;
            SampleMemory();
        }
    }

    void SampleMemory()
    {
        long alloc = _memAlloc.Valid ? _memAlloc.LastValue : 0;
        long reserved = _memReserved.Valid ? _memReserved.LastValue : 0;
        long system = _memSystem.Valid ? _memSystem.LastValue : 0;

        _memCountersWork = alloc > 0;
        if (!_memCountersWork)
            alloc = GC.GetTotalMemory(false);   // managed heap only — last-resort fallback

        if (_allocSamples.Count >= MaxMemSamples)
        {
            _allocSamples.RemoveAt(0);
            _reservedSamples.RemoveAt(0);
            _systemSamples.RemoveAt(0);
        }
        _allocSamples.Add(alloc);
        _reservedSamples.Add(reserved);
        _systemSamples.Add(system);
    }

    PerfSummary BuildSummary()
    {
        var s = new PerfSummary();
        if (_fpsSamples.Count == 0) return s;   // Valid stays false

        float sum = 0f, min = float.MaxValue;
        for (int i = 0; i < _fpsSamples.Count; i++)
        {
            sum += _fpsSamples[i];
            if (_fpsSamples[i] < min) min = _fpsSamples[i];
        }

        var sorted = new List<float>(_fpsSamples);
        sorted.Sort();

        s.Valid = true;
        s.AvgFps = sum / _fpsSamples.Count;
        s.MinFps = min;
        s.FpsP01 = Percentile(sorted, 0.01f);
        s.FpsP05 = Percentile(sorted, 0.05f);
        s.FpsP25 = Percentile(sorted, 0.25f);
        s.FpsP50 = Percentile(sorted, 0.50f);
        s.FpsP75 = Percentile(sorted, 0.75f);
        s.FpsP95 = Percentile(sorted, 0.95f);
        s.FpsP99 = Percentile(sorted, 0.99f);
        s.WorstFrameMs = _worstMs;
        s.RecordedSeconds = Mathf.RoundToInt(_fpsSamples.Count * sampleIntervalSeconds);

        s.TotalDeviceRamMB = SystemInfo.systemMemorySize;
        Aggregate(_allocSamples, out s.PeakAllocMB, out s.AvgAllocMB);
        Aggregate(_reservedSamples, out s.PeakReservedMB, out s.AvgReservedMB);
        Aggregate(_systemSamples, out s.PeakSystemMB, out s.AvgSystemMB);
        return s;
    }

    static float Percentile(List<float> sortedAscending, float p)
    {
        int index = Mathf.Clamp(
            Mathf.RoundToInt((sortedAscending.Count - 1) * p), 0, sortedAscending.Count - 1);
        return sortedAscending[index];
    }

    static void Aggregate(List<long> samples, out int peakMB, out int avgMB)
    {
        peakMB = 0;
        avgMB = 0;
        if (samples.Count == 0) return;
        long peak = 0, sum = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i] > peak) peak = samples[i];
            sum += samples[i];
        }
        peakMB = (int)(peak / (1024 * 1024));
        avgMB = (int)(sum / samples.Count / (1024 * 1024));
    }

    // ---- Housekeeping ------------------------------------------------------

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SkipFrames();
    void OnApplicationPause(bool paused) => SkipFrames();
    void OnApplicationFocus(bool focus) => SkipFrames();

    void SkipFrames()
    {
        _skip = 3;
        _bucketTime = 0f;
        _bucketFrames = 0;
    }

    void ClearBuffers()
    {
        _fpsSamples.Clear();
        _allocSamples.Clear();
        _reservedSamples.Clear();
        _systemSamples.Clear();
        _worstMs = 0f;
        _memTimer = 0f;
        _bucketTime = 0f;
        _bucketFrames = 0;
    }
}
