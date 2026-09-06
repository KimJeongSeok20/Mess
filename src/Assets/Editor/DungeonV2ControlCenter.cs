using System;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Single production-facing entry point for the NewPrison V2 authoring pipeline.
/// Older helpers remain callable from code where V2 still uses them, but they no
/// longer add competing menu items.
/// </summary>
public sealed class DungeonV2ControlCenter : EditorWindow
{
    private const string WindowTitle = "Dungeon V2 Control Center";
    private const string HandoffPath = "Docs/DungeonV2_CurrentBaseline_Handoff.md";
    private const string NextSessionPrompt =
        HandoffPath + "를 먼저 읽고 " +
        "현재 Dungeon V2 baseline을 그대로 이어서 작업해줘. 시작할 때 " +
        "DungeonV2WorkspaceValidation.ValidateAllCli()로 read-only 검증하고, " +
        "기존 V1/legacy NavMesh 실험 경로를 생산 경로로 되살리지 마.";

    private Vector2 scroll;
    private bool showAdvanced;
    private string lastResult = "아직 실행한 작업이 없습니다.";

    [MenuItem("Tools/Dungeon V2/Control Center", false, 0)]
    public static void Open()
    {
        DungeonV2ControlCenter window = GetWindow<DungeonV2ControlCenter>(WindowTitle);
        window.minSize = new Vector2(610f, 640f);
        window.Show();
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("현재 생산 기준", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Tile_modified는 수동 저작 원본이고, test/V2_*는 회전 베이크 출력입니다. " +
            "생성 던전은 New_Prison_V2_Flow를 사용하며 NavMesh는 생성 완료 후 " +
            "DungeonRuntimeNavMeshPipeline이 한 번 구축합니다.",
            MessageType.Info);

        EditorGUILayout.Space(4f);
        if (GUILayout.Button("전체 V2 기준 검증 (Read-only)", GUILayout.Height(34f)))
            RunReadOnly(DungeonV2WorkspaceValidation.ValidateAllCli);

        if (GUILayout.Button("회전 베이크 도구 V2 열기", GUILayout.Height(30f)))
            DungeonTileRotationBakeToolV2Window.OpenWindow();

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("설정 동기화", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "BakeData, LightingSet, Probe/Lightmap 값은 보존하고 계층과 일반 컴포넌트 설정만 동기화합니다.",
            MessageType.None);

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || Lightmapping.isRunning))
        {
            if (GUILayout.Button("선택한 Tile_modified 방 동기화"))
            {
                string selectedPath = AssetDatabase.GetAssetPath(Selection.activeObject);
                RunMutation(
                    "선택한 방 동기화",
                    string.IsNullOrWhiteSpace(selectedPath) ? "선택된 에셋이 없습니다." : selectedPath,
                    () => DungeonV2NonBakeSettingsSync.SyncRoomCli(selectedPath));
            }

            if (GUILayout.Button("StartRoom + Administrative 동기화"))
            {
                RunMutation(
                    "두 방의 비베이크 설정 동기화",
                    "StartRoom과 AdminstrativeSegregation의 V2 출력에 일반 설정을 반영합니다.",
                    DungeonV2NonBakeSettingsSync.SyncStartRoomAndAdministrativeCli);
            }

            if (GUILayout.Button("현재 존재하는 모든 V2 방 동기화"))
            {
                RunMutation(
                    "모든 V2 방 동기화",
                    "Tile_modified 중 이미 V2 출력이 존재하는 모든 방을 동기화합니다.",
                    DungeonV2NonBakeSettingsSync.SyncAllExistingTestRoomsCli);
            }
        }

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("생산 Dungeon / NavMesh", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying || Lightmapping.isRunning))
        {
            if (GUILayout.Button("V2 전용 생산 Dungeon Flow 갱신"))
            {
                RunMutation(
                    "생산 Dungeon Flow 갱신",
                    "현재 V2 방 출력으로 TileSet, Archetype, Flow, DungeonMapList 연결을 갱신합니다.",
                    DungeonV2ProductionFlowAuthoring.ApplyCli);
            }

            if (GUILayout.Button("NavMesh / Door / Monster 기준 적용"))
            {
                RunMutation(
                    "Navigation 기준 적용",
                    "V2 방, 문, 몬스터 프리팹에 현재 navigation baseline을 적용합니다.",
                    DungeonV2NavigationPrefabAuthoring.ApplyCli);
            }

            if (GUILayout.Button("Administrative 내부문 18개 다시 저작"))
            {
                RunMutation(
                    "Administrative 내부문 저작",
                    "감방문 16개, 경비실 문 1개, 내부 철문 1개의 통과 영역과 몬스터 바인딩을 갱신합니다.",
                    DungeonInternalDoorAuthoring.AuthorAdministrativeInternalDoorsCli);
            }
        }

        if (GUILayout.Button("Administrative 내부문 검증 (Read-only)"))
            RunReadOnly(DungeonInternalDoorAuthoring.ValidateAdministrativeInternalDoorsCli);

        EditorGUILayout.Space(10f);
        EditorGUILayout.LabelField("새 세션 인계", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("기준 문서 선택"))
            SelectHandoffDocument();
        if (GUILayout.Button("다음 세션 프롬프트 복사"))
        {
            EditorGUIUtility.systemCopyBuffer = NextSessionPrompt;
            lastResult = "다음 세션 프롬프트를 클립보드에 복사했습니다.\n\n" + NextSessionPrompt;
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8f);
        showAdvanced = EditorGUILayout.Foldout(showAdvanced, "고급 진단 / 레거시 상태", true);
        if (showAdvanced)
        {
            EditorGUILayout.HelpBox(
                "DungeonTileRotationBakeTool(V1)은 V2의 내부 베이크 백엔드이므로 삭제하지 않습니다. " +
                "DoorNavLink는 Assets/Map/Door_prefab 1.prefab이 아직 참조하여 보존합니다. " +
                "이 둘은 새 생산 작업의 직접 진입점이 아닙니다.",
                MessageType.Warning);
            if (GUILayout.Button("Runtime NavMesh Agent Viewer 열기"))
                RuntimeNavMeshAgentTypeDebugWindow.Open();
        }

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("최근 결과", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(lastResult, GUILayout.MinHeight(180f));

        EditorGUILayout.EndScrollView();
    }

    private void RunReadOnly(Func<string> action)
    {
        try
        {
            lastResult = action();
            Debug.Log("[DungeonV2ControlCenter]\n" + lastResult);
        }
        catch (Exception exception)
        {
            lastResult = "FAIL: " + exception;
            Debug.LogException(exception);
        }
    }

    private void RunMutation(string title, string description, Func<string> action)
    {
        if (!EditorUtility.DisplayDialog(title, description, "실행", "취소"))
            return;

        RunReadOnly(action);
    }

    private void SelectHandoffDocument()
    {
        UnityEngine.Object document = AssetDatabase.LoadMainAssetAtPath(HandoffPath);
        if (document == null)
        {
            lastResult = "FAIL: 기준 문서를 찾을 수 없습니다: " + HandoffPath;
            return;
        }

        Selection.activeObject = document;
        EditorGUIUtility.PingObject(document);
        lastResult = "기준 문서를 선택했습니다: " + HandoffPath;
    }
}

