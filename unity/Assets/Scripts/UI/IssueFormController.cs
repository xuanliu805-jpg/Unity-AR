using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityAR.Ar;
using UnityAR.Core;

namespace UnityAR.UI
{
    /// <summary>
    /// 问题填写面板。
    ///
    /// 负责：标题/描述/优先级输入 → 本地校验 → 调用后端上报 → 给用户明确的结果提示。
    /// 面板打开时会关闭 AR 标记放置（PlacementController.PlacementEnabled = false），
    /// 这是「点输入框时不会误放标记」的第二重保障。
    ///
    /// 优先级用三个按钮而不是下拉框：手机上点大按钮比点小三角更不容易误触，
    /// 而且下拉框需要复杂的模板 GameObject，用代码搭模板很容易出错。
    /// </summary>
    [DisallowMultipleComponent]
    public class IssueFormController : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private PlacementController placement;
        [SerializeField] private ApiClient apiClient;
        [SerializeField] private BackendSettings backendSettings;

        [Header("界面")]
        [SerializeField] private GameObject formRoot;
        [SerializeField] private TMP_InputField titleInput;
        [SerializeField] private TMP_InputField descriptionInput;
        [SerializeField] private Button[] priorityButtons;
        [SerializeField] private Button submitButton;
        [SerializeField] private Button toggleButton;
        [SerializeField] private TMP_Text feedbackText;
        [SerializeField] private TMP_Text positionText;
        [SerializeField] private Button triageButton;
        [SerializeField] private Button triageAdoptButton;
        [SerializeField] private TMP_Text triageText;

                private Coroutine submitRoutine;
        private string selectedPriority = IssuePriorities.Medium;
        private string suggestedPriority = string.Empty;

        // 保存每个优先级按钮的委托，OnDestroy 时用 RemoveListener 精确移除，
        // 不��� RemoveAllListeners（那会把 Inspector 里手工挂的监听也一起清掉）。
        private readonly System.Collections.Generic.List<UnityEngine.Events.UnityAction> priorityHandlers =
            new System.Collections.Generic.List<UnityEngine.Events.UnityAction>();

        /// <summary>当前面板是否打开。</summary>
        public bool IsFormOpen { get; private set; }

        /// <summary>当前选中的优先级。</summary>
        public string SelectedPriority
        {
            get { return selectedPriority; }
        }

        private void Awake()
        {
            if (placement == null)
            {
                placement = FindObjectOfType<PlacementController>();
            }

            if (apiClient == null)
            {
                apiClient = FindObjectOfType<ApiClient>();
            }

            if (backendSettings == null)
            {
                backendSettings = FindObjectOfType<BackendSettings>();
            }

            SetFormVisible(false);
            ShowFeedback("请先在场景中点击地面放置标记，再打开面板填写问题", false);
        }

        private void Start()
        {
            if (submitButton != null)
            {
                submitButton.onClick.AddListener(OnSubmitClicked);
            }

            if (toggleButton != null)
            {
                toggleButton.onClick.AddListener(OnToggleClicked);
            }

            if (priorityButtons != null)
            {
                for (var i = 0; i < priorityButtons.Length; i++)
                {
                    var index = i;
                    if (priorityButtons[i] == null)
                    {
                        continue;
                    }

                    UnityEngine.Events.UnityAction handler = () => OnPriorityButtonClicked(index);
                    priorityHandlers.Add(handler);
                    priorityButtons[i].onClick.AddListener(handler);
                }
            }

            if (triageButton != null)
            {
                triageButton.onClick.AddListener(OnTriageClicked);
            }

            if (triageAdoptButton != null)
            {
                triageAdoptButton.onClick.AddListener(OnAdoptSuggestedPriority);
                triageAdoptButton.gameObject.SetActive(false);
            }

            if (placement != null)
            {
                placement.MarkerPlaced += OnMarkerPlaced;
            }

            RefreshPriorityButtons();
        }

        private void OnDestroy()
        {
            if (submitButton != null)
            {
                submitButton.onClick.RemoveListener(OnSubmitClicked);
            }

            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(OnToggleClicked);
            }

            if (priorityButtons != null)
            {
                for (var i = 0; i < priorityButtons.Length && i < priorityHandlers.Count; i++)
                {
                    if (priorityButtons[i] != null)
                    {
                        priorityButtons[i].onClick.RemoveListener(priorityHandlers[i]);
                    }
                }

                priorityHandlers.Clear();
            }

