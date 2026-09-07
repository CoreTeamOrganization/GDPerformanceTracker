# GD Performance Tracker

## Install (Unity Package Manager)

**Window → Package Manager → `+` → Add package from git URL…** and paste:

```
https://github.com/CoreTeamOrganization/GDPerformanceTracker.git
```

This tracks the latest release on `main`. When a new version is published, Package
Manager shows the new version number on the package — click **Update** to pull it.
To lock a project to one release instead, append the tag: `…tracker.git#v1.0.1`.
Minimum Unity version: **2022.3**. No other dependencies.

Then run **Tools → GD Performance Tracker → Performance Tracker Wizard** to place
the prefab in your boot scene and copy the integration snippets.

---

## What it is

RAM-only performance instrumentation for Unity mobile games (portfolio-wide).
Two utilities behind one 4-method API:

- **GDPerfTracker** — records FPS and memory into RAM during gameplay; the developer
  consumes one aggregated payload at a moment of their choosing (level end, session end)
  and logs it through the game's own analytics (`perfStats` event).
- **GDStartupTime** — measures cold start from **actual OS process creation** (not Unity
  engine init): time until Unity takes control (automatic) and time until the game is
  genuinely playable (one developer call). Logged as the `loadingTime` event.

Neither utility writes to disk, and neither sends anything itself — they hand the
developer a `Dictionary<string, object>` and the developer's analytics code sends it.

---

## Contents

| Path | Purpose |
|---|---|
| `package.json` | UPM manifest (`com.gamedistrict.performance-tracker`). |
| `Scripts/GDPerformance.cs` | **The only class developers call** — 4-method static facade. Everything else is `internal`. |
| `Scripts/GDPerfTracker.cs` | FPS/memory recorder implementation (MonoBehaviour, lives on the prefab). Never called directly. |
| `Scripts/GDStartupTime.cs` | Cold-start stopwatch implementation (static). Never called directly. Needs no prefab. |
| `Prefabs/GDPerfTracker.prefab` | Drag-and-drop setup. Inspector holds the **initial values** (all tracking OFF, interval 1s). |
| `Editor/PerformanceTrackerWizard.cs` | Setup wizard — `Tools → GD Performance Tracker → Performance Tracker Wizard`. Editor-only. |
| `Documentation/GDPerformanceTracker-Guide.pdf` | Full integration guide (payload field reference, Metica examples, caveats). |

---

## Developer API (`GDPerformance`)

```csharp
// 1) Once, after remote config is fetched — controls BOTH kill switches,
//    overriding the prefab's Inspector defaults:
GDPerformance.Configure(perfEnabled, sampleIntervalSeconds, startupEnabled);

// 2) At the game's chosen logging moment — FPS/memory payload since last call
//    (null = tracking off / nothing recorded → skip logging):
Dictionary<string, object> perf = GDPerformance.ConsumePerfPayload();

// 3) Once, the moment the game is genuinely playable (idempotent):
GDPerformance.MarkGameInteractive();

// 4) When logging the loadingTime event (null = startup tracking disabled → skip):
Dictionary<string, object> startup = GDPerformance.GetStartupPayload();
```

The developer must append the pipeline's required base fields (`adid` / `appToken`
from `AdjustAnalyticsNetwork`) before sending, and null-strip values before Metica
(the SDK silently drops events containing nulls on iOS). Event names used across
the portfolio: **`perfStats`** and **`loadingTime`** (via
`MeticaSdk.Analytics.LogCustomEvent` — it rejects Metica core event names).

---

## Design decisions (why it is built this way)

1. **Facade pattern / minimal surface.** Developers see exactly 4 methods.
   `GDPerfTracker`'s members, `GDStartupTime`, and `PerfSummary` are `internal` so
   IntelliSense can't lead anyone to implementation details. The `GDPerfTracker`
   *type* stays `public` only because the Editor wizard (separate assembly) needs
   `GetComponent<GDPerfTracker>()`.

