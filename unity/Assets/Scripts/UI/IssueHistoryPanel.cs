using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityAR.Core;

namespace UnityAR.UI
{
    /// <summary>
    /// 历史问题列表 + 编辑面板。
    ///
    /// 巡检员在手机上常常不只是「上报新问题」，还要回头看**自己之前报过什么**、
    /// 以及**管理���把某条问题处理掉了没有**。这个面板解决这两件事：
    ///   - 列出后端已有的全部问题（标题/优先级/状态/时间/坐标）；
    ///   - 可以直接把某条的状态改成 待处理 / 处理中 / 已解决，或删除它。
    ///
    /// 面板在运行时自己构建 UI（而不是在 SceneBuilder 里手搭），
    /// 原因是列表行是**按数据动态生成**的，手搭反而更绕。
    /// </summary>
    [DisallowMultipleComponent]
    public class IssueHistoryPanel : MonoBehaviour
    {
        [SerializeField] private ApiClient apiClient;
        [SerializeField] private BackendSettings backendSettings;

        private GameObject panelRoot;
        private RectTransform contentRoot;
        private TMP_Text statusText;
        private TMP_Text emptyText;
        private Coroutine refreshRoutine;
        private bool built;

        /// <summary>面板当前是否打开。</summary>
        public bool IsOpen { get; private set; }

        private void Awake()
        {
            if (apiClient == null) apiClient = FindObjectOfType<ApiClient>();
            if (backendSettings == null) backendSettings = FindObjectOfType<BackendSettings>();
            Build();
        }

        /// <summary>打开/关闭面板。</summary>
        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        /// <summary>打开面板并拉取最新列表。</summary>
        public void Open()
        {
            if (panelRoot != null) panelRoot.SetActive(true);
            IsOpen = true;
            Refresh();
        }

        /// <summary>关闭面板。</summary>
        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
            IsOpen = false;
        }

        /// <summary>从后端拉取列表并渲染。</summary>
        public void Refresh()
        {
            if (apiClient == null)
            {
                SetStatus("未配置 ApiClient，无法加载历史问题");
                return;
            }

            SetStatus("正在加载…");
            refreshRoutine = StartCoroutine(apiClient.GetIssues(OnIssuesLoaded));
        }

        private void OnIssuesLoaded(List<IssueSummary> issues)
        {
            if (issues == null)
            {
                SetStatus("加载失败：连不上后端，请检查后端地址");
                return;
            }

            ClearRows();

            if (issues.Count == 0)
            {
                if (emptyText != null) emptyText.gameObject.SetActive(true);
                SetStatus("共 0 条记录");
                return;
            }

            if (emptyText != null) emptyText.gameObject.SetActive(false);

            // 最近的排在前面
            issues.Reverse();
            for (var i = 0; i < issues.Count; i++)
            {
                CreateRow(issues[i]);
            }

            SetStatus("共 " + issues.Count + " 条记录");
        }

        /// <summary>为一条历史问题生成一行（含状态编辑与删除按钮）。</summary>
        private void CreateRow(IssueSummary issue)
        {
            var row = new GameObject("Row", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter), typeof(Image));
            row.transform.SetParent(contentRoot, false);

