using System.Collections.Generic;
using System.IO;
using GDPerformanceTracker.Brand;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Setup wizard for GD Performance Tracker.
/// Menu: Tools > GD Performance Tracker > Performance Tracker Wizard
///
/// Step 1 — pick a scene (enabled Build Settings scenes only, in build order); the
///          wizard adds the GDPerfTracker prefab to it (skips if the scene already
///          has one), saves the scene and selects it. The page shows a live picture
///          of the component as it appears in the Inspector, with the reminder that
///          GDPerformance.Configure() from remote config overrides those values.
/// Step 2 — shows the three integration calls the developer wires up manually,
///          each with a Copy button.
///
/// Visual language: Game District cream / navy / gold (see Brand/BrandTokens.cs).
/// Fixed 900×600 hub window with the 6px gold left-bar.
/// </summary>
public class PerformanceTrackerWizard : EditorWindow
{
    const string PrefabSearchFilter = "GDPerfTracker t:Prefab";

    enum Tone { Gold, Ok, Warn }

    int _page;
    int _sceneIndex;
    string[] _scenePaths = new string[0];
    string[] _sceneNames = new string[0];
    string _status = "";
    bool _statusOk;
    Vector2 _scroll;

    // Inspector values read back from the prefab asset, or from the instance once placed.
    bool _defaultsKnown;
    bool _defTracking;
    float _defInterval = 1f;
    bool _defStartup;
    string _defSource = "";

    bool _stylesBuilt;
    GUIStyle _h2, _h3, _lede, _body, _muted, _eyebrow, _footnote, _statValue, _statValueOff,
             _calloutTitle, _code, _popup, _btnPrimary, _btnSecondary, _btnGhost,
             _calloutGold, _calloutOk, _calloutWarn,
             _inspLabel, _inspBold, _inspMutedStyle, _inspIcon, _inspCheck;

    const string SnippetConfigure =
@"bool  perfOn    = MonetizationServices.Remote.GetRemoteValue<bool>(""perf_tracking_enabled"");
float interval   = MonetizationServices.Remote.GetRemoteValue<float>(""perf_sample_interval"");
bool  startupOn  = MonetizationServices.Remote.GetRemoteValue<bool>(""startup_tracking_enabled"");

GDPerformance.Configure(perfOn, interval, startupOn);";

    const string SnippetLogPerf =
@"// 1) Get the payload from the utility first (null = tracking off / nothing recorded — skip):
Dictionary<string, object> payload = GDPerformance.ConsumePerfPayload();
if (payload == null) return;

// 2) Base fields — REQUIRED on every event in the pipeline, added one by one:
payload[""adid""]            = AdjustAnalyticsNetwork.AdId;
payload[""appToken""]        = AdjustAnalyticsNetwork.AppToken;
payload[""abTest""]          = analytics.AbTest;           // your GDMeticaAnalytics instance
payload[""abGroup""]         = analytics.AbGroup;
payload[""abTestStartDate""] = analytics.AbTestStartDate;

// 3) Game context — whatever THIS game tracks on its events, e.g.:
payload[""taskId""]      = taskId;
payload[""taskName""]    = taskName;
payload[""subTaskId""]   = subTaskId;
payload[""subTaskName""] = subTaskName;
payload[""day""]         = day;

// 4) Send (null-strip first: Metica drops the whole event on iOS if any value is null)
MeticaSdk.Analytics.LogCustomEvent(""perfStats"", payload);";

    const string SnippetStartup =
@"// Once, the moment THIS game is genuinely playable (menu ready / first input enabled):
GDPerformance.MarkGameInteractive();

// 1) Get the payload from the utility first (null = startup tracking off — skip):
Dictionary<string, object> payload = GDPerformance.GetStartupPayload();
if (payload == null) return;

// 2) Base fields — REQUIRED on every event in the pipeline, added one by one:
payload[""adid""]            = AdjustAnalyticsNetwork.AdId;
payload[""appToken""]        = AdjustAnalyticsNetwork.AppToken;
payload[""abTest""]          = analytics.AbTest;           // your GDMeticaAnalytics instance
payload[""abGroup""]         = analytics.AbGroup;
payload[""abTestStartDate""] = analytics.AbTestStartDate;

// 3) Game context — whatever THIS game tracks on its events, e.g.:
payload[""taskId""]      = taskId;
payload[""taskName""]    = taskName;
payload[""subTaskId""]   = subTaskId;
payload[""subTaskName""] = subTaskName;
payload[""day""]         = day;

// 4) Send (null-strip first: adid can still be null this early; Metica drops the event on iOS)
MeticaSdk.Analytics.LogCustomEvent(""loadingTime"", payload);";

