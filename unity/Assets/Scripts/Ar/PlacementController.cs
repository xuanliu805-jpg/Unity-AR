using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityAR.Core;
using UnityAR.Input;

namespace UnityAR.Ar
{
    /// <summary>
    /// AR 标记放置。
    ///
    /// 核心约束（任务书 3.1）：
    ///   「点击检测到的平面，在命中位置放置一个简单标记」
    ///   「点击按钮或输入框时，不应误在 AR 场景中放置标记」
    ///
    /// 后一条通过 PlacementGate（纯逻辑，已单元测试）+ EventSystem 的当前帧射线命中
    /// + 填写面板矩形 三重保障实现。
    /// </summary>
    [DisallowMultipleComponent]
    public class PlacementController : MonoBehaviour
    {
        [Header("AR 组件")]
        [SerializeField] private ArSessionController arSession;
        [SerializeField] private ARRaycastManager raycastManager;

        [Header("标记")]
        [SerializeField] private Transform markerRoot;
        [SerializeField] private GameObject markerPrefab;
        [SerializeField] private Vector3 markerScale = new Vector3(0.12f, 0.12f, 0.12f);

        [Header("UI 遮挡")]
        [Tooltip("填写面板的 RectTransform。点击落在这个矩形内时不会放置标记。")]
        [SerializeField] private RectTransform uiBlocker;

        [Tooltip("标记放置后要设置的优先级，仅影响颜色。")]
        [SerializeField] private string defaultPriority = IssuePriorities.Medium;

        /// <summary>成功放置标记时触发，参数是标记的世界坐标。</summary>
        public event Action<Vector3Position> MarkerPlaced;

        /// <summary>点击被拒绝时触发，参数是原因码（见 <see cref="PlacementReasons"/>）。</summary>
        public event Action<string> PlacementRejected;

        /// <summary>放置总开关。填写面板打开时会置为 false。</summary>
        public bool PlacementEnabled { get; set; }

        /// <summary>最近一次放置的坐标；还没放置过时是零向量。</summary>
        public Vector3Position LastPosition { get; private set; }

        private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private ITapSource tapSource;
        private GameObject lastMarker;

        private void Awake()
        {
            if (arSession == null)
            {
                arSession = FindObjectOfType<ArSessionController>();
            }

            if (raycastManager == null)
            {
                raycastManager = FindObjectOfType<ARRaycastManager>();
            }

            if (markerRoot == null)
            {
                var root = new GameObject("Markers");
                root.transform.SetParent(transform, false);
                markerRoot = root.transform;
            }

            tapSource = TapSourceFactory.Create();
            PlacementEnabled = true;
        }

        private void Update()
        {
            TapInfo tap;
            if (tapSource == null || !tapSource.TryGetTap(out tap))
            {
                return;
            }

            var context = new PlacementContext
            {
                PlacementEnabled = PlacementEnabled,
                PointerOverUI = IsPointerOverUI(tap),
                PlaneTracked = arSession != null && arSession.HasHorizontalPlane,
                TapBegan = tap.Began
            };

            var decision = PlacementGate.Evaluate(context);
            if (!decision.ShouldPlace)
            {
                if (PlacementRejected != null)
                {
                    PlacementRejected(decision.Reason);
                }

                return;
            }

            PlaceMarker(tap.ScreenPosition);
        }

        /// <summary>
        /// 判断这次点击是否落在界面控件上。
        ///
        /// 这里刻意用 <see cref="EventSystem.RaycastAll"/> 而不是
        /// <c>EventSystem.IsPointerOverGameObject</c>：后者读的是 EventSystem.Update 里
        /// 缓存的上一帧命中状态，而本脚本的 Update 与 EventSystem.Update 的执行顺序是不确定的，
        /// 于是点顶部状态栏这类 UI 时会「读到上一帧」而误判成不在 UI 上，进而误放标记。
        /// RaycastAll 会当场重新射线一次，没有这一帧延迟。
        /// </summary>
        private bool IsPointerOverUI(TapInfo tap)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem != null)
            {
                var pointerData = new PointerEventData(eventSystem) { position = tap.ScreenPosition };
                uiHits.Clear();
                eventSystem.RaycastAll(pointerData, uiHits);
                if (uiHits.Count > 0)
                {
                    return true;
                }
            }

            // 兜底：填写面板矩形内一律不放标记。
            // 注意 RectangleContainsScreenPoint 只做几何计算、不看对象是否 active，
            // 面板收起时它依然占着屏幕下半部分，所以必须先确认它可见。
            if (uiBlocker != null && uiBlocker.gameObject.activeInHierarchy)
            {
                var canvas = uiBlocker.GetComponentInParent<Canvas>();
                var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera
                    : null;

                if (RectTransformUtility.RectangleContainsScreenPoint(uiBlocker, tap.ScreenPosition, camera))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>在屏幕位置处放置标记。</summary>
        private void PlaceMarker(Vector2 screenPosition)
        {
            if (raycastManager == null)
            {
                Reject(PlacementReasons.NoPlane);
                return;
            }

            hits.Clear();
            if (!raycastManager.Raycast(screenPosition, hits, TrackableType.Planes) || hits.Count == 0)
            {
                Reject(PlacementReasons.NoPlane);
                return;
            }

            var hit = hits[0];
            // ARRaycastHit.pose 是世界空间坐标，直接用它。
            var worldPosition = hit.pose.position;

            var marker = CreateMarker();
            marker.transform.position = worldPosition;
            MarkerStyler.Apply(marker, defaultPriority);
            lastMarker = marker;

            LastPosition = new Vector3Position(worldPosition.x, worldPosition.y, worldPosition.z);
            if (MarkerPlaced != null)
            {
                MarkerPlaced(LastPosition);
            }
        }

        /// <summary>
        /// 按新的优先级重新给最近放置的标记上色。
        /// 巡检员选完优先级后调用，画面上就能立刻看出问题的严重程度。
        /// </summary>
        public void RestyleLastMarker(string priority)
        {
            if (lastMarker == null)
            {
                return;
            }

            MarkerStyler.Apply(lastMarker, priority);
        }

        /// <summary>生成标记对象。没配置 prefab 时用 Unity 内置立方体兜底（任务书允许基础几何体）。</summary>
        private GameObject CreateMarker()
        {
            GameObject marker;
            if (markerPrefab != null)
            {
                marker = Instantiate(markerPrefab, markerRoot);
            }
            else
            {
                marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "IssueMarker";
                marker.transform.SetParent(markerRoot, false);
            }

            marker.transform.localScale = markerScale;
            return marker;
        }

        private void Reject(string reason)
        {
            if (PlacementRejected != null)
            {
                PlacementRejected(reason);
            }
        }
    }
}