            if (triageButton != null)
            {
                triageButton.onClick.RemoveListener(OnTriageClicked);
            }

            if (triageAdoptButton != null)
            {
                triageAdoptButton.onClick.RemoveListener(OnAdoptSuggestedPriority);
            }

            if (placement != null)
            {
                placement.MarkerPlaced -= OnMarkerPlaced;
            }
        }

        private void OnMarkerPlaced(Vector3Position position)
        {
            if (positionText != null)
            {
                positionText.text = "已放置标记 位置: " + position;
            }

            ShowFeedback("标记已放置，可以填写问题并提交", false);

            // 放好标记后自动打开面板，减少一次点击。
            SetFormVisible(true);
        }

        private void OnToggleClicked()
        {
            Debug.Log("[调试] OnToggleClicked 被调用，IsFormOpen=" + IsFormOpen);
            SetFormVisible(!IsFormOpen);
        }

        private void OnPriorityButtonClicked(int index)
        {
            if (index < 0 || index >= IssuePriorities.All.Length)
            {
                return;
            }

            selectedPriority = IssuePriorities.All[index];
            RefreshPriorityButtons();

            // 让画面里的标记颜色跟着优先级走（加分项：按优先级改变 AR 标记样式）。
            if (placement != null)
            {
                placement.RestyleLastMarker(selectedPriority, IssueStatuses.Open);
            }
        }

        /// <summary>刷新优先级按钮的选中态。</summary>
        private void RefreshPriorityButtons()
        {
            if (priorityButtons == null)
            {
                return;
            }

            for (var i = 0; i < priorityButtons.Length && i < IssuePriorities.All.Length; i++)
            {
                var button = priorityButtons[i];
                if (button == null)
                {
                    continue;
                }

                var selected = IssuePriorities.All[i] == selectedPriority;
                var image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = selected ? new Color(0.20f, 0.52f, 0.90f) : new Color(0.24f, 0.24f, 0.27f);
                }

                var label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.color = selected ? Color.white : new Color(0.85f, 0.85f, 0.85f);
                }
            }
        }

        /// <summary>打开/关闭面板，同时联动 AR 放置开关。</summary>
        public void SetFormVisible(bool visible)
        {
            IsFormOpen = visible;

            if (formRoot != null)
            {
                formRoot.SetActive(visible);
            }

            if (placement != null)
            {
                // 面板打开时禁止放置标记：避免巡检员点输入框时在场景里冒出标记。
                placement.PlacementEnabled = !visible;
            }

            if (toggleButton != null)
            {
                var label = toggleButton.GetComponentInChildren<TMP_Text>();
                if (label != null)
                {
                    label.text = visible ? "关闭填写面板" : "填写问题";
                }
            }
        }

        private void OnSubmitClicked()
        {
            if (submitRoutine != null)
            {
                return;
            }

            if (apiClient == null)
            {
                ShowFeedback("无法提交：ApiClient 没有配置", true);
                return;
            }

            var draft = BuildDraft();
            var validation = IssueValidator.Validate(draft);
            if (!validation.IsValid)
            {
                ShowFeedback("无法提交：" + validation.Message, true);
                return;
            }

            if (submitButton != null)
            {
                submitButton.interactable = false;
            }

            ShowFeedback("正在提交…", false);
            submitRoutine = StartCoroutine(Submit(draft));
        }

        private IEnumerator Submit(IssueDraft draft)
        {
            try
            {
                var done = false;
                var result = new ApiResult();

                yield return apiClient.SubmitIssue(draft, r =>
                {
                    result = r;
                    done = true;
                });

                // SubmitIssue 内部已经 yield 过，这里等回调被触发即可。
                while (!done)
                {
                    yield return null;
                }

                if (result.Success)
                {
                    ShowFeedback(result.Message, false);
                    if (titleInput != null)
                    {
                        titleInput.text = string.Empty;
                    }

                    if (descriptionInput != null)
                    {
                        descriptionInput.text = string.Empty;
                    }
                }
                else
                {
                    ShowFeedback("提交失败：" + result.Message, true);
                }
            }
            finally
            {
                // 无论成功失败、哪怕协程里抛了异常，都要把按钮恢复可点，
                // 否则一次失败就会让「提交问题」按钮永久失效。
                submitRoutine = null;
                if (submitButton != null)
                {
                    submitButton.interactable = true;
                }
            }
        }

        /// <summary>
        /// 调用后端的「智能分诊」，给出建议优先级、类别与重复报修风险。
        ///
        /// 分诊结果**只是建议**，不会自动改掉巡检员已经选的优先级；
        /// 想采纳要点「采纳建议优先级」。
        /// </summary>
        private void OnTriageClicked()
        {
            if (apiClient == null)
            {
                ShowFeedback("无法分诊：ApiClient 没有配置", true);
                return;
            }

            var draft = BuildDraft();
            if (string.IsNullOrEmpty(draft.Title))
            {
                ShowFeedback("无法分诊：请先填写问题标题", true);
                return;
            }

            if (triageButton != null)
            {
                triageButton.interactable = false;
            }

            ShowTriage("正在分析…");

            StartCoroutine(apiClient.Triage(draft.Title, draft.Description, draft.Position, result =>
            {
                if (triageButton != null)
                {
                    triageButton.interactable = true;
                }

                if (result == null)
                {
                    ShowTriage("分诊失败：连不上后端，请检查后端地址");
                    return;
                }

                suggestedPriority = result.SuggestedPriority;
                ShowTriage(FormatTriage(result));

                if (triageAdoptButton != null)
                {
                    triageAdoptButton.gameObject.SetActive(true);
                }
            }));
        }

        /// <summary>把分诊建议的优先级应用到表单。</summary>
        private void OnAdoptSuggestedPriority()
        {
            if (string.IsNullOrEmpty(suggestedPriority))
            {
                return;
            }

            var index = System.Array.IndexOf(IssuePriorities.All, suggestedPriority);
            if (index < 0)
            {
                return;
            }

            OnPriorityButtonClicked(index);
            ShowFeedback("已采纳建议优先级：" + IssuePriorities.DisplayName(suggestedPriority), false);

            if (triageAdoptButton != null)
            {
                triageAdoptButton.gameObject.SetActive(false);
            }
        }

        /// <summary>把分诊结果拼成给巡检员看的文字。</summary>
        private string FormatTriage(TriageResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("建议优先级：").Append(IssuePriorities.DisplayName(result.SuggestedPriority));
            sb.Append("　类别：").Append(result.SuggestedCategory);
            sb.Append("　可信度：").Append(Mathf.RoundToInt((float)result.Confidence * 100f)).Append("%");

            if (result.HasDuplicateRisk)
            {
                sb.Append("\n⚠ 可能重复报修，请先确认");
            }

            if (result.Keywords != null && result.Keywords.Count > 0)
            {
                sb.Append("\n关键词：");
                for (var i = 0; i < result.Keywords.Count && i < 6; i++)
                {
                    if (i > 0) sb.Append(" / ");
                    sb.Append(result.Keywords[i]);
                }
            }

            if (result.Reasons != null)
            {
                for (var i = 0; i < result.Reasons.Count; i++)
                {
                    sb.Append("\n· ").Append(result.Reasons[i]);
                }
            }

            if (result.SimilarIssues != null)
            {
                for (var i = 0; i < result.SimilarIssues.Count; i++)
                {
                    var s = result.SimilarIssues[i];
                    sb.Append("\n相似：").Append(s.Title)
                      .Append("（相似度 ").Append(Mathf.RoundToInt((float)s.Similarity * 100f)).Append("%")
                      .Append("，相距 ").Append(s.Distance.ToString("0.0")).Append(" 米）");
                    if (!string.IsNullOrEmpty(s.Advice))
                    {
                        sb.Append("\n　").Append(s.Advice);
                    }
                }
            }

            return sb.ToString();
        }

        private void ShowTriage(string message)
        {
            if (triageText != null)
            {
                triageText.text = message;
                triageText.color = message.StartsWith("⚠")
                    ? new Color(0.95f, 0.55f, 0.2f)
                    : new Color(0.45f, 0.72f, 0.95f);
            }
        }
        /// <summary>把界面上的输入组装成一份问题草稿。</summary>
        public IssueDraft BuildDraft()
        {
            var position = placement != null ? placement.LastPosition : Vector3Position.Zero;

            return new IssueDraft(
                titleInput != null ? titleInput.text : string.Empty,
                descriptionInput != null ? descriptionInput.text : string.Empty,
                selectedPriority,
                position);
        }

        /// <summary>显示提示信息。error=true 时用红色。</summary>
        public void ShowFeedback(string message, bool error)
        {
            if (feedbackText == null)
            {
                return;
            }

            feedbackText.text = message;
            feedbackText.color = error ? new Color(0.85f, 0.25f, 0.22f) : new Color(0.15f, 0.55f, 0.25f);
        }
    }
}