    [MenuItem("Tools/GD Performance Tracker/Performance Tracker Wizard")]
    static void Open()
    {
        var w = GetWindow<PerformanceTrackerWizard>(true, "Performance Tracker Wizard", true);
        w.minSize = BrandTokens.HubSize;
        w.maxSize = BrandTokens.HubSize;
        w.wantsMouseMove = true;
        w.RefreshSceneList();
        w.ReadDefaultsFromPrefabAsset();
    }

    // ---------------- styles ----------------

    void EnsureStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        Font heading = BrandTokens.Fraunces;
        Font italic  = BrandTokens.FrauncesItalic;
        Font ui      = BrandTokens.Inter;
        Font mono    = Font.CreateDynamicFontFromOSFont(
            new[] { "SF Mono", "Menlo", "Consolas", "Courier New" }, BrandTokens.SizeMono);

        _h2       = BrandTokens.MakeStyle(heading, BrandTokens.SizeH2, BrandTokens.Navy);
        _h3       = BrandTokens.MakeStyle(heading, BrandTokens.SizeH3, BrandTokens.Navy);
        _lede     = BrandTokens.MakeWrappedStyle(italic ?? heading, BrandTokens.SizeLede, BrandTokens.Ink,
                        italic != null ? FontStyle.Normal : FontStyle.Italic);
        _body     = BrandTokens.MakeWrappedStyle(ui, BrandTokens.SizeBody, BrandTokens.Ink);
        _muted    = BrandTokens.MakeWrappedStyle(ui, BrandTokens.SizeUI, BrandTokens.WarmGray);
        _eyebrow  = BrandTokens.MakeStyle(ui, BrandTokens.SizeEyebrow, BrandTokens.WarmGray, FontStyle.Bold);
        _footnote = BrandTokens.MakeStyle(ui, BrandTokens.SizeFootnote, BrandTokens.WarmGray, FontStyle.Normal, TextAnchor.MiddleLeft);
        _statValue    = BrandTokens.MakeStyle(heading, BrandTokens.SizeStatNum, BrandTokens.Navy);
        _statValueOff = BrandTokens.MakeStyle(heading, BrandTokens.SizeStatNum, BrandTokens.Amber);
        _calloutTitle = BrandTokens.MakeStyle(ui, BrandTokens.SizeBody, BrandTokens.Navy, FontStyle.Bold);

        _code = new GUIStyle
        {
            fontSize = BrandTokens.SizeMono,
            wordWrap = false,
            richText = false,
            padding  = new RectOffset(18, 16, 12, 12),
        };
        if (mono != null) _code.font = mono;
        var codeBg = BrandTokens.SolidTex(BrandTokens.Navy);
        _code.normal.background  = codeBg; _code.normal.textColor  = BrandTokens.Cream;
        _code.hover.background   = codeBg; _code.hover.textColor   = BrandTokens.Cream;
        _code.active.background  = codeBg; _code.active.textColor  = BrandTokens.Cream;
        _code.focused.background = codeBg; _code.focused.textColor = BrandTokens.Cream;

        _btnPrimary   = MakeButton(ui, BrandTokens.Gold, BrandTokens.Navy, Color.Lerp(BrandTokens.Gold, BrandTokens.Navy, 0.12f));
        _btnSecondary = MakeButton(ui, BrandTokens.Navy, BrandTokens.Cream, Color.Lerp(BrandTokens.Navy, BrandTokens.Cream, 0.15f));
        _btnGhost     = MakeButton(ui, BrandTokens.Tint(BrandTokens.Navy, 0.08f), BrandTokens.Navy, BrandTokens.Tint(BrandTokens.Gold, 0.35f));