2. **RAM-only by design.** No PlayerPrefs, no file writes — this package was born
   out of a perf audit where per-coin `PlayerPrefs.Save()` calls froze the game;
   the instrumentation must never cause the problem it measures. Recording buffers
   are capped (~30 KB) and the only measurable work is one sort of ≤4,096 floats
   at consume time (<1 ms, once per consume).

3. **Two-layer configuration.**
   - *Layer 1 — Inspector (shipped defaults):* the prefab exposes FPS tracking
     on/off, sample interval, startup reporting on/off. All OFF by default.
   - *Layer 2 — remote config (runtime override):* `Configure()` overrides all
     three when it runs. Without the call, Inspector values apply.

4. **Startup kill switch gates OUTPUT, not measurement.** Cold start finishes
   before Firebase can answer "is tracking allowed?" — waiting for the flag would
   leave nothing to measure. So milestones are ALWAYS captured into RAM (two
   timestamp reads, zero cost, nothing leaves the device) and the switch gates
   `GetStartupPayload()`: while disabled it returns **null**, so no event is
   logged. *Measure always, report only when allowed.*

5. **FPS needs the prefab; startup does not.** Frame counting requires a
   per-frame `Update()` → MonoBehaviour on a GameObject (the prefab,
   `DontDestroyOnLoad`). Startup timing is two timestamps → static class.
   If the prefab is missing, the facade logs a clear warning and returns null
   instead of throwing.

6. **Sampling model.** FPS is bucketed (default 1 s/bucket, remote-configurable
   1–30 s). Memory sampled every 5 s from three `ProfilerRecorder` counters —
   works in **release** builds: `Total Used Memory` (allocated), `Total Reserved
   Memory`, `System Used Memory` (process footprint, closest to what Android's
   low-memory killer judges) — with a `GC.GetTotalMemory` fallback and a
   `MemoryCountersAvailable` flag when the real counters return 0 on a device.
   Frame times are skipped for 3 frames after scene load / pause / focus so
   loading spikes don't pollute the window.

7. **Payload (perfStats):** `fps_avg`, `fps_min`, percentiles `fps_p01/p05/p25/
   p50/p75/p95/p99` (p01 = the stutter metric), `worst_ms` (single worst frame),
   `ram_total`, `mem_alloc_peak/avg`, `mem_resv_peak/avg`, `mem_sys_peak/avg`
   (MB), `seconds`, `tier_fps` (the FPS cap — required to interpret FPS fields
   fairly: p50=29 on a 30-cap device is perfect). Device model is deliberately
   NOT included (tracked elsewhere in the pipeline; join on user/session id).
   **Payload (loadingTime):** `unityControlMs`, `interactiveMs`; `-1` means that
   milestone was never captured (deliberately distinct from a fake 0 ms).

8. **Android specifics.** `GDStartupTime` uses `Process.getStartUptimeMillis()`
   + `SystemClock.uptimeMillis()` on API 24+ (guarded by
   `#if UNITY_ANDROID && !UNITY_EDITOR`); Editor / pre-API-24 falls back to
   `Time.realtimeSinceStartup`, which measures from engine init — those readings
   are NOT comparable to real Android numbers. No iOS branch yet.
   `GDPerfTracker` is fully cross-platform.

9. **Setup wizard.** Two pages: (1) pick a scene → wizard instantiates the
   prefab (skips if one already exists) and saves the scene; (2) the three
   integration snippets (Configure / perfStats / loadingTime) with Copy buttons,
   monospace code styling, and an Open-Guide-PDF button. Lives in `Editor/`,
   never ships in builds.

---

## Integration flow (what a game team does)

1. Import the package.
2. Run **Tools → GD Performance Tracker → Performance Tracker Wizard** → Step 1
   puts the prefab in the boot scene.
3. Wire the three calls from Step 2 (or the PDF): Configure after remote config;
   `ConsumePerfPayload` + base fields + `LogCustomEvent("perfStats", …)` at the
   logging moment; `MarkGameInteractive` at the playable moment +
   `GetStartupPayload` + `LogCustomEvent("loadingTime", …)`.
4. Add the three remote config keys: `perf_tracking_enabled` (bool),
   `perf_sample_interval` (float), `startup_tracking_enabled` (bool).