/// <summary>
/// Read-only validation entry point for interactive use and batch/next-session use.
/// </summary>
public static class DungeonV2WorkspaceValidation
{
    private static readonly string[] V2SourceRooms =
    {
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/StartRoom.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/AdminstrativeSegregation.prefab",
        "Assets/Prefabs/map_piece/NewPrison/Tile_modified/OfficerRoom.prefab",
    };

    [Serializable]
    private sealed class RotationValidationResult
    {
        public bool completed;
        public string tile;
        public string failure;
    }

    public static void ValidateAllBatchCli()
    {
        string result = ValidateAllCli();
        Debug.Log("[DungeonV2WorkspaceValidation]\n" + result);
        if (!result.StartsWith("PASS", StringComparison.Ordinal))
            EditorApplication.Exit(1);
    }

    public static string ValidateAllCli()
    {
        var report = new StringBuilder();
        bool passed = true;

        report.AppendLine("Dungeon V2 baseline validation");
        for (int i = 0; i < V2SourceRooms.Length; i++)
        {
            string json = BAKEROTATETOOLV2.ValidateRoomCli(V2SourceRooms[i]);
            RotationValidationResult result = JsonUtility.FromJson<RotationValidationResult>(json);
            bool roomPassed = result != null && result.completed;
            passed &= roomPassed;
            report.Append("- Rotation output ")
                .Append(result != null && !string.IsNullOrWhiteSpace(result.tile) ? result.tile : V2SourceRooms[i])
                .Append(": ")
                .AppendLine(roomPassed ? "PASS" : "FAIL");
            if (!roomPassed)
                report.AppendLine(result == null ? json : result.failure);
        }

        AppendValidation(
            report,
            "V2 production flow",
            DungeonV2ProductionFlowAuthoring.ValidateCli(),
            ref passed);
        AppendValidation(
            report,
            "Navigation prefabs",
            DungeonV2NavigationPrefabAuthoring.ValidateCli(),
            ref passed);
        AppendValidation(
            report,
            "Administrative internal doors",
            DungeonInternalDoorAuthoring.ValidateAdministrativeInternalDoorsCli(),
            ref passed);

        report.AppendLine("- Runtime NavMesh owner: DungeonRuntimeNavMeshPipeline");
        report.AppendLine("- Authoring areas: DungeonNavMeshArea + DungeonNavMeshFootprintBlocker");
        report.AppendLine("- Legacy retained dependency: DoorNavLink on Assets/Map/Door_prefab 1.prefab (not production entry point)");

        return (passed ? "PASS" : "FAIL") + "\n" + report;
    }

    private static void AppendValidation(StringBuilder report, string label, string result, ref bool passed)
    {
        bool sectionPassed = !string.IsNullOrWhiteSpace(result) &&
                             result.StartsWith("PASS", StringComparison.Ordinal);
        passed &= sectionPassed;
        report.Append("- ").Append(label).Append(": ").AppendLine(sectionPassed ? "PASS" : "FAIL");
        if (!sectionPassed)
            report.AppendLine(result);
    }
}
