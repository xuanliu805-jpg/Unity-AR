using UnityEngine;
using UnityAR.Core;

namespace UnityAR.Ar
{
    /// <summary>
    /// 标记外观控制：按优先级给标记上色，让管理人员一眼就能从画面里分辨问题严重程度。
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
        /// 把颜色应用到标记上。
        ///
        /// 用 MaterialPropertyBlock 而不是 <c>renderer.material</c>：
        /// 后者每访问一次都会给渲染器实例化一份材质，标记一多就是材质泄漏 + DrawCall 增加。
        /// </summary>
        public static void Apply(GameObject marker, string priority)
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
            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            // URP 用 _BaseColor，内置管线用 _Color，两个都设一遍最保险。
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }
    }
}
