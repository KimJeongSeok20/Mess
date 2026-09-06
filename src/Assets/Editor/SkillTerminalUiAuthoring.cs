using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;

public static class SkillTerminalUiAuthoring
{
    private const string WebViewPath = "Assets/StylishEsper/SkillWeb/Resources/Prefabs/uGUI/WebViewUGUI.prefab";
    private const string NodePath = "Assets/StylishEsper/SkillWeb/Resources/Prefabs/uGUI/DefaultSkillNodeUGUI.prefab";
    private const string ConnectionPath = "Assets/UI/SkillWeb/SkillTerminalConnection.prefab";
    private const string ConnectionTemplatePath = "Assets/StylishEsper/SkillWeb/Resources/Prefabs/uGUI/DefaultMaskableConnectionLine.prefab";

    [MenuItem("Tools/StillWorking/SkillWeb/Update Terminal Panel")]
    public static void BuildFromMenu() => Debug.Log(Build());

    public static string Build()
    {
        var node = AssetDatabase.LoadAssetAtPath<GameObject>(NodePath);
        var nodeLabel = node.GetComponentInChildren<TMP_Text>(true);
        if (nodeLabel == null || nodeLabel.font == null)
            throw new System.InvalidOperationException("The skill node font is missing.");

        var connection = BuildConnection();

        GameObject root = PrefabUtility.LoadPrefabContents(WebViewPath);
        try
        {
            var view = root.GetComponent<Esper.SkillWeb.UI.UGUI.WebViewUGUI>();
            var serializedView = new SerializedObject(view);
            var connectionPrefabs = serializedView.FindProperty("connectionPrefabs");
            connectionPrefabs.arraySize = Mathf.Max(1, connectionPrefabs.arraySize);
            connectionPrefabs.GetArrayElementAtIndex(0).objectReferenceValue = connection;
            serializedView.ApplyModifiedPropertiesWithoutUndo();
            RectTransform header = Rect(view.content, "TerminalHeader", new Vector2(512f, 82f));
            header.anchorMin = header.anchorMax = header.pivot = Vector2.one;
            header.anchoredPosition = new Vector2(-28f, -24f);
            header.SetAsLastSibling();
            // GraphContent has its own sorting Canvas; keep the fixed controls above panned nodes.
            var headerCanvas = GetOrAdd<Canvas>(header.gameObject);
            var graphCanvas = view.graphContent.GetComponent<Canvas>();
            headerCanvas.overrideSorting = true;
            headerCanvas.sortingOrder = graphCanvas != null ? graphCanvas.sortingOrder + 1 : 3;
            GetOrAdd<UnityEngine.UI.GraphicRaycaster>(header.gameObject);
            var background = GetOrAdd<UnityEngine.UI.Image>(header.gameObject);
            background.color = new Color32(20, 26, 32, 245);
            background.raycastTarget = true;

            RectTransform accent = Rect(header, "Accent", new Vector2(512f, 2f));
            accent.anchoredPosition = Vector2.zero;
            var accentImage = GetOrAdd<UnityEngine.UI.Image>(accent.gameObject);
            accentImage.color = new Color32(178, 141, 76, 255);
            accentImage.raycastTarget = false;

            Label(header, "CivicLabel", "CIVIC RANK", nodeLabel.font, new Vector2(18f, -12f), new Vector2(148f, 22f), 15f, new Color32(176, 185, 191, 255));
            TMP_Text civicValue = Label(header, "CivicValue", "1", nodeLabel.font, new Vector2(18f, -33f), new Vector2(148f, 38f), 32f, new Color32(136, 201, 197, 255));
            Label(header, "PointsLabel", "SKILL POINTS", nodeLabel.font, new Vector2(186f, -12f), new Vector2(176f, 22f), 15f, new Color32(176, 185, 191, 255));
            TMP_Text pointsValue = Label(header, "PointsValue", "0", nodeLabel.font, new Vector2(186f, -33f), new Vector2(176f, 38f), 32f, new Color32(236, 190, 105, 255));

            RectTransform close = Rect(header, "CloseButton", new Vector2(116f, 46f));
            close.anchoredPosition = new Vector2(378f, -19f);
            var closeImage = GetOrAdd<UnityEngine.UI.Image>(close.gameObject);
            closeImage.color = new Color32(57, 65, 72, 255);
            closeImage.raycastTarget = true;
            var closeButton = GetOrAdd<UnityEngine.UI.Button>(close.gameObject);
            closeButton.targetGraphic = closeImage;
            var navigation = closeButton.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.None;
            closeButton.navigation = navigation;
            var colors = closeButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color32(220, 193, 145, 255);
            colors.pressedColor = new Color32(179, 153, 112, 255);
            closeButton.colors = colors;
            TMP_Text closeLabel = Label(close, "Label", "CLOSE  ESC", nodeLabel.font, Vector2.zero, close.sizeDelta, 16f, new Color32(237, 236, 227, 255));
            closeLabel.alignment = TextAlignmentOptions.Center;

            var panel = GetOrAdd<SkillTerminalPanel>(header.gameObject);
            var serializedPanel = new SerializedObject(panel);
            serializedPanel.FindProperty("civicRankValue").objectReferenceValue = civicValue;
            serializedPanel.FindProperty("skillPointsValue").objectReferenceValue = pointsValue;
            serializedPanel.ApplyModifiedPropertiesWithoutUndo();
            for (int i = closeButton.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
                UnityEventTools.RemovePersistentListener(closeButton.onClick, i);
            UnityEventTools.AddPersistentListener(closeButton.onClick, panel.Close);

            PrefabUtility.SaveAsPrefabAsset(root, WebViewPath);
            return "[SkillTerminalUiAuthoring] Civic rank, skill points, close button and Canvas connection lines assigned.";
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static Esper.SkillWeb.UI.UGUI.ConnectionUGUI BuildConnection()
    {
        string source = AssetDatabase.LoadAssetAtPath<GameObject>(ConnectionPath) != null
            ? ConnectionPath : ConnectionTemplatePath;
        GameObject root = PrefabUtility.LoadPrefabContents(source);
        try
        {
            root.name = "SkillTerminalConnection";
            var line = root.GetComponent<Esper.SkillWeb.UI.UGUI.ConnectionLine>();
            if (line == null || line.uiLineRenderer == null)
                throw new System.InvalidOperationException("A Canvas-based connection renderer is required.");
            line.uiLineRenderer.raycastTarget = false;
            line.uiLineRenderer.LineWidth = 3f;
            PrefabUtility.SaveAsPrefabAsset(root, ConnectionPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return AssetDatabase.LoadAssetAtPath<GameObject>(ConnectionPath).GetComponent<Esper.SkillWeb.UI.UGUI.ConnectionUGUI>();
    }

    private static RectTransform Rect(Transform parent, string name, Vector2 size)
    {
        Transform existing = parent.Find(name);
        var rect = existing != null
            ? existing.GetComponent<RectTransform>()
            : new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = size;
        return rect;
    }

    private static TMP_Text Label(Transform parent, string name, string text, TMP_FontAsset font,
        Vector2 position, Vector2 size, float fontSize, Color color)
    {
        RectTransform rect = Rect(parent, name, size);
        rect.anchoredPosition = position;
        var label = GetOrAdd<TextMeshProUGUI>(rect.gameObject);
        label.font = font;
        label.text = text;
        label.fontSize = fontSize;
        label.enableAutoSizing = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.alignment = TextAlignmentOptions.Left;
        label.color = color;
        label.raycastTarget = false;
        return label;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
        => target.TryGetComponent(out T component) ? component : target.AddComponent<T>();
}
