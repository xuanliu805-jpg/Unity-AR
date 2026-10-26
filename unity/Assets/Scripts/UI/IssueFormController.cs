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

        private Coroutine submitRoutine;
        private string selectedPriority = IssuePriorities.Medium;

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
                    if (priorityButtons[i] != null)
                    {
                        priorityButtons[i].onClick.AddListener(() => OnPriorityButtonClicked(index));
                    }
                }
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
                for (var i = 0; i < priorityButtons.Length; i++)
                {
                    if (priorityButtons[i] != null)
                    {
                        priorityButtons[i].onClick.RemoveAllListeners();
                    }
                }
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

            submitRoutine = null;
            if (submitButton != null)
            {
                submitButton.interactable = true;
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
