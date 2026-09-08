using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;

namespace StillWorking.EditorAudio
{
    public sealed class AudioBalanceWindow : EditorWindow
    {
        private const string CatalogPath = "Assets/Sfx/Profiles/ClipBalanceCatalog.asset";
        private const string MixerPath = "Assets/SoundController.mixer";
        private const string StylePath = "Assets/Editor/Audio/AudioBalanceWindow.uss";
        private static readonly string[] MixerGroups =
        {
            "Master", "SFX", "Player", "Weapon", "Store", "Music",
            "Inner_Environment", "Outer_Environment"
        };

        [SerializeField] private ClipBalanceCatalog catalog;
        [SerializeField] private int selectedGroupIndex;

        private ObjectField _catalogField;
        private DropdownField _groupField;
        private VisualElement _groupSettings;
        private VisualElement _mixerSettings;
        private FloatField _targetField;
        private FloatField _ceilingField;
        private FloatField _maxBoostField;
        private Label _counts;
        private HelpBox _status;
        private TextField _report;
        private Button _analyzeButton;
        private Button _applyButton;
        private Button _restoreButton;
        private readonly Dictionary<string, Slider> _mixerSliders = new Dictionary<string, Slider>();

        private enum LevelSetting { Target, Ceiling, MaxBoost }

        [MenuItem("Tools/Audio/Audio Balance")]
        public static void Open()
        {
            var window = GetWindow<AudioBalanceWindow>();
            window.titleContent = new GUIContent("Audio Balance");
            window.minSize = new Vector2(600f, 560f);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += RefreshFromAssets;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshFromAssets;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.AddToClassList("audio-balance");
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (stylesheet != null)
                rootVisualElement.styleSheets.Add(stylesheet);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("audio-balance-scroll");
            rootVisualElement.Add(scroll);

            var title = new Label("Audio Balance");
            title.AddToClassList("audio-balance-title");
            scroll.Add(title);
            scroll.Add(new HelpBox(
                "먼저 같은 용도의 클립을 Active RMS와 피크 기준으로 맞춘 뒤, 아래 믹서에서 그룹별 최종 볼륨을 조절합니다. " +
                "기본은 감쇄만 적용하며, 최대 보정 증폭을 지정한 그룹만 피크 상한 내에서 최대 6 dB까지 높일 수 있습니다. " +
                "Active RMS는 무음 구간을 제외한 자체 측정값이며 LUFS가 아닙니다. 원본은 보존하고 보정본을 연결합니다.",
                HelpBoxMessageType.Info));

            if (catalog == null)
                catalog = AssetDatabase.LoadAssetAtPath<ClipBalanceCatalog>(CatalogPath);
            _catalogField = new ObjectField("클립 목록")
            {
                objectType = typeof(ClipBalanceCatalog),
                allowSceneObjects = false,
                value = catalog
            };
            _catalogField.RegisterValueChangedCallback(evt =>
            {
                catalog = evt.newValue as ClipBalanceCatalog;
                selectedGroupIndex = 0;
                _report.SetValueWithoutNotify(string.Empty);
                RefreshGroupList();
            });
            scroll.Add(_catalogField);

            var navigation = new VisualElement();
            navigation.AddToClassList("audio-balance-actions");
            navigation.Add(new Button(() =>
            {
                if (catalog == null)
                {
                    SetStatus("클립 목록 에셋을 지정하세요.", HelpBoxMessageType.Warning);
                    return;
                }
                Selection.activeObject = catalog;
                EditorGUIUtility.PingObject(catalog);
            }) { text = "목록 선택 · Inspector에서 편집" });
            navigation.Add(new Button(OpenMixer) { text = "Audio Mixer 열기" });
            scroll.Add(navigation);

            var mixerFoldout = new Foldout { text = "그룹별 최종 볼륨 (dB)", value = true };
            mixerFoldout.AddToClassList("audio-balance-section");
            _mixerSettings = new VisualElement();
            _mixerSettings.AddToClassList("audio-balance-mixer");
            _mixerSliders.Clear();
            foreach (var groupName in MixerGroups)
            {
                var slider = new Slider(groupName, -60f, 0f) { showInputField = true };
                slider.AddToClassList("audio-balance-mixer-slider");
                slider.RegisterValueChangedCallback(evt => ChangeMixerVolume(groupName, evt.newValue));
                _mixerSliders.Add(groupName, slider);
                _mixerSettings.Add(slider);
            }
            mixerFoldout.Add(_mixerSettings);
            var mixerHelp = new Label("0 dB는 추가 증폭 없음입니다. 이 창에서는 과도한 증폭을 막기 위해 +값을 설정하지 않습니다.");
            mixerHelp.AddToClassList("audio-balance-description");
            mixerFoldout.Add(mixerHelp);
            scroll.Add(mixerFoldout);

            _groupField = new DropdownField("클립 그룹");
            _groupField.RegisterValueChangedCallback(_ =>
            {
                selectedGroupIndex = _groupField.index;
                _report.SetValueWithoutNotify(string.Empty);
                RefreshSelectedGroup();
            });
            scroll.Add(_groupField);

            _counts = new Label();
            _counts.AddToClassList("audio-balance-description");
            scroll.Add(_counts);
            _groupSettings = new VisualElement();
            _targetField = new FloatField("목표 Active RMS (dBFS)") { isDelayed = true };
            _ceilingField = new FloatField("샘플 피크 상한 (dBFS)") { isDelayed = true };
            _maxBoostField = new FloatField("최대 보정 증폭 (dB)") { isDelayed = true };
            _targetField.RegisterValueChangedCallback(evt => ChangeGroupLevel(LevelSetting.Target, evt.newValue));
            _ceilingField.RegisterValueChangedCallback(evt => ChangeGroupLevel(LevelSetting.Ceiling, evt.newValue));
            _maxBoostField.RegisterValueChangedCallback(evt => ChangeGroupLevel(LevelSetting.MaxBoost, evt.newValue));
            _groupSettings.Add(_targetField);
            _groupSettings.Add(_ceilingField);
            _groupSettings.Add(_maxBoostField);
            scroll.Add(_groupSettings);

            var actions = new VisualElement();
            actions.AddToClassList("audio-balance-actions");
            _analyzeButton = new Button(() => RunOperation(false,
                group => ClipBalanceProcessor.AnalyzeGroup(group))) { text = "클립 음량 분석" };
            _applyButton = new Button(() => RunOperation(true,
                group => ClipBalanceProcessor.ApplyGroup(catalog, group))) { text = "보정본 생성 · 연결" };
            _restoreButton = new Button(() => RunOperation(true,
                group => ClipBalanceProcessor.RestoreGroup(catalog, group))) { text = "원본 연결 복원" };
            actions.Add(_analyzeButton);
            actions.Add(_applyButton);
            actions.Add(_restoreButton);
            scroll.Add(actions);

            _status = new HelpBox("그룹을 선택해 분석하면 클립별 차이와 적용할 감쇄량을 확인할 수 있습니다.", HelpBoxMessageType.Info);
            scroll.Add(_status);
            _report = new TextField("측정 · 적용 결과") { multiline = true, isReadOnly = true };
            _report.AddToClassList("audio-balance-report");
            scroll.Add(_report);

            RefreshGroupList();
            RefreshMixerValues();
        }

