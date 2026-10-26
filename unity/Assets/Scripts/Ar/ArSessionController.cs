using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace UnityAR.Ar
{
    /// <summary>
    /// AR 会话与平面检测的状态封装。
    ///
    /// 职责：
    ///   1. 只检测**水平**平面（巡检场景看的是地面/墙面下沿，水平面最稳）；
    ///   2. 把 ARSession 的状态翻译成中文提示，直接喂给 UI；
    ///   3. 维护「当前是否有可用水平平面」，供放置逻辑判断。
    /// </summary>
    [DisallowMultipleComponent]
    public class ArSessionController : MonoBehaviour
    {
        [Header("AR 组件")]
        [SerializeField] private ARSession session;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private ARCameraManager cameraManager;

        /// <summary>AR 状态变化（参数是中文提示文案）。</summary>
        public event Action<string> StatusChanged;

        /// <summary>可用水平平面数量变化。</summary>
        public event Action<int> HorizontalPlaneCountChanged;

        /// <summary>当前是否存在可以放置标记的水平平面。</summary>
        public bool HasHorizontalPlane { get; private set; }

        /// <summary>当前状态的中文提示。初始就有值，避免 UI 在 OnEnable 早期拿到 null。</summary>
        public string StatusText { get; private set; } = "AR 尚未启动";

        /// <summary>AR 会话状态原文，便于排查问题。</summary>
        public string RawState { get; private set; }

        public ARSession Session
        {
            get { return session; }
        }

        public ARPlaneManager PlaneManager
        {
            get { return planeManager; }
        }

                /// <summary>上一次统计到的水平平面数量，用来判断数量是否真的变了。</summary>
        private int lastPlaneCount = -1;

        private void Awake()
        {
            if (session == null)
            {
                session = GetComponent<ARSession>();
            }

            if (planeManager == null)
            {
                planeManager = GetComponent<ARPlaneManager>();
            }

            if (cameraManager == null)
            {
                cameraManager = FindObjectOfType<ARCameraManager>();
            }

            if (planeManager != null)
            {
                // 只检测水平平面：巡检标记贴在地面/矮墙根上最稳定，垂直面在弱光下反而容易误检。
                planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
            }
        }

        private void OnEnable()
        {
            // ARSession.stateChanged 是静态事件，OnEnable 订阅、OnDisable 退订，避免重复订阅导致回调多次触发。
            ARSession.stateChanged += OnSessionStateChanged;
            RefreshStatus();
        }

        private void OnDisable()
        {
            ARSession.stateChanged -= OnSessionStateChanged;
        }

        private void Update()
        {
            RefreshPlaneCount();
        }

        private void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            RefreshStatus();
        }

        /// <summary>刷新状态文案。</summary>
        public void RefreshStatus()
        {
            // 用 ToString() 而不是直接比较枚举成员，AR Foundation 不同版本的枚举名字有差异，
            // 这样即使升级包版本也不会编译不过。
            RawState = ARSession.state.ToString();
            StatusText = DescribeState(RawState);
            if (StatusChanged != null)
            {
                StatusChanged(StatusText);
            }
        }

        /// <summary>刷新水平平面数量。</summary>
        public void RefreshPlaneCount()
        {
            var count = CountHorizontalPlanes();

            // 数量真的变了才通知。
            // 之前只在「0 ↔ 非 0」翻转时通知，1 → 3 这种变化会被吞掉，
            // 状态栏上的「检测到 N 个水平平面」会长期停留在旧值。
            if (count == lastPlaneCount)
            {
                return;
            }

            lastPlaneCount = count;
            HasHorizontalPlane = count > 0;

            if (HorizontalPlaneCountChanged != null)
            {
                HorizontalPlaneCountChanged(count);
            }
        }

        /// <summary>统计当前可用的水平平面数量。</summary>
        public int CountHorizontalPlanes()
        {
            if (planeManager == null)
            {
                return 0;
            }

            var count = 0;
            foreach (var plane in planeManager.trackables)
            {
                if (plane == null)
                {
                    continue;
                }

                if (plane.alignment == PlaneAlignment.HorizontalUp
                    || plane.alignment == PlaneAlignment.HorizontalDown)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>把 AR 会话状态翻译成给巡检员看的中文提示。</summary>
        public static string DescribeState(string rawState)
        {
            switch (rawState)
            {
                case "SessionTracking":
                    return "AR 正在跟踪，移动手机扫描地面";
                case "SessionInitializing":
                    return "AR 会话正在初始化，请稍候";
                case "Ready":
                    return "AR 已就绪";
                case "Unsupported":
                    return "当前设备不支持 AR";
                case "NeedsInstall":
                    return "需要先安装 ARCore/ARKit 服务";
                case "Installing":
                    return "正在安装 AR 服务";
                case "CheckingAvailability":
                    return "正在检查设备 AR 支持情况";
                case "None":
                    return "AR 尚未启动";
                default:
                    return "AR 状态：" + rawState;
            }
        }
    }
}
