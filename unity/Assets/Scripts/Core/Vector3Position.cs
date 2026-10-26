using System;
using System.Globalization;

namespace UnityAR.Core
{
    /// <summary>
    /// 标记在世界空间中的 x/y/z 坐标（单位：米）。
    /// 与后端 <c>position</c> 字段结构一致：<c>{ "x":…, "y":…, "z":… }</c>。
    /// 不使用 UnityEngine.Vector3，是为了让这段逻辑可以脱离 Unity 单测。
    /// </summary>
    [Serializable]
    public struct Vector3Position
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3Position(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3Position Zero
        {
            get { return new Vector3Position(0f, 0f, 0f); }
        }

        /// <summary>坐标是否都是有限数值（不含 NaN / Infinity）。</summary>
        public bool IsFinite
        {
            get
            {
                return !float.IsNaN(X) && !float.IsInfinity(X)
                    && !float.IsNaN(Y) && !float.IsInfinity(Y)
                    && !float.IsNaN(Z) && !float.IsInfinity(Z);
            }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);
        }
    }
}