        private void OnProjectChange()
        {
            if (_catalogField == null)
                return;
            if (catalog == null)
            {
                catalog = AssetDatabase.LoadAssetAtPath<ClipBalanceCatalog>(CatalogPath);
                _catalogField.SetValueWithoutNotify(catalog);
            }
            RefreshFromAssets();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            UpdateEnabledState();
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                RefreshMixerValues();
        }

        private void RefreshFromAssets()
        {
            if (_groupField == null)
                return;
            RefreshGroupList();
            RefreshMixerValues();
        }

        private ClipBalanceGroup SelectedGroup
        {
            get
            {
                if (catalog == null || catalog.groups == null ||
                    selectedGroupIndex < 0 || selectedGroupIndex >= catalog.groups.Count)
                    return null;
                return catalog.groups[selectedGroupIndex];
            }
        }

        private void RefreshGroupList()
        {
            var choices = new List<string>();
            if (catalog != null && catalog.groups != null)
            {
                for (var index = 0; index < catalog.groups.Count; index++)
                {
                    var group = catalog.groups[index];
                    choices.Add($"{index + 1}. {(group == null ? "비어 있는 그룹" : group.name)}");
                }
            }
            _groupField.choices = choices;
            if (choices.Count > 0)
            {
                selectedGroupIndex = Mathf.Clamp(selectedGroupIndex, 0, choices.Count - 1);
                _groupField.SetValueWithoutNotify(choices[selectedGroupIndex]);
            }
            else
            {
                selectedGroupIndex = 0;
                _groupField.SetValueWithoutNotify(string.Empty);
            }
            RefreshSelectedGroup();
        }

        private void RefreshSelectedGroup()
        {
            var group = SelectedGroup;
            _counts.text = group == null
                ? "Inspector에서 같은 용도의 클립과 연결 대상 에셋을 그룹에 등록하세요."
                : $"등록 클립 {group.clips?.Count ?? 0}개 · 연결 대상 에셋 {group.bindingAssets?.Count ?? 0}개";
            _targetField.SetValueWithoutNotify(group == null ? -30f : group.targetActiveRmsDb);
            _ceilingField.SetValueWithoutNotify(group == null ? -6f : group.peakCeilingDb);
            _maxBoostField.SetValueWithoutNotify(group == null ? 0f : group.maxBoostDb);
            UpdateEnabledState();
        }