        _popup = new GUIStyle(EditorStyles.popup) { fontSize = BrandTokens.SizeUI, fixedHeight = 26 };
        if (ui != null) _popup.font = ui;

        _inspLabel      = BrandTokens.MakeStyle(ui, 12, InspText,  FontStyle.Normal, TextAnchor.MiddleLeft);
        _inspBold       = BrandTokens.MakeStyle(ui, 12, InspText,  FontStyle.Bold,   TextAnchor.MiddleLeft);
        _inspMutedStyle = BrandTokens.MakeStyle(ui, 12, InspMuted, FontStyle.Normal, TextAnchor.MiddleLeft);
        _inspIcon       = BrandTokens.MakeStyle(ui, 10, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter);
        _inspCheck      = BrandTokens.MakeStyle(ui, 12, InspText,  FontStyle.Bold,   TextAnchor.MiddleCenter);

        _calloutGold = MakeCallout(BrandTokens.Gold);
        _calloutOk   = MakeCallout(BrandTokens.Shipped);
        _calloutWarn = MakeCallout(BrandTokens.Overdue);
    }

    static GUIStyle MakeButton(Font font, Color bg, Color fg, Color hoverBg)
    {
        var s = new GUIStyle
        {
            fontSize  = BrandTokens.SizeUI,
            alignment = TextAnchor.MiddleCenter,
            padding   = new RectOffset(18, 18, 0, 0),
            richText  = false,
        };
        if (font != null) s.font = font;
        s.normal.background  = BrandTokens.SolidTex(bg);              s.normal.textColor  = fg;
        s.hover.background   = BrandTokens.SolidTex(hoverBg);         s.hover.textColor   = fg;
        s.focused.background = BrandTokens.SolidTex(bg);              s.focused.textColor = fg;
        s.active.background  = BrandTokens.SolidTex(BrandTokens.Navy); s.active.textColor = BrandTokens.Cream;
        return s;
    }

    static GUIStyle MakeCallout(Color accent)
    {
        var s = new GUIStyle { padding = new RectOffset(20, 16, 12, 14) };
        s.normal.background = BrandTokens.SolidTex(BrandTokens.Tint(accent, 0.10f));
        return s;
    }

    // ---------------- frame ----------------

    void OnGUI()
    {
        EnsureStyles();
        if (Event.current.type == EventType.MouseMove) Repaint();

        // Page ground + the 6px gold left-bar signature.
        BrandTokens.Fill(new Rect(0, 0, position.width, position.height), BrandTokens.Cream);
        BrandTokens.Fill(new Rect(0, 0, BrandTokens.GoldBarWidth, position.height), BrandTokens.Gold);

        var content = new Rect(
            BrandTokens.GoldBarWidth + BrandTokens.PadEdge,
            BrandTokens.PadTop,
            position.width - BrandTokens.GoldBarWidth - BrandTokens.PadEdge * 2f,
            position.height - BrandTokens.PadTop * 2f);

        GUILayout.BeginArea(content);
        if (_page == 0) DrawScenePage();
        else DrawIntegrationPage();
        GUILayout.EndArea();
    }

    void Header(string eyebrow, string title, string lede)
    {
        Eyebrow(eyebrow);
        GUILayout.Space(6);
        GUILayout.Label(title, _h2);
        if (!string.IsNullOrEmpty(lede))
        {
            GUILayout.Space(4);
            GUILayout.Label(lede, _lede);
        }
        Hairline(14, 16);
    }

    void Eyebrow(string text)
    {
        Rect r = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
        BrandTokens.Fill(new Rect(r.x, r.y + 5, BrandTokens.EyebrowSquare, BrandTokens.EyebrowSquare), BrandTokens.Gold);
        float x = r.x + BrandTokens.EyebrowSquare + 8;
        GUI.Label(new Rect(x, r.y, r.width - (x - r.x), r.height), text.ToUpperInvariant(), _eyebrow);
    }

    void Hairline(float before, float after)
    {
        GUILayout.Space(before);
        Rect r = GUILayoutUtility.GetRect(0, BrandTokens.Hairline, GUILayout.ExpandWidth(true));
        BrandTokens.Fill(r, BrandTokens.Taupe);
        GUILayout.Space(after);
    }

    void Callout(Tone tone, string title, string body)
    {
        GUIStyle box   = tone == Tone.Ok ? _calloutOk : tone == Tone.Warn ? _calloutWarn : _calloutGold;
        Color   accent = tone == Tone.Ok ? BrandTokens.Shipped : tone == Tone.Warn ? BrandTokens.Overdue : BrandTokens.Gold;

        using (new GUILayout.VerticalScope(box))
        {
            if (!string.IsNullOrEmpty(title))
            {
                GUILayout.Label(title, _calloutTitle);
                GUILayout.Space(4);
            }
            GUILayout.Label(body, _body);
        }
        AccentBar(accent);
    }

    /// <summary>3px accent bar down the left edge of the last laid-out block.</summary>
    void AccentBar(Color accent)
    {
        if (Event.current.type != EventType.Repaint) return;
        Rect r = GUILayoutUtility.GetLastRect();
        BrandTokens.Fill(new Rect(r.x, r.y, 3, r.height), accent);
    }

    // ---------------- Page 1: scene + prefab ----------------

    void DrawScenePage()
    {
        Header("Step 1 of 2", "Add the tracker to a scene",
            "Pick the scene where the tracker should live — normally scene 0 (boot or splash). " +
            "Only scenes enabled in Build Settings are listed, in build order. " +
            "The prefab survives scene loads, so one scene covers the whole game.");

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

        if (_scenePaths.Length == 0)
        {
            Callout(Tone.Warn, "No scenes in Build Settings",
                "Only scenes enabled in File > Build Settings are listed, because only those ship. " +
                "Add your boot scene there, then refresh the list.");
            GUILayout.Space(12);
            if (GUILayout.Button("Refresh scene list", _btnGhost, GUILayout.Width(160), GUILayout.Height(32)))
                RefreshSceneList();
        }
        else
        {
            Eyebrow("Target scene  ·  from Build Settings");
            GUILayout.Space(6);
            _sceneIndex = EditorGUILayout.Popup(
                Mathf.Clamp(_sceneIndex, 0, _scenePaths.Length - 1), _sceneNames, _popup, GUILayout.Width(560));
            GUILayout.Space(4);
            GUILayout.Label(_scenePaths[_sceneIndex], _muted);

            GUILayout.Space(14);
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add prefab to selected scene", _btnPrimary, GUILayout.Width(240), GUILayout.Height(32)))
                    AddPrefabToScene(_scenePaths[_sceneIndex]);
                GUILayout.Space(8);
                if (GUILayout.Button("Refresh scenes", _btnGhost, GUILayout.Width(130), GUILayout.Height(32)))
                    RefreshSceneList();
            }
        }

        if (!string.IsNullOrEmpty(_status))
        {
            GUILayout.Space(14);
            Callout(_statusOk ? Tone.Ok : Tone.Warn, null, _status);
        }

        GUILayout.Space(18);
        DrawDefaultsPanel();
        GUILayout.Space(8);

        EditorGUILayout.EndScrollView();

        Hairline(10, 12);
        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label("Game District · Performance Tracker", _footnote, GUILayout.Height(32));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Next: Integration steps  →", _btnPrimary, GUILayout.Width(230), GUILayout.Height(32)))
            {
                _page = 1;
                _scroll = Vector2.zero;
            }
        }
    }

    /// <summary>
    /// The prefab ships with everything OFF. Show the component exactly as the developer will
    /// see it in the Inspector (live values), and say plainly that remote config is the switch.
    /// </summary>
    void DrawDefaultsPanel()
    {
        using (new GUILayout.VerticalScope(_calloutGold))
        {
            GUILayout.Label("This is the prefab in the Inspector — its values are only the starting point", _calloutTitle);
            if (_defaultsKnown)
                GUILayout.Label("Values read live from the " + _defSource + ".", _muted);
            GUILayout.Space(10);

            using (new GUILayout.HorizontalScope())
            {
                DrawInspectorMock(430f);
                GUILayout.Space(20);
                using (new GUILayout.VerticalScope())
                {
                    GUILayout.Label(
                        "Right now: FPS / memory tracking " + OnOff(_defTracking) +
                        ", sample interval " + _defInterval.ToString("0.#") + " s, startup-time reporting " +
                        OnOff(_defStartup) + ". While tracking is OFF nothing records and no perfStats or " +
                        "loadingTime event is ever logged.",
                        _body);
                    GUILayout.Space(8);
                    GUILayout.Label(
                        "Do not treat these Inspector fields as the switch. Call GDPerformance.Configure() " +
                        "once remote config is fetched so perf_tracking_enabled, perf_sample_interval and " +
                        "startup_tracking_enabled control both kill switches at runtime. The Configure() " +
                        "snippet is in Step 2.",
                        _body);
                }
            }
        }
        AccentBar(BrandTokens.Gold);
    }

    // Unity dark-skin Inspector colours, so the mock reads as "the Inspector" in either editor skin.
    static readonly Color InspBg     = new Color32(56,  56,  56,  255);
    static readonly Color InspHeader = new Color32(64,  64,  64,  255);
    static readonly Color InspText   = new Color32(210, 210, 210, 255);
    static readonly Color InspMuted  = new Color32(150, 150, 150, 255);
    static readonly Color InspField  = new Color32(42,  42,  42,  255);
    static readonly Color InspBorder = new Color32(28,  28,  28,  255);
    static readonly Color InspKnob   = new Color32(190, 190, 190, 255);
    static readonly Color InspScript = new Color32(90,  158, 90,  255);

    /// <summary>Draws a read-only picture of the GDPerfTracker component as it appears in the Inspector.</summary>
    void DrawInspectorMock(float width)
    {
        const float pad = 10f, headerH = 26f, rowH = 22f, labelW = 210f;
        float height = pad + headerH + 6f + rowH * 5f + pad;
        Rect r = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
        if (Event.current.type != EventType.Repaint) return;

        BrandTokens.Fill(r, InspBg);
        BrandTokens.Outline(r, InspBorder);

        // Component header: foldout arrow, script icon, enabled checkbox, title.
        var header = new Rect(r.x, r.y + pad, r.width, headerH);
        BrandTokens.Fill(header, InspHeader);
        GUI.Label(new Rect(header.x + 8, header.y, 14, headerH), "▾", _inspBold);
        var icon = new Rect(header.x + 26, header.y + 6, 14, 14);
        BrandTokens.Fill(icon, InspScript);
        GUI.Label(icon, "#", _inspIcon);
        MockToggle(new Rect(header.x + 48, header.y + 6, 14, 14), true);
        GUI.Label(new Rect(header.x + 70, header.y, header.width - 70, headerH), "GD Perf Tracker (Script)", _inspBold);

        float y = header.yMax + 6f;
        float fieldX = r.x + pad + labelW;
        float fieldW = r.width - pad * 2f - labelW;

        // Script row.
        GUI.Label(new Rect(r.x + pad, y, labelW, rowH), "Script", _inspMutedStyle);
        var scriptField = new Rect(fieldX, y + 3, fieldW, rowH - 6);
        BrandTokens.Fill(scriptField, InspField);
        GUI.Label(new Rect(scriptField.x + 6, scriptField.y, scriptField.width - 12, scriptField.height), "GDPerfTracker", _inspMutedStyle);
        y += rowH;

        // [Header] attribute text — clipped like the real Inspector.
        GUI.BeginGroup(new Rect(r.x + pad, y, r.width - pad * 2f, rowH));
        GUI.Label(new Rect(0, 0, 900, rowH), "Initial values — GDPerformance.Configure() (remote config) overrides these at runtime", _inspBold);
        GUI.EndGroup();
        y += rowH;

        // Tracking Enabled.
        GUI.Label(new Rect(r.x + pad, y, labelW, rowH), "Tracking Enabled", _inspLabel);
        MockToggle(new Rect(fieldX, y + 4, 14, 14), _defTracking);
        y += rowH;

        // Sample Interval Seconds — [Range(1, 30)] slider + value box.
        GUI.Label(new Rect(r.x + pad, y, labelW, rowH), "Sample Interval Seconds", _inspLabel);
        const float valueW = 56f;
        var track = new Rect(fieldX + 6, y + rowH * 0.5f - 1, fieldW - valueW - 18, 2);
        BrandTokens.Fill(track, InspMuted);
        float t = Mathf.InverseLerp(1f, 30f, _defInterval);
        var knob = new Rect(track.x + t * track.width - 6, track.y - 5, 12, 12);
        BrandTokens.Fill(knob, InspKnob);
        var valueBox = new Rect(fieldX + fieldW - valueW, y + 3, valueW, rowH - 6);
        BrandTokens.Fill(valueBox, InspField);
        GUI.Label(new Rect(valueBox.x + 6, valueBox.y, valueBox.width - 6, valueBox.height), _defInterval.ToString("0.#"), _inspLabel);
        y += rowH;

        // Startup Tracking Enabled.
        GUI.Label(new Rect(r.x + pad, y, labelW, rowH), "Startup Tracking Enabled", _inspLabel);
        MockToggle(new Rect(fieldX, y + 4, 14, 14), _defStartup);
    }

    void MockToggle(Rect box, bool on)
    {
        BrandTokens.Fill(box, InspField);
        BrandTokens.Outline(box, InspBorder);
        if (on) GUI.Label(new Rect(box.x - 1, box.y - 2, box.width + 2, box.height + 2), "✓", _inspCheck);
    }

    static string OnOff(bool on) => on ? "ON" : "OFF";

    // ---------------- Page 2: developer integration ----------------

    void DrawIntegrationPage()
    {
        Header("Step 2 of 2", "Wire it up in code",
            "These three calls are the developer's part. Adapt service names to this game's stack if it differs. " +
            "Until Configure() runs, the prefab's Inspector defaults apply — and those ship OFF.");

        _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

        DrawSnippet(1, "Configure from remote config",
            "Once at startup, after remote config is fetched. Controls BOTH kill switches — FPS tracker and " +
            "startup-time reporting — overriding the prefab's Inspector defaults. Without this call, the " +
            "Inspector values apply.",
            SnippetConfigure);

        DrawSnippet(2, "Log the FPS / memory event — \"perfStats\"",
            "At your chosen logging moment (level end, session end). Pattern for every event: get the payload " +
            "from the utility, add the five required base fields (adid, appToken, abTest, abGroup, " +
            "abTestStartDate), then this game's own context fields, then send. Null = nothing recorded, skip.",
            SnippetLogPerf);

        DrawSnippet(3, "Capture + log startup time — \"loadingTime\"",
            "MarkGameInteractive() fires where THIS game becomes genuinely playable. Unity-control time is " +
            "captured automatically — no prefab needed. Same pattern: payload from the utility, the five required " +
            "base fields, game context, send. GetStartupPayload() returns null when startup tracking is disabled " +
            "— skip logging. Null-strip before Metica (iOS drops events containing nulls).",
            SnippetStartup);

        GUILayout.Space(8);
        EditorGUILayout.EndScrollView();

        Hairline(10, 12);
        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("←  Back", _btnGhost, GUILayout.Width(100), GUILayout.Height(32)))
            {
                _page = 0;
                _scroll = Vector2.zero;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Guide PDF", _btnSecondary, GUILayout.Width(150), GUILayout.Height(32)))
                OpenGuidePdf();
            GUILayout.Space(8);
            if (GUILayout.Button("Done", _btnPrimary, GUILayout.Width(110), GUILayout.Height(32)))
                Close();
        }
    }

    void DrawSnippet(int number, string title, string hint, string code)
    {
        if (number > 1) GUILayout.Space(20);

        using (new GUILayout.HorizontalScope())
        {
            GUILayout.Label(number + ".  " + title, _h3);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", _btnGhost, GUILayout.Width(76), GUILayout.Height(26)))
            {
                EditorGUIUtility.systemCopyBuffer = code;
                ShowNotification(new GUIContent("Copied"));
            }
        }
        GUILayout.Space(4);
        GUILayout.Label(hint, _muted);
        GUILayout.Space(8);

        // Read-only, selectable, monospace code box on navy with the gold accent bar.
        float width  = position.width - BrandTokens.GoldBarWidth - BrandTokens.PadEdge * 2f - 20f;
        float height = _code.CalcHeight(new GUIContent(code), width);
        Rect r = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        EditorGUI.SelectableLabel(r, code, _code);
        BrandTokens.Fill(new Rect(r.x, r.y, 3, r.height), BrandTokens.Gold);
    }

    // ---------------- actions ----------------

    void SetStatus(bool ok, string message)
    {
        _statusOk = ok;
        _status = message;
        Repaint();
    }

    void RefreshSceneList()
    {
        // Only scenes that ship: enabled entries in File > Build Settings, in build order.
        // Index 0 is the boot scene, which is where the tracker normally belongs.
        var paths = new List<string>();
        var names = new List<string>();
        foreach (EditorBuildSettingsScene s in EditorBuildSettings.scenes)
        {
            if (!s.enabled || string.IsNullOrEmpty(s.path)) continue;
            paths.Add(s.path);
            // No "/" in popup items: Unity turns slashes into nested submenus. Path is shown below instead.
            names.Add(paths.Count - 1 + "  ·  " + Path.GetFileNameWithoutExtension(s.path));
        }
        _scenePaths = paths.ToArray();
        _sceneNames = names.ToArray();

        // Default to the boot scene; prefer the currently open scene if it is in the build.
        _sceneIndex = 0;
        string active = EditorSceneManager.GetActiveScene().path;
        for (int i = 0; i < _scenePaths.Length; i++)
            if (_scenePaths[i] == active) { _sceneIndex = i; break; }
    }

    GameObject FindPrefabAsset()
    {
        foreach (string guid in AssetDatabase.FindAssets(PrefabSearchFilter))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (candidate != null && candidate.GetComponent<GDPerfTracker>() != null) return candidate;
        }
        return null;
    }

    void ReadDefaultsFromPrefabAsset()
    {
        var prefab = FindPrefabAsset();
        if (prefab != null) ReadDefaults(prefab.GetComponent<GDPerfTracker>(), "prefab asset");
    }

    void ReadDefaults(GDPerfTracker tracker, string source)
    {
        if (tracker == null) return;
        var so = new SerializedObject(tracker);
        var tracking = so.FindProperty("trackingEnabled");
        var interval = so.FindProperty("sampleIntervalSeconds");
        var startup  = so.FindProperty("startupTrackingEnabled");
        if (tracking == null || interval == null || startup == null) return;

        _defTracking   = tracking.boolValue;
        _defInterval   = interval.floatValue;
        _defStartup    = startup.boolValue;
        _defSource     = source;
        _defaultsKnown = true;
    }

    void AddPrefabToScene(string scenePath)
    {
        GameObject prefab = FindPrefabAsset();
        if (prefab == null)
        {
            SetStatus(false, "GDPerfTracker prefab not found in the project. Re-import the GDPerformanceTracker package.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            SetStatus(false, "Cancelled — current scene changes were not saved.");
            return;
        }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        string sceneName = Path.GetFileNameWithoutExtension(scenePath);

#if UNITY_2022_2_OR_NEWER
        var existing = Object.FindFirstObjectByType<GDPerfTracker>();
#else
        var existing = Object.FindObjectOfType<GDPerfTracker>();
#endif
        if (existing != null)
        {
            ReadDefaults(existing, "instance already in '" + sceneName + "'");
            SetStatus(true, "Already set up — '" + sceneName + "' already contains a GDPerfTracker. Nothing added.");
            Selection.activeGameObject = existing.gameObject;
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Add GDPerfTracker");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorGUIUtility.PingObject(instance);

        ReadDefaults(instance.GetComponent<GDPerfTracker>(), "instance placed in '" + sceneName + "'");
        SetStatus(true, "Done — prefab added to '" + sceneName + "' and the scene was saved. " +
                        "It is selected in the Hierarchy; compare with the panel below, then click Next.");
        Selection.activeGameObject = instance;
    }

    void OpenGuidePdf()
    {
        foreach (string guid in AssetDatabase.FindAssets("GDPerformanceTracker-Guide"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.EndsWith(".pdf"))
            {
                EditorUtility.OpenWithDefaultApp(p);
                return;
            }
        }
        ShowNotification(new GUIContent("Guide PDF not found"));
    }
}
