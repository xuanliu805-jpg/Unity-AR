using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
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
        [Tooltip("UI 画布上的 GraphicRaycaster。直接问它拿 UI 命中，比 EventSystem.RaycastAll 可靠。")]
        [SerializeField] private GraphicRaycaster uiRaycaster;

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
        private Material markerMaterial;

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
            Debug.Log("[调试] tap=" + tap.ScreenPosition + " finger=" + tap.FingerId +
                      " eventSystem=" + (EventSystem.current != null) +
                      " uiHits=" + uiHits.Count +
                      " blockerActive=" + (uiBlocker != null && uiBlocker.gameObject.activeInHierarchy) +
                      " -> " + decision.Reason);
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

                // 直接问画布上的 GraphicRaycaster，而不是 EventSystem.RaycastAll：
                // 后者走的是 EventSystem 内部的全局 raycaster 列表，
                // 在有嵌套 Canvas 的场景里可能根本拿不到画布上这个 raycaster，
                // 于是 uiHits 恒为 0 —— 结果就是「点按钮时不会被识别成 UI 点击」，
                // 反而会去放置标记，正好违反任务书 3.1 的要求。
                var raycaster = uiRaycaster != null
                    ? uiRaycaster
                    : FindObjectOfType<GraphicRaycaster>();

                if (raycaster != null)
                {
                    raycaster.Raycast(pointerData, uiHits);
                }
                else
                {
                    eventSystem.RaycastAll(pointerData, uiHits);
                }

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

        /// <summary>
        /// 求出这次点击对应的世界坐标点。
        ///
        /// 两条路径，先试 ARCore 自己的命中测试，不行就用「射线 × 已检测到的水平平面」自己求交：
        ///
        /// 为什么要回退：AR Foundation 的 <c>TrackableType.Planes</c> 里含
        /// <c>PlaneWithinInfinity</c> 等 ARCore 原生层并不支持的标志位，
        /// 掩码是直接透传给原生层的，在部分机型/驱动上会返回**空结果**，
        /// 于是「明明检测到地面平面却怎么点都放不下标记」。
        /// 巡检场景其实只需要点地面，用已经检测到的水平平面自己求交更稳、更可控，
        /// 而且这段求交逻辑（Core/RayPlaneIntersection）是有单元测试覆盖的。
        /// </summary>
        private bool TryGetPlacementPoint(Vector2 screenPosition, out Vector3 worldPosition)
        {
            worldPosition = Vector3.zero;

            // ① ARCore 原生命中测试
            if (raycastManager != null)
            {
                hits.Clear();
                // 只请求 PlaneWithinPolygon —— 这是 ARCore 原生层明确支持的标志位。
                if (raycastManager.Raycast(screenPosition, hits, TrackableType.PlaneWithinPolygon)
                    && hits.Count > 0)
                {
                    // ARRaycastHit.pose 是世界空间坐标，直接用它。
                    worldPosition = hits[0].pose.position;
                    return true;
                }
            }

            // ② 回退：射线 × 水平平面，自己求交
            var camera = Camera.main;
            if (camera == null || arSession == null)
            {
                return false;
            }

            var planes = arSession.HorizontalPlanes;
            if (planes == null || planes.Count == 0)
            {
                return false;
            }

            var corePlanes = new Core.Plane3[planes.Count];
            for (var i = 0; i < planes.Count; i++)
            {
                var plane = planes[i];
                if (plane == null)
                {
                    continue;
                }

                var normal = plane.normal;                 // 平面法线（世界空间，单位向量）
                var center = plane.center;                 // 平面中心（世界空间）
                corePlanes[i] = new Core.Plane3
                {
                    Nx = normal.x, Ny = normal.y, Nz = normal.z,
                    Px = center.x, Py = center.y, Pz = center.z
                };
            }

            var ray = camera.ScreenPointToRay(screenPosition);
            var coreRay = new Core.Ray3
            {
                OriginX = ray.origin.x, OriginY = ray.origin.y, OriginZ = ray.origin.z,
                DirX = ray.direction.x, DirY = ray.direction.y, DirZ = ray.direction.z
            };

            Core.Vector3Position point;
            int planeIndex;
            if (!Core.RayPlaneIntersection.TryIntersectNearest(coreRay, corePlanes, out point, out planeIndex))
            {
                return false;
            }

            worldPosition = new Vector3(point.X, point.Y, point.Z);
            return true;
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
            MarkerStyler.Apply(marker, defaultPriority, IssueStatuses.Open);
            MarkerStyler.ApplyScale(marker, markerScale, IssueStatuses.Open);
            lastMarker = marker;

            LastPosition = new Vector3Position(worldPosition.x, worldPosition.y, worldPosition.z);
            if (MarkerPlaced != null)
            {
                MarkerPlaced(LastPosition);
            }
        }

        /// <summary>
        /// 按新的优先级/状态重新给最近放置的标记上色与调整大小。
        /// 巡检员选完优先级、或问题状态被改成「已解决」后调用，
        /// 画面上就能立刻看出问题的严重程度与处理进度。
        /// </summary>
        public void RestyleLastMarker(string priority, string status)
        {
            if (lastMarker == null)
            {
                return;
            }

            MarkerStyler.Apply(lastMarker, priority, status);
            MarkerStyler.ApplyScale(lastMarker, markerScale, status);
        }

        /// <summary>最近放置的标记对象（供外部改样式）。</summary>
        public GameObject LastMarker
        {
            get { return lastMarker; }
        }

        /// <summary>
        /// 生成标记对象。没配置 prefab 时用 Unity 内置立方体兜底（任务书允许基础几何体）。
        ///
        /// 这里给标记显式指定一份 Standard 材质，而不是直接用 CreatePrimitive 的内置默认材质：
        /// 内置默认材质依赖的 shader 默认不在「始终包含的着色器」里，
        /// 新建工程没有材质引用它 → 打包时被剔除 → 真机上标记渲染成 Unity 的「shader 缺失」品红色。
        /// （ArProjectSettings.ConfigureAlwaysIncludedShaders() 也会把它加进去，双保险。）
        /// </summary>
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

                var markerRenderer = marker.GetComponent<Renderer>();
                if (markerRenderer != null)
                {
                    if (markerMaterial == null)
                    {
                        var shader = Shader.Find("Standard");
                        if (shader != null)
                        {
                            markerMaterial = new Material(shader) { name = "IssueMarkerMaterial" };
                        }
                    }

                    if (markerMaterial != null)
                    {
                        markerRenderer.sharedMaterial = markerMaterial;
                    }
                }
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