            var layout = row.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 16, 16);
            layout.spacing = 8;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            var fitter = row.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var bg = row.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.45f);

            // 标题
            var title = CreateText(row.transform, "Title", issue.Title, 30, TextAlignmentOptions.Left);
            title.color = Color.white;

            // 元信息
            var meta = CreateText(row.transform, "Meta",
                "优先级 " + IssuePriorities.DisplayName(issue.Priority) +
                "  ·  状态 " + IssueStatuses.DisplayName(issue.Status) +
                "  ·  " + issue.CreatedAt, 22, TextAlignmentOptions.Left);
            meta.color = new Color(0.75f, 0.75f, 0.78f);

            // 坐标
            var pos = CreateText(row.transform, "Pos",
                string.Format("位置 x {0:0.00} · y {1:0.00} · z {2:0.00}",
                    issue.Position.X, issue.Position.Y, issue.Position.Z),
                22, TextAlignmentOptions.Left);
            pos.color = new Color(0.75f, 0.75f, 0.78f);

            // 状态编辑按钮组
            var actions = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup),
                typeof(ContentSizeFitter));
            actions.transform.SetParent(row.transform, false);
            var actionLayout = actions.GetComponent<HorizontalLayoutGroup>();
            actionLayout.spacing = 12;
            actionLayout.childForceExpandWidth = false;
            actionLayout.childControlHeight = true;
            var actionFitter = actions.GetComponent<ContentSizeFitter>();
            actionFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            foreach (var status in IssueStatuses.All)
            {
                var captured = status;
                var label = "设为" + IssueStatuses.DisplayName(captured);
                var button = CreateButton(actions.transform, label, captured == issue.Status);
                button.onClick.AddListener(() => ChangeStatus(issue, captured));
            }

            var deleteButton = CreateButton(actions.transform, "删除", false);
            deleteButton.onClick.AddListener(() => Delete(issue));
        }

        private void ChangeStatus(IssueSummary issue, string status)
        {
            if (apiClient == null) return;
            SetStatus("正在更新 " + issue.Title + " …");
            StartCoroutine(apiClient.UpdateStatus(issue.ID, status, result =>
            {
                if (result != null && result.Success)
                {
                    SetStatus("已把「" + issue.Title + "」设为" + IssueStatuses.DisplayName(status));
                }
                else
                {
                    SetStatus("更新失败：" + (result != null ? result.Message : "未知错误"));
                }
                Refresh();
            }));
        }

        private void Delete(IssueSummary issue)
        {
            if (apiClient == null) return;
            SetStatus("正在删除 " + issue.Title + " …");
            StartCoroutine(apiClient.DeleteIssue(issue.ID, result =>
            {
                if (result != null && result.Success)
                {
                    SetStatus("已删除「" + issue.Title + "」");
                }
                else
                {
                    SetStatus("删除失败：" + (result != null ? result.Message : "未知错误"));
                }
                Refresh();
            }));
        }

        private void ClearRows()
        {
            if (contentRoot == null) return;
            for (var i = contentRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(contentRoot.GetChild(i).gameObject);
            }
        }

        private void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message;
        }

        // ------------------------------------------------------------ UI 构建

        private void Build()
        {
            if (built) return;
            built = true;

            // 挂在**已有的 UI 画布**下面，而不是自建一个嵌套 Canvas。
            // 自建嵌套 Canvas 会带来两个问题：
            //   1. 它的 CanvasScaler 会干扰画布缩放，导致整个界面错位；
            //   2. 它自带 GraphicRaycaster，会让 EventSystem.RaycastAll 的全局
            //      raycaster 列表变得不可靠，进而影响「点按钮不误放标记」的判定。
            var parentCanvas = GetComponentInParent<Canvas>();
            if (parentCanvas == null)
            {
                Debug.LogError("[历史问题面板] 找不到父级 Canvas");
                return;
            }

            var canvasGo = new GameObject("HistoryPanelRoot", typeof(RectTransform));
            canvasGo.transform.SetParent(parentCanvas.transform, false);
            // 放到最后，保证画在最上层
            canvasGo.transform.SetAsLastSibling();

            Stretch((RectTransform)canvasGo.transform);
            panelRoot = canvasGo;

            // 半透明遮罩
            var mask = new GameObject("Mask", typeof(RectTransform), typeof(Image));
            mask.transform.SetParent(canvasGo.transform, false);
            Stretch((RectTransform)mask.transform);
            mask.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);

            // 标题栏
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(canvasGo.transform, false);
            var headerRect = (RectTransform)header.transform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.offsetMin = new Vector2(0f, -120f);
            headerRect.offsetMax = new Vector2(0f, 0f);

            var titleText = CreateText(header.transform, "Title", "历史问题", 40, TextAlignmentOptions.Left);
            titleText.color = Color.white;
            Stretch((RectTransform)titleText.transform, 24f, 0f, 24f, 0f);

            var closeButton = CreateButton(header.transform, "关闭", false);
            var closeRect = (RectTransform)closeButton.transform;
            closeRect.anchorMin = new Vector2(1f, 0.5f);
            closeRect.anchorMax = new Vector2(1f, 0.5f);
            closeRect.pivot = new Vector2(1f, 0.5f);
            closeRect.sizeDelta = new Vector2(160f, 72f);
            closeRect.anchoredPosition = new Vector2(-24f, 0f);
            closeButton.onClick.AddListener(Close);

            // 状态行
            var statusGo = new GameObject("StatusRow", typeof(RectTransform));
            statusGo.transform.SetParent(canvasGo.transform, false);
            var statusRect = (RectTransform)statusGo.transform;
            statusRect.anchorMin = new Vector2(0f, 1f);
            statusRect.anchorMax = new Vector2(1f, 1f);
            statusRect.pivot = new Vector2(0.5f, 1f);
            statusRect.offsetMin = new Vector2(0f, -176f);
            statusRect.offsetMax = new Vector2(0f, -120f);

            statusText = CreateText(statusGo.transform, "Status", "", 24, TextAlignmentOptions.Left);
            statusText.color = new Color(0.8f, 0.85f, 0.9f);
            Stretch((RectTransform)statusText.transform, 24f, 0f, 24f, 0f);

            // 滚动区域
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(Image),
                typeof(ScrollRect));
            scrollGo.transform.SetParent(canvasGo.transform, false);
            var scrollRect = (RectTransform)scrollGo.transform;
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(16f, 16f);
            scrollRect.offsetMax = new Vector2(-16f, -190f);
            scrollGo.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image),
                typeof(Mask));
            viewport.transform.SetParent(scrollGo.transform, false);
            Stretch((RectTransform)viewport.transform);
            viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.01f);

            var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup),
                typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            contentRoot = (RectTransform)content.transform;
            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(1f, 1f);
            contentRoot.pivot = new Vector2(0.5f, 1f);
            contentRoot.offsetMin = Vector2.zero;
            contentRoot.offsetMax = Vector2.zero;

            var contentLayout = content.GetComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(0, 0, 8, 8);
            contentLayout.spacing = 12;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childForceExpandWidth = true;

            var contentFitter = content.GetComponent<ContentSizeFitter>();
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGo.GetComponent<ScrollRect>();
            scroll.viewport = (RectTransform)viewport.transform;
            scroll.content = contentRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;

            // 空态
            emptyText = CreateText(contentRoot, "Empty", "还没有历史问题", 26, TextAlignmentOptions.Center);
            emptyText.color = new Color(0.8f, 0.85f, 0.9f);
            emptyText.gameObject.SetActive(false);

            canvasGo.SetActive(false);
        }

        private static TMP_Text CreateText(Transform parent, string name, string content, int size,
            TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = size;
            text.alignment = alignment;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            return text;
        }

        private static Button CreateButton(Transform parent, string label, bool highlighted)
        {
            var go = new GameObject("Button_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.color = highlighted ? new Color(0.16f, 0.55f, 0.85f) : new Color(0.22f, 0.22f, 0.26f);

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var text = CreateText(go.transform, "Label", label, 24, TextAlignmentOptions.Center);
            text.color = highlighted ? Color.white : new Color(0.9f, 0.9f, 0.92f);
            Stretch((RectTransform)text.transform, 8f, 8f, 8f, 8f);

            var layout = go.GetComponent<RectTransform>();
            layout.sizeDelta = new Vector2(200f, 72f);

            return button;
        }

        private static void Stretch(RectTransform rect, float left = 0f, float bottom = 0f,
            float right = 0f, float top = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }
    }
}
