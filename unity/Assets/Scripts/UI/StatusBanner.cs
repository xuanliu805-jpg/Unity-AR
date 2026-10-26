using UnityEngine;
using TMPro;
using UnityAR.Ar;
using UnityAR.Core;

namespace UnityAR.UI
{
    /// <summary>
    /// 顶部状态条：显示 AR 跟踪状态、平面检测进度和放置提示。
    /// </summary>
    [DisallowMultipleComponent]
    public class StatusBanner : MonoBehaviour
    {
        [SerializeField] private ArSessionController arSession;
        [SerializeField] private PlacementController placement;
        [SerializeField] private TMP_Text arStatusText;
        [SerializeField] private TMP_Text hintText;

        private void Awake()
        {
            if (arSession == null)
            {
                arSession = FindObjectOfType<ArSessionController>();
            }

            if (placement == null)
            {
                placement = FindObjectOfType<PlacementController>();
            }
        }

        private void OnEnable()
        {
            if (arSession != null)
            {
                arSession.StatusChanged += OnArStatusChanged;
                arSession.HorizontalPlaneCountChanged += OnPlaneCountChanged;
                OnArStatusChanged(arSession.StatusText);
            }

            if (placement != null)
            {
                placement.PlacementRejected += OnPlacementRejected;
            }
        }

        private void OnDisable()
        {
            if (arSession != null)
            {
                arSession.StatusChanged -= OnArStatusChanged;
                arSession.HorizontalPlaneCountChanged -= OnPlaneCountChanged;
            }

            if (placement != null)
            {
                placement.PlacementRejected -= OnPlacementRejected;
            }
        }

        private void OnArStatusChanged(string status)
        {
            if (arStatusText != null)
            {
                arStatusText.text = status;
            }
        }

        private void OnPlaneCountChanged(int count)
        {
            if (hintText != null)
            {
                hintText.text = count > 0
                    ? "检测到 " + count + " 个水平平面，点击地面放置标记"
                    : "请缓慢移动手机扫描地面";
            }
        }

        private void OnPlacementRejected(string reason)
        {
            if (hintText != null)
            {
                hintText.text = PlacementGate.Describe(reason);
            }
        }
    }
}
