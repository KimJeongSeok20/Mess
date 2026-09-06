using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DungeonAdjacentLightingPoC
{
    public sealed class DungeonAdjacentLightingPoCPreviewController : MonoBehaviour
    {
        [SerializeField] private bool showAdjacentLightmap = true;
        [SerializeField] private DungeonTileLightmapSwitcher startLighting;
        [SerializeField] private DungeonTileLightmapSwitcher adminLighting;

        private DungeonAdjacentLightmapReceiver[] receivers;
        private DungeonAdjacentLightingPoCStartMapEnvironmentController startMapEnvironment;
        [SerializeField]
        private DungeonAdjacentLightingPoCStartMapRuntimeValidationController startMapRuntimeValidation;
        private Text runtimeHudText;

        public void Configure(
            DungeonTileLightmapSwitcher startRoomLighting,
            DungeonTileLightmapSwitcher administrativeRoomLighting)
        {
            startLighting = startRoomLighting;
            adminLighting = administrativeRoomLighting;
        }

        public void ConfigureStartMapRuntimeValidation(
            DungeonAdjacentLightingPoCStartMapRuntimeValidationController runtimeValidation)
        {
            startMapRuntimeValidation = runtimeValidation;
        }

        private void Start()
        {
            RefreshReceivers();
            startMapEnvironment = GetComponent<DungeonAdjacentLightingPoCStartMapEnvironmentController>();
            ApplyPowerCombination(
                DungeonTileLightmapSwitcher.PowerLevel.P100,
                DungeonTileLightmapSwitcher.PowerLevel.P100);
            ApplyMode();
            CreateRuntimeHud();
            UpdateRuntimeHud();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
                SetMode(false);
            if (Input.GetKeyDown(KeyCode.Alpha2))
                SetMode(true);
            if (Input.GetKeyDown(KeyCode.Space))
                SetMode(!showAdjacentLightmap);
            if (Input.GetKeyDown(KeyCode.Alpha3))
                ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P100,
                    DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (Input.GetKeyDown(KeyCode.Alpha4))
                ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P100,
                    DungeonTileLightmapSwitcher.PowerLevel.P0);
            if (Input.GetKeyDown(KeyCode.Alpha5))
                ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P0,
                    DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (Input.GetKeyDown(KeyCode.Alpha6))
                ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P0,
                    DungeonTileLightmapSwitcher.PowerLevel.P0);

            // Receivers can refresh their property blocks when power/door state changes.
            // Re-assert the preview choice so the Game-view comparison remains deterministic.
            ApplyMode();
            UpdateRuntimeHud();
        }

        private void OnGUI()
        {
            if (runtimeHudText != null)
                return;

            GUI.depth = -1000;
            GUIStyle title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            GUIStyle body = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                normal = { textColor = Color.white }
            };

            GUILayout.BeginArea(new Rect(20f, 20f, 700f, 435f), GUI.skin.box);
            GUILayout.Label(
                showAdjacentLightmap ? "ADJACENT LIGHTMAP: ON" : "BASELINE: PRIMARY LIGHTMAP ONLY",
                title);
            GUILayout.Label(
                $"POWER  Start={GetPowerLabel(startLighting)}  Admin={GetPowerLabel(adminLighting)}",
                title);
            GUILayout.Label(
                $"ENV  {GetEnvironmentLabel()}",
                body);
            GUILayout.Label(
                $"Ambient={RenderSettings.ambientMode} " +
                $"RGB={RenderSettings.ambientLight.r:F2}/{RenderSettings.ambientLight.g:F2}/{RenderSettings.ambientLight.b:F2} " +
                $"Fog={(RenderSettings.fog ? "ON" : "OFF")}",
                body);
            GUILayout.Label(
                $"Volume={GetVolumeLabel()}  DungeonSpot={GetDungeonLightLabel()}",
                body);
            if (startMapRuntimeValidation != null)
            {
                GUILayout.Label(
                    $"STARTMAP RUNTIME: {startMapRuntimeValidation.Status}  " +
                    $"GeneratedRooms={startMapRuntimeValidation.GeneratedRoomCount}",
                    body);
            }
            GUILayout.Label("1 = Baseline    2 = Adjacent    Space = Toggle", body);
            GUILayout.Label(
                "3 = P100/P100    4 = P100/P0    5 = P0/P100    6 = P0/P0",
                body);
            GUILayout.Label("WASD = Move    Mouse = Look    Shift = Run    R = Reset", body);
            GUILayout.Label("Esc = Release cursor    Left click = Capture cursor", body);
            GUILayout.Label($"Receivers: {(receivers == null ? 0 : receivers.Length)}", body);
            GUILayout.Space(8f);
            if (GUILayout.Button("SHOW BASELINE (1)", GUILayout.Height(32f)))
                SetMode(false);
            if (GUILayout.Button("SHOW ADJACENT LIGHTMAP (2)", GUILayout.Height(32f)))
                SetMode(true);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("P100 / P100 (3)", GUILayout.Height(32f)))
                ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P100,
                    DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (GUILayout.Button("P100 / P0 (4)", GUILayout.Height(32f)))
                ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P100,
                    DungeonTileLightmapSwitcher.PowerLevel.P0);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("P0 / P100 (5)", GUILayout.Height(32f)))
                ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P0,
                    DungeonTileLightmapSwitcher.PowerLevel.P100);
            if (GUILayout.Button("P0 / P0 (6)", GUILayout.Height(32f)))
                ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P0,
                    DungeonTileLightmapSwitcher.PowerLevel.P0);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ApplyPowerCombination(
            DungeonTileLightmapSwitcher.PowerLevel startPower,
            DungeonTileLightmapSwitcher.PowerLevel adminPower)
        {
            if (startLighting != null)
                startLighting.SetPowerLevel(startPower);
            if (adminLighting != null)
                adminLighting.SetPowerLevel(adminPower);
            ApplyMode();
        }

        public void ApplyValidationState(
            DungeonTileLightmapSwitcher.PowerLevel startPower,
            DungeonTileLightmapSwitcher.PowerLevel adminPower,
            bool adjacentEnabled)
        {
            ApplyPowerCombination(startPower, adminPower);
            SetMode(adjacentEnabled);
        }

        public void RefreshReceivers()
        {
            receivers = FindObjectsByType<DungeonAdjacentLightmapReceiver>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        }

        private static string GetPowerLabel(DungeonTileLightmapSwitcher lighting)
        {
            return lighting != null ? lighting.CurrentPowerLevel.ToString() : "MISSING";
        }

        private string GetVolumeLabel()
        {
            if (startMapRuntimeValidation != null)
            {
                return $"{startMapRuntimeValidation.VolumeProfileName}/" +
                       (startMapRuntimeValidation.VolumeEnabled ? "ON" : "OFF");
            }
            if (startMapEnvironment == null)
                return "MISSING";
            return $"{startMapEnvironment.VolumeProfileName}/" +
                   (startMapEnvironment.VolumeEnabled ? "ON" : "OFF");
        }

        private string GetDungeonLightLabel()
        {
            if (startMapRuntimeValidation != null)
                return $"PLAYER LIGHTS OFF ({startMapRuntimeValidation.DisabledPlayerLightCount})";
            if (startMapEnvironment == null)
                return "MISSING";
            return startMapEnvironment.DungeonOnlyLightEnabled ? "ON" : "OFF";
        }

        private string GetEnvironmentLabel()
        {
            if (startMapRuntimeValidation != null)
            {
                return startMapRuntimeValidation.IsActualDungeonEnvironmentApplied
                    ? "ACTUAL STARTMAP DUNGEON ENTRY: APPLIED"
                    : "ACTUAL STARTMAP DUNGEON ENTRY: WAITING";
            }

            return startMapEnvironment != null && startMapEnvironment.IsApplied
                ? "STARTMAP-COPIED DUNGEON ENTRY: APPLIED"
                : "STARTMAP-COPIED DUNGEON ENTRY: MISSING";
        }

        private void CreateRuntimeHud()
        {
            if (runtimeHudText != null)
                return;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            GameObject canvasObject = new GameObject("AdjacentLightmapValidationHUD");
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = short.MaxValue;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0f;
            canvasObject.AddComponent<GraphicRaycaster>();

            GameObject panelObject = new GameObject("Panel");
            panelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform panelRect = panelObject.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(20f, -20f);
            panelRect.sizeDelta = new Vector2(760f, 500f);
            Image panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.025f, 0.035f, 0.045f, 0.92f);

            GameObject textObject = new GameObject("Status");
            textObject.transform.SetParent(panelObject.transform, false);
            RectTransform textRect = textObject.AddComponent<RectTransform>();
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(18f, -14f);
            textRect.sizeDelta = new Vector2(724f, 285f);
            runtimeHudText = textObject.AddComponent<Text>();
            runtimeHudText.font = font;
            runtimeHudText.fontSize = 20;
            runtimeHudText.lineSpacing = 1.12f;
            runtimeHudText.alignment = TextAnchor.UpperLeft;
            runtimeHudText.color = Color.white;
            runtimeHudText.raycastTarget = false;

            CreateHudButton(panelObject.transform, font, "BASELINE (1)", new Vector2(18f, -310f),
                () => SetMode(false));
            CreateHudButton(panelObject.transform, font, "ADJACENT ON (2)", new Vector2(255f, -310f),
                () => SetMode(true));
            CreateHudButton(panelObject.transform, font, "P100 / P100 (3)", new Vector2(18f, -357f),
                () => ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P100,
                    DungeonTileLightmapSwitcher.PowerLevel.P100));
            CreateHudButton(panelObject.transform, font, "P100 / P0 (4)", new Vector2(255f, -357f),
                () => ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P100,
                    DungeonTileLightmapSwitcher.PowerLevel.P0));
            CreateHudButton(panelObject.transform, font, "P0 / P100 (5)", new Vector2(18f, -404f),
                () => ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P0,
                    DungeonTileLightmapSwitcher.PowerLevel.P100));
            CreateHudButton(panelObject.transform, font, "P0 / P0 (6)", new Vector2(255f, -404f),
                () => ApplyPowerCombination(
                    DungeonTileLightmapSwitcher.PowerLevel.P0,
                    DungeonTileLightmapSwitcher.PowerLevel.P0));
        }

        private static void CreateHudButton(
            Transform parent,
            Font font,
            string label,
            Vector2 anchoredPosition,
            UnityAction action)
        {
            GameObject buttonObject = new GameObject(label);
            buttonObject.transform.SetParent(parent, false);
            RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0f, 1f);
            buttonRect.anchorMax = new Vector2(0f, 1f);
            buttonRect.pivot = new Vector2(0f, 1f);
            buttonRect.anchoredPosition = anchoredPosition;
            buttonRect.sizeDelta = new Vector2(220f, 38f);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.17f, 0.21f, 0.98f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.18f, 0.34f, 0.43f, 1f);
            colors.pressedColor = new Color(0.08f, 0.50f, 0.70f, 1f);
            button.colors = colors;
            button.onClick.AddListener(action);

            GameObject labelObject = new GameObject("Label");
            labelObject.transform.SetParent(buttonObject.transform, false);
            RectTransform labelRect = labelObject.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            Text text = labelObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = 18;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = label;
        }

        private void UpdateRuntimeHud()
        {
            if (runtimeHudText == null)
                return;

            string mode = showAdjacentLightmap
                ? "ADJACENT LIGHTMAP: ON"
                : "BASELINE: PRIMARY LIGHTMAP ONLY";
            string runtimeStatus = startMapRuntimeValidation != null
                ? $"{startMapRuntimeValidation.Status}  Rooms={startMapRuntimeValidation.GeneratedRoomCount}"
                : "ISOLATED PREVIEW";
            runtimeHudText.text =
                $"{mode}\n" +
                $"POWER  Start={GetPowerLabel(startLighting)}  Admin={GetPowerLabel(adminLighting)}\n" +
                $"ENV  {GetEnvironmentLabel()}\n" +
                $"Ambient={RenderSettings.ambientMode}  " +
                $"RGB={RenderSettings.ambientLight.r:F2}/{RenderSettings.ambientLight.g:F2}/{RenderSettings.ambientLight.b:F2}  " +
                $"Fog={(RenderSettings.fog ? "ON" : "OFF")}\n" +
                $"Volume={GetVolumeLabel()}  DungeonSpot={GetDungeonLightLabel()}\n" +
                $"RUNTIME  {runtimeStatus}\n" +
                $"Receivers={(receivers == null ? 0 : receivers.Length)}  " +
                "1/2=Baseline/Adjacent  3-6=Power states  Esc=Mouse";
        }

        private void SetMode(bool enabled)
        {
            showAdjacentLightmap = enabled;
            ApplyMode();
        }

        private void ApplyMode()
        {
            if (receivers == null)
                return;

            for (int i = 0; i < receivers.Length; i++)
            {
                if (receivers[i] != null)
                    receivers[i].SetVisualizationEnabled(showAdjacentLightmap);
            }
        }
    }
}
