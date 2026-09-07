using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Setup wizard for GD Performance Tracker.
/// Menu: Tools > GD Performance Tracker > Performance Tracker Wizard
///
/// Step 1 — pick a scene; the wizard adds the GDPerfTracker prefab to it
///          (skips if the scene already has one) and saves the scene.
/// Step 2 — shows the three integration calls the developer wires up manually,
///          each with a Copy button.
/// </summary>
public class PerformanceTrackerWizard : EditorWindow
{
    const string PrefabSearchFilter = "GDPerfTracker t:Prefab";

    int _page;
    int _sceneIndex;
    string[] _scenePaths = new string[0];
    string[] _sceneNames = new string[0];
    string _status = "";
    Vector2 _scroll;

    GUIStyle _codeStyle, _stepTitle, _hintStyle, _pageTitle;
    Texture2D _codeBg;

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
        var w = GetWindow<PerformanceTrackerWizard>(true, "Performance Tracker Wizard");
        w.minSize = new Vector2(640, 540);
        w.RefreshSceneList();
    }

    // ---------------- styles ----------------

    void EnsureStyles()
    {
        if (_codeStyle != null) return;

        _codeBg = new Texture2D(1, 1);
        _codeBg.SetPixel(0, 0, EditorGUIUtility.isProSkin
            ? new Color(0.118f, 0.125f, 0.145f)
            : new Color(0.93f, 0.94f, 0.96f));
        _codeBg.Apply();
        _codeBg.hideFlags = HideFlags.HideAndDontSave;

        Font mono = Font.CreateDynamicFontFromOSFont(
            new[] { "SF Mono", "Menlo", "Consolas", "Courier New" }, 11);

        _codeStyle = new GUIStyle(EditorStyles.label)
        {
            font = mono,
            fontSize = 11,
            wordWrap = false,
            richText = false,
            padding = new RectOffset(12, 12, 10, 10),
            normal =
            {
                background = _codeBg,
                textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.85f, 0.87f, 0.90f)
                    : new Color(0.13f, 0.15f, 0.20f)
            }
        };

        _stepTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };
        _pageTitle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 };
        _hintStyle = new GUIStyle(EditorStyles.wordWrappedLabel)
        {
            fontSize = 11,
            normal = { textColor = EditorGUIUtility.isProSkin
                ? new Color(0.65f, 0.67f, 0.70f)
                : new Color(0.35f, 0.37f, 0.40f) }
        };
    }

    void OnGUI()
    {
        EnsureStyles();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        GUILayout.Space(14);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(16);
            using (new EditorGUILayout.VerticalScope())
            {
                if (_page == 0) DrawScenePage();
                else DrawIntegrationPage();
            }
            GUILayout.Space(16);
        }
        EditorGUILayout.EndScrollView();
    }

    // ---------------- Page 1: scene + prefab ----------------

    void DrawScenePage()
    {
        GUILayout.Label("Add the tracker to a scene", _pageTitle);
        GUILayout.Label("Step 1 of 2", _hintStyle);
        Separator();

        GUILayout.Label(
            "Pick the scene where the tracker should live — normally your FIRST scene (boot/splash). " +
            "The prefab survives scene loads, so one scene covers the whole game. " +
            "All tracking ships DISABLED. Initial on/off values can be set on the prefab in the " +
            "Inspector; Configure() from remote config (step 2) overrides them at runtime.",
            _hintStyle);

        GUILayout.Space(12);
        if (_scenePaths.Length == 0)
        {
            EditorGUILayout.HelpBox("No scenes found under Assets/.", MessageType.Warning);
            if (GUILayout.Button("Refresh scene list", GUILayout.Width(160))) RefreshSceneList();
            return;
        }

        _sceneIndex = EditorGUILayout.Popup("Target scene",
            Mathf.Clamp(_sceneIndex, 0, _scenePaths.Length - 1), _sceneNames);

        GUILayout.Space(12);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Add prefab to selected scene", GUILayout.Height(32), GUILayout.Width(220)))
                AddPrefabToScene(_scenePaths[_sceneIndex]);
            GUILayout.Space(6);
            if (GUILayout.Button("Refresh scenes", GUILayout.Height(32), GUILayout.Width(120)))
                RefreshSceneList();
        }

        if (!string.IsNullOrEmpty(_status))
        {
            GUILayout.Space(8);
            EditorGUILayout.HelpBox(_status,
                _status.StartsWith("Done") || _status.StartsWith("Already")
                    ? MessageType.Info : MessageType.Warning);
        }

        GUILayout.Space(24);
        Separator();
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Next: Integration steps  →", GUILayout.Height(30), GUILayout.Width(210)))
                _page = 1;
        }
        GUILayout.Space(10);
    }

    // ---------------- Page 2: developer integration ----------------

    void DrawIntegrationPage()
    {
        GUILayout.Label("Wire it up in code", _pageTitle);
        GUILayout.Label("Step 2 of 2 — these three calls are the developer's part. " +
                        "Adapt service names to this game's stack if it differs.", _hintStyle);
        Separator();

        DrawSnippet(1, "Configure from remote config",
            "Once at startup, after remote config is fetched. Controls BOTH kill switches: FPS tracker and startup-time reporting — overriding the prefab's Inspector defaults. Without this call, the Inspector values apply.",
            SnippetConfigure);

        DrawSnippet(2, "Log the FPS / memory event — \"perfStats\"",
            "At your chosen logging moment (level end, session end). ConsumePerfPayload() returns everything since the last call, then resets. Null = nothing recorded, skip.",
            SnippetLogPerf);

        DrawSnippet(3, "Capture + log startup time — \"loadingTime\"",
            "MarkGameInteractive() fires where THIS game becomes genuinely playable. Unity-control time is captured automatically — no prefab needed. GetStartupPayload() returns null when startup tracking is disabled — skip logging. Null-strip values before Metica (iOS drops events containing nulls).",
            SnippetStartup);

        GUILayout.Space(16);
        Separator();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("←  Back", GUILayout.Height(30), GUILayout.Width(100)))
                _page = 0;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Guide PDF", GUILayout.Height(30), GUILayout.Width(140)))
                OpenGuidePdf();
            GUILayout.Space(6);
            if (GUILayout.Button("Done", GUILayout.Height(30), GUILayout.Width(100)))
                Close();
        }
        GUILayout.Space(10);
    }

    void DrawSnippet(int number, string title, string hint, string code)
    {
        GUILayout.Space(14);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(number + ".  " + title, _stepTitle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy", GUILayout.Width(64), GUILayout.Height(22)))
            {
                EditorGUIUtility.systemCopyBuffer = code;
                ShowNotification(new GUIContent("Copied"));
            }
        }
        GUILayout.Label(hint, _hintStyle);
        GUILayout.Space(4);

        // Read-only, selectable, monospace code box.
        float height = _codeStyle.CalcHeight(new GUIContent(code), position.width - 40);
        Rect r = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
        EditorGUI.SelectableLabel(r, code, _codeStyle);
    }

    void Separator()
    {
        GUILayout.Space(8);
        Rect r = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(r, EditorGUIUtility.isProSkin
            ? new Color(1, 1, 1, 0.10f) : new Color(0, 0, 0, 0.15f));
        GUILayout.Space(8);
    }

    // ---------------- actions ----------------

    void RefreshSceneList()
    {
        var paths = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.StartsWith("Assets/")) paths.Add(p);   // skip Packages/
        }
        paths.Sort();
        _scenePaths = paths.ToArray();
        _sceneNames = new string[paths.Count];
        for (int i = 0; i < paths.Count; i++)
            _sceneNames[i] = Path.GetFileNameWithoutExtension(paths[i]) + "  —  " + paths[i];

        string active = EditorSceneManager.GetActiveScene().path;
        for (int i = 0; i < _scenePaths.Length; i++)
            if (_scenePaths[i] == active) { _sceneIndex = i; break; }
    }

    void AddPrefabToScene(string scenePath)
    {
        GameObject prefab = null;
        foreach (string guid in AssetDatabase.FindAssets(PrefabSearchFilter))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            var candidate = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (candidate != null && candidate.GetComponent<GDPerfTracker>() != null) { prefab = candidate; break; }
        }
        if (prefab == null)
        {
            _status = "GDPerfTracker prefab not found in the project. Re-import the GDPerformanceTracker package.";
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            _status = "Cancelled — current scene changes were not saved.";
            return;
        }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

#if UNITY_2022_2_OR_NEWER
        var existing = Object.FindFirstObjectByType<GDPerfTracker>();
#else
        var existing = Object.FindObjectOfType<GDPerfTracker>();
#endif
        if (existing != null)
        {
            _status = "Already set up — this scene already contains a GDPerfTracker. Nothing added.";
            return;
        }

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        Undo.RegisterCreatedObjectUndo(instance, "Add GDPerfTracker");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        EditorGUIUtility.PingObject(instance);

        _status = "Done — prefab added to '" + Path.GetFileNameWithoutExtension(scenePath) +
                  "' and the scene was saved. Click Next for the integration steps.";
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
