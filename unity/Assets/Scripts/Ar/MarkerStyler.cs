using UnityEngine;
using UnityAR.Core;

namespace UnityAR.Ar
{
    /// <summary>
    /// 标记外观控制。
    ///
    /// 两层视觉编码：
    ///   - **优先级决定色相**：低=绿 / 中=黄 / 高=红，管理人员一眼能看出严重程度；
    ///   - **状态决定形态**：待处理=实心正常大小 / 处理中=高亮放大 / 已解决=半透明缩小，
    ///     这样从画面里也能一眼看出哪些已经处理完了。
    /// </summary>
    public static class MarkerStyler
    {
        public static readonly Color LowColor = new Color(0.36f, 0.78f, 0.36f);   // 绿
        public static readonly Color MediumColor = new Color(0.95f, 0.72f, 0.20f); // 黄
        public static readonly Color HighColor = new Color(0.90f, 0.28f, 0.25f);   // 红

        /// <summary>根据优先级取颜色。</summary>
        public static Color ColorFor(string priority)
        {
            switch (priority)
            {
                case IssuePriorities.Low:
                    return LowColor;
                case IssuePriorities.High:
                    return HighColor;
                default:
                    return MediumColor;
            }
        }

        /// <summary>
        /// 根据状态取「缩放系数」。
        /// 处理中的问题更显眼，已解决的缩小淡出 —— 一眼能区分「还在盯的」和「已经搞定的」。
        /// </summary>
        public static float ScaleFor(string status)
        {
            switch (status)
            {
                case IssueStatuses.Resolved:
                    return 0.75f;
                case IssueStatuses.InProgress:
                    return 1.25f;
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// 根据状态取「不透明度」。
        /// 已解决的淡出到 35%，避免它继续抢占画面注意力。
        /// </summary>
        public static float AlphaFor(string status)
        {
            switch (status)
            {
                case IssueStatuses.Resolved:
                    return 0.35f;
                case IssueStatuses.InProgress:
                    return 1f;
                default:
                    return 0.9f;
            }
        }

        /// <summary>
        /// 把颜色与透明度应用到标记上。
        ///
        /// 用 MaterialPropertyBlock 而不是 <c>renderer.material</c>：
        /// 后者每访问一次都会给渲染器实例化一份材质，标记一多就是材质泄漏 + DrawCall 增加。
        /// </summary>
        public static void Apply(GameObject marker, string priority, string status)
        {
            if (marker == null)
            {
                return;
            }

            var renderer = marker.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            var color = ColorFor(priority);
            color.a = AlphaFor(status);

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            // URP 用 _BaseColor，内置管线用 _Color，两个都设一遍最保险。
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// 按状态调整标记大小。
        /// </summary>
        public static void ApplyScale(GameObject marker, Vector3 baseScale, string status)
        {
            if (marker == null)
            {
                return;
            }

            marker.transform.localScale = baseScale * ScaleFor(status);
        }
    }
}
