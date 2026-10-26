using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using UnityAR.Ar;
using UnityAR.Core;
using UnityAR.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace UnityAR.EditorTools
{
    /// <summary>
    /// 一键生成「现场巡检问题标注」的完整 AR 场景。
    ///
    /// 为什么用脚本生成场景而不是提交一个 .unity 文件：
    /// 场景文件是 Unity 序列化出来的 YAML，手写极易出错，而且本项目在编写时
    /// 没有可用的 Unity 编辑器来校验。改成脚本生成的好处是：
    ///   1. 打开工程后点一下菜单就能得到一个必然可用的场景；
    ///   2. 场景结构在代码里一目了然，便于评审；
    ///   3. 不提交未经校验的二进制/序列化资产。
    ///
    /// 使用方法：Unity 菜单栏 → Tools → AR 巡检 → 生成巡检场景
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/ARInspect.unity";

        [MenuItem("Tools/AR 巡检/生成巡检场景", false, 1)]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---------- 相机 ----------
            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.nearClipPlane = 0.1f;
            cameraGo.AddComponent<UnityEngine.XR.ARFoundation.ARCameraManager>();
            cameraGo.AddComponent<UnityEngine.XR.ARFoundation.ARCameraBackground>();

            // ---------- AR 会话 ----------
            var sessionGo = new GameObject("AR Session",
                typeof(UnityEngine.XR.ARFoundation.ARSession),
                typeof(UnityEngine.XR.ARFoundation.ARPlaneManager),
                typeof(UnityEngine.XR.ARFoundation.ARRaycastManager),
                typeof(ArSessionController));

            // ---------- 标记根节点 ----------
            var markersGo = new GameObject("Markers");

            // ---------- 放置控制 ----------
            var placementGo = new GameObject("AR Placement", typeof(PlacementController));

            // ---------- 全局服务 ----------
            var appGo = new GameObject("App", typeof(BackendSettings), typeof(ApiClient));

            // ---------- UI ----------
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;

            // 顶部状态条
            var statusBar = CreatePanel("StatusBar", canvasGo.transform, new Color(0f, 0f, 0f, 0.55f));
            SetAnchors(statusBar, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -140f), new Vector2(0f, 0f));
            var arStatusText = CreateText("ARStatusText", statusBar, "AR 正在启动…", 30, TextAlignmentOptions.TopLeft);
            SetAnchors(arStatusText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20f, 52f), new Vector2(-20f, -10f));
            var hintText = CreateText("HintText", statusBar, "请缓慢移动手机扫描地面", 26, TextAlignmentOptions.BottomLeft);
            SetAnchors(hintText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(20f, 8f), new Vector2(-20f, -54f));

            // 填写面板
            var formPanel = CreatePanel("FormPanel", canvasGo.transform, new Color(0.10f, 0.10f, 0.12f, 0.92f));
            SetAnchors(formPanel, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 760f));

            var titleLabel = CreateText("TitleLabel", formPanel, "问题标题（必填）", 28, TextAlignmentOptions.Left);
            SetAnchors(titleLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -60f), new Vector2(-30f, -16f));

            var titleInput = CreateInputField("TitleInput", formPanel, "例如：入口墙面破损");
            SetAnchors(titleInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -140f), new Vector2(-30f, -66f));

            var descLabel = CreateText("DescLabel", formPanel, "问题描述（可选）", 28, TextAlignmentOptions.Left);
            SetAnchors(descLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -184f), new Vector2(-30f, -146f));

            var descInput = CreateInputField("DescInput", formPanel, "例如：左侧墙体存在裂缝");
            SetAnchors(descInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -264f), new Vector2(-30f, -190f));

            var priorityLabel = CreateText("PriorityLabel", formPanel, "优先级", 28, TextAlignmentOptions.Left);
            SetAnchors(priorityLabel.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -308f), new Vector2(-30f, -270f));

            var priorityButtons = new Button[3];
            var names = IssuePriorities.DisplayNames();
            for (var i = 0; i < 3; i++)
            {
                var button = CreateButton("PriorityButton" + IssuePriorities.All[i], formPanel, names[i]);
                var left = 30f + i * 190f;
                SetAnchors(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(left, -390f), new Vector2(left + 170f, -314f));
                priorityButtons[i] = button;
            }

            var positionText = CreateText("PositionText", formPanel, "尚未放置标记", 24, TextAlignmentOptions.Left);
            SetAnchors(positionText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -436f), new Vector2(-30f, -398f));

            var submitButton = CreateButton("SubmitButton", formPanel, "提交问题");
            SetAnchors(submitButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -510f), new Vector2(-30f, -444f));

            var feedbackText = CreateText("FeedbackText", formPanel, "", 26, TextAlignmentOptions.Left);
            SetAnchors(feedbackText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(30f, -556f), new Vector2(-30f, -518f));

            // 打开/关闭面板的按钮
            var toggleButton = CreateButton("ToggleFormButton", canvasGo.transform, "填写问题");
            SetAnchors(toggleButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-160f, 40f), new Vector2(160f, 130f));

            var formController = canvasGo.AddComponent<IssueFormController>();
            var statusBanner = canvasGo.AddComponent<StatusBanner>();
            var placement = placementGo.GetComponent<PlacementController>();
            var arSession = sessionGo.GetComponent<ArSessionController>();
            var backend = appGo.GetComponent<BackendSettings>();
            var apiClient = appGo.GetComponent<ApiClient>();

            // ---------- 用 SerializedObject 绑定私有 [SerializeField] 字段 ----------
            Assign(placement, "arSession", arSession);
            Assign(placement, "raycastManager", sessionGo.GetComponent<UnityEngine.XR.ARFoundation.ARRaycastManager>());
            Assign(placement, "markerRoot", markersGo.transform);
            Assign(placement, "uiBlocker", formPanel);

            Assign(arSession, "session", sessionGo.GetComponent<UnityEngine.XR.ARFoundation.ARSession>());
            Assign(arSession, "planeManager", sessionGo.GetComponent<UnityEngine.XR.ARFoundation.ARPlaneManager>());
            Assign(arSession, "cameraManager", cameraGo.GetComponent<UnityEngine.XR.ARFoundation.ARCameraManager>());

            Assign(apiClient, "backendSettings", backend);

            Assign(formController, "placement", placement);
            Assign(formController, "apiClient", apiClient);
            Assign(formController, "backendSettings", backend);
            Assign(formController, "formRoot", formPanel.gameObject);
            Assign(formController, "titleInput", titleInput);
            Assign(formController, "descriptionInput", descInput);
            Assign(formController, "priorityButtons", priorityButtons);
            Assign(formController, "submitButton", submitButton);
            Assign(formController, "toggleButton", toggleButton);
            Assign(formController, "feedbackText", feedbackText);
            Assign(formController, "positionText", positionText);

            Assign(statusBanner, "arSession", arSession);
            Assign(statusBanner, "placement", placement);
            Assign(statusBanner, "arStatusText", arStatusText);
            Assign(statusBanner, "hintText", hintText);

            // ---------- EventSystem ----------
            var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            eventSystemGo.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemGo.AddComponent<StandaloneInputModule>();
