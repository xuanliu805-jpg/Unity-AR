using UnityEngine;
using UnityAR.Core;

namespace UnityAR.Ar
{
    /// <summary>
    /// 标记外观控制：按优先级给标记上色，让管理人员一眼能从画面里分辨问题严重程度。
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

        /// <summary>把颜色应用到标记上（含子渲染器）。</summary>
        public static void Apply(GameObject marker, string priority)
        {
            if (marker == null)
            {
                return;
            }

            var color = ColorFor(priority);
            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null)
            {
                renderer.material.color = color;
            }
        }
    }
}