        private void UpdateEnabledState()
        {
            if (_applyButton == null)
                return;
            var hasGroup = SelectedGroup != null;
            var canEdit = !EditorApplication.isPlayingOrWillChangePlaymode;
            _groupField.SetEnabled(catalog != null && catalog.groups != null && catalog.groups.Count > 0);
            _groupSettings.SetEnabled(hasGroup && canEdit);
            _mixerSettings.SetEnabled(canEdit);
            _analyzeButton.SetEnabled(hasGroup);
            _applyButton.SetEnabled(hasGroup && canEdit);
            _restoreButton.SetEnabled(hasGroup && canEdit);
        }

        private void ChangeGroupLevel(LevelSetting setting, float value)
        {
            var group = SelectedGroup;
            if (group == null)
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RefreshSelectedGroup();
                SetStatus("재생을 종료한 뒤 에셋 설정을 변경할 수 있습니다.", HelpBoxMessageType.Warning);
                return;
            }
            var minimum = setting == LevelSetting.MaxBoost ? 0f : -80f;
            var maximum = setting == LevelSetting.MaxBoost ? 6f : 0f;
            if (float.IsNaN(value) || float.IsInfinity(value) || value < minimum || value > maximum)
            {
                RefreshSelectedGroup();
                SetStatus($"{minimum:0} ~ {maximum:0} 범위의 유한한 값을 입력하세요.", HelpBoxMessageType.Warning);
                return;
            }
            Undo.RecordObject(catalog, "Change Clip Balance Level");
            if (setting == LevelSetting.Target)
                group.targetActiveRmsDb = value;
            else if (setting == LevelSetting.Ceiling)
                group.peakCeilingDb = value;
            else
                group.maxBoostDb = value;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssetIfDirty(catalog);
            SetStatus("기준값을 저장했습니다. 분석 후 보정본을 생성하면 클립 연결에 적용됩니다.", HelpBoxMessageType.Info);
        }

        private void RunOperation(bool modifiesAssets, Func<ClipBalanceGroup, string> operation)
        {
            var group = SelectedGroup;
            if (group == null)
            {
                SetStatus("클립 그룹을 선택하세요.", HelpBoxMessageType.Warning);
                return;
            }
            if (modifiesAssets && EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SetStatus("재생을 종료한 뒤 보정 또는 복원을 실행할 수 있습니다.", HelpBoxMessageType.Warning);
                return;
            }
            try
            {
                _report.SetValueWithoutNotify(operation(group) ?? string.Empty);
                RefreshSelectedGroup();
                SetStatus(modifiesAssets ? "작업을 마쳤습니다. 아래 결과를 확인하세요." : "분석을 마쳤습니다. 아래 클립별 측정값을 확인하세요.", HelpBoxMessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, HelpBoxMessageType.Error);
            }
        }

        private void OpenMixer()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null || !AssetDatabase.OpenAsset(mixer))
                SetStatus("SoundController 믹서를 열 수 없습니다. 에셋 경로를 확인하세요.", HelpBoxMessageType.Error);
        }

        private void RefreshMixerValues()
        {
            if (_status == null)
                return;
            try
            {
                foreach (var pair in _mixerSliders)
                {
                    var volume = AudioBalanceMixer.GetVolume(pair.Key);
                    pair.Value.label = volume > 0f ? $"{pair.Key} (현재 +{volume:0.0} dB)" : pair.Key;
                    pair.Value.SetValueWithoutNotify(Mathf.Clamp(volume, -60f, 0f));
                    pair.Value.tooltip = $"저장된 음량: {volume:0.0} dB";
                }
            }
            catch (Exception exception)
            {
                SetStatus(exception.Message, HelpBoxMessageType.Error);
            }
        }

        private void ChangeMixerVolume(string groupName, float value)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                RefreshMixerValues();
                SetStatus("재생을 종료한 뒤 저장할 믹서 음량을 변경할 수 있습니다.", HelpBoxMessageType.Warning);
                return;
            }
            try
            {
                AudioBalanceMixer.SetVolume(groupName, Mathf.Clamp(value, -60f, 0f));
                RefreshMixerValues();
                SetStatus($"{groupName} 그룹의 음량을 저장했습니다.", HelpBoxMessageType.Info);
            }
            catch (Exception exception)
            {
                RefreshMixerValues();
                SetStatus(exception.Message, HelpBoxMessageType.Error);
            }
        }

        private void SetStatus(string message, HelpBoxMessageType messageType)
        {
            if (_status == null)
                return;
            _status.text = message;
            _status.messageType = messageType;
        }
    }
}
