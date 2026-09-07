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
///          wizard adds the GDPerfTracker prefab to it
///          (skips if the scene already has one), saves the scene, and shows the
///          prefab's shipped defaults with a reminder to override them via
///          GDPerformance.Configure() from remote config.
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
             _calloutGold, _calloutOk, _calloutWarn;

    const string SnippetConfigure =
@"bool  perfOn    = MonetizationServices.Remote.GetRemoteValue<bool>(""perf_tracking_enabled"");
float interval   = MonetizationServices.Remote.GetRemoteValue<float>(""perf_sample_interval"");
bool  startupOn  = MonetizationServices.Remote.GetRemoteValue<bool>(""startup_tracking_enabled"");

GDPerformance.Configure(perfOn, interval, startupOn);";

    const string SnippetLogPerf =
@"Dictionary<string, object> perf = GDPerformance.ConsumePerfPayload();
if (perf != null)
{
    perf[""adid""]     = AdjustAnalyticsNetwork.AdId;
    perf[""appToken""] = AdjustAnalyticsNetwork.AppToken;

    MeticaSdk.Analytics.LogCustomEvent(""perfStats"", perf);
}";

    const string SnippetStartup =
@"GDPerformance.MarkGameInteractive();

Dictionary<string, object> payload = GDPerformance.GetStartupPayload();
if (payload != null)
{
    payload[""adjustAdid""]     = adjustAdid;
    payload[""adjustAppToken""] = adjustAppToken;

    MeticaSdk.Analytics.LogCustomEvent(""loadingTime"", payload);
}";

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
    /// The prefab ships with everything OFF. Show the real Inspector values and make it
    /// unmistakable that remote config (Configure) is the switch, not the Inspector.
    /// </summary>
    void DrawDefaultsPanel()
    {
        using (new GUILayout.VerticalScope(_calloutGold))
        {
            GUILayout.Label("Prefab defaults — override them from remote config", _calloutTitle);
            if (_defaultsKnown)
                GUILayout.Label("Values read from the " + _defSource + ".", _muted);
            GUILayout.Space(10);

            using (new GUILayout.HorizontalScope())
            {
                Stat("FPS / memory tracking", OnOff(_defTracking), !_defTracking);
                Stat("Sample interval",       _defInterval.ToString("0.#") + " s", false);
                Stat("Startup-time reporting", OnOff(_defStartup), !_defStartup);
            }

            GUILayout.Space(12);
            GUILayout.Label(
                "These are only the INITIAL values. While tracking is OFF nothing records and no perfStats or " +
                "loadingTime event is ever logged. Do not treat the Inspector as the switch: call " +
                "GDPerformance.Configure() once remote config is fetched so the keys perf_tracking_enabled, " +
                "perf_sample_interval and startup_tracking_enabled control both kill switches at runtime. " +
                "The Configure() snippet is in Step 2.",
                _body);
        }
        AccentBar(BrandTokens.Gold);
    }

    void Stat(string label, string value, bool attention)
    {
        using (new GUILayout.VerticalScope(GUILayout.Width(220)))
        {
            GUILayout.Label(value, attention ? _statValueOff : _statValue);
            GUILayout.Label(label, _muted);
        }
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
            "At your chosen logging moment (level end, session end). ConsumePerfPayload() returns everything " +
            "since the last call, then resets. Null = nothing recorded, skip.",
            SnippetLogPerf);

        DrawSnippet(3, "Capture + log startup time — \"loadingTime\"",
            "MarkGameInteractive() fires where THIS game becomes genuinely playable. Unity-control time is " +
            "captured automatically — no prefab needed. GetStartupPayload() returns null when startup tracking " +
            "is disabled — skip logging. Null-strip values before Metica (iOS drops events containing nulls).",
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
            names.Add(paths.Count - 1 + "  ·  " + Path.GetFileNameWithoutExtension(s.path) + "  —  " + s.path);
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
            ShowDefaultsDialog(sceneName, added: false);
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Add GDPerfTracker");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorGUIUtility.PingObject(instance);

        ReadDefaults(instance.GetComponent<GDPerfTracker>(), "instance placed in '" + sceneName + "'");
        SetStatus(true, "Done — prefab added to '" + sceneName + "' and the scene was saved. " +
                        "Check the defaults below, then click Next for the integration steps.");
        ShowDefaultsDialog(sceneName, added: true);
    }

    /// <summary>Modal reminder shown right after placement: defaults are OFF, remote config is the switch.</summary>
    void ShowDefaultsDialog(string sceneName, bool added)
    {
        string message =
            (added ? "GDPerfTracker was added to '" + sceneName + "' and the scene was saved."
                   : "'" + sceneName + "' already contains a GDPerfTracker.") +
            "\n\nCurrent Inspector values on the prefab:\n" +
            "    FPS / memory tracking:      " + OnOff(_defTracking) + "\n" +
            "    Sample interval:            " + _defInterval.ToString("0.#") + " s\n" +
            "    Startup-time reporting:     " + OnOff(_defStartup) + "\n\n" +
            "These are only the initial values. Nothing records and no perfStats or loadingTime event is " +
            "logged until GDPerformance.Configure() overrides them at runtime.\n\n" +
            "Add the remote config keys perf_tracking_enabled, perf_sample_interval and " +
            "startup_tracking_enabled, and call Configure() once remote config is fetched (Step 2).";

        bool showConfigure = EditorUtility.DisplayDialog(
            "Prefab defaults — configure via remote config",
            message, "Show me Configure()", "Later");

        if (showConfigure)
        {
            _page = 1;
            _scroll = Vector2.zero;
        }
        Repaint();
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