#endif

            // ---------- 保存 ----------
            var directory = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            EditorSceneManager.SaveScene(scene, ScenePath);

            // 把场景加入 Build Settings，方便直接打包。
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[AR 巡检] 场景已生成：" + ScenePath);
            Debug.Log("[AR 巡检] 请确认已导入 TMP Essential Resources（Window > TextMeshPro > Import TMP Essential Resources），否则界面文字不会显示。");
        }

        // ---------------- UI 构建辅助 ----------------

        private static RectTransform CreatePanel(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return go.GetComponent<RectTransform>();
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string content, int fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.enableWordWrapping = true;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = new Color(0.20f, 0.52f, 0.90f);

            var text = CreateText(name + "Label", go.transform, label, 30, TextAlignmentOptions.Center);
            SetAnchors(text.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

            return go.GetComponent<Button>();
        }

        private static TMP_InputField CreateInputField(string name, Transform parent, string placeholderText)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(TMP_InputField));
            root.transform.SetParent(parent, false);
            var image = root.GetComponent<Image>();
            image.color = new Color(0.18f, 0.18f, 0.21f);

            var viewport = new GameObject("TextViewport", typeof(RectTransform));
            viewport.transform.SetParent(root.transform, false);
            var viewportRect = viewport.GetComponent<RectTransform>();
            SetAnchors(viewportRect, Vector2.zero, Vector2.one, new Vector2(8f, 4f), new Vector2(-8f, -4f));

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            textGo.transform.SetParent(viewport.transform, false);
            var textComponent = textGo.GetComponent<TextMeshProUGUI>();
            textComponent.fontSize = 28;
            textComponent.color = Color.white;
            textComponent.enableWordWrapping = false;
            SetAnchors(textComponent.rectTransform, Vector2.zero, Vector2.one, new Vector2(0f, 0f), Vector2.zero);

            var placeholderGo = new GameObject("Placeholder", typeof(RectTransform), typeof(TextMeshProUGUI));
            placeholderGo.transform.SetParent(viewport.transform, false);
            var placeholder = placeholderGo.GetComponent<TextMeshProUGUI>();
            placeholder.text = placeholderText;
            placeholder.fontSize = 28;
            placeholder.color = new Color(0.6f, 0.6f, 0.6f);
            placeholder.enableWordWrapping = false;
            SetAnchors(placeholder.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var input = root.GetComponent<TMP_InputField>();
            input.viewport = viewportRect;
            input.textComponent = textComponent;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;

            return input;
        }

        private static void SetAnchors(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        /// <summary>给 MonoBehaviour 的私有 [SerializeField] 字段赋值。</summary>
        private static void Assign(Object target, string fieldName, Object value)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null)
            {
                Debug.LogWarning("[AR 巡检] 找不到序列化字段 " + fieldName + "（" + target.GetType().Name + "）");
                return;
            }

            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>给 MonoBehaviour 的私有 [SerializeField] 数组字段赋值。</summary>
        private static void Assign(Object target, string fieldName, Object[] values)
        {
            var so = new SerializedObject(target);
            var property = so.FindProperty(fieldName);
            if (property == null || !property.isArray)
            {
                Debug.LogWarning("[AR 巡检] 找不到序列化数组字段 " + fieldName + "（" + target.GetType().Name + "）");
                return;
            }

            property.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
