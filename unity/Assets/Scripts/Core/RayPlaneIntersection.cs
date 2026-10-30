namespace UnityAR.Core
{
    /// <summary>
    /// 一条射线（不依赖 UnityEngine，方便单元测试）。
    /// </summary>
    public struct Ray3
    {
        public float OriginX, OriginY, OriginZ;
        public float DirX, DirY, DirZ;
    }

    /// <summary>
    /// 一个平面（不依赖 UnityEngine）。
    /// 由「单位法线 + 平面上一点」定义，与 UnityEngine.Plane 的语义一致。
    /// </summary>
    public struct Plane3
    {
        public float Nx, Ny, Nz;
        public float Px, Py, Pz;
    }

    /// <summary>
    /// 射线与平面的求交。
    ///
    /// 为什么自己算：ARCore 的原生 raycast 依赖 TrackableType 标志位，
    /// 在部分机型上会返回空结果；而巡检场景只需要「点地面」，
    /// 用已经检测到的水平平面自己求交更稳，而且这段逻辑可以被单元测试覆盖。
    /// </summary>
    public static class RayPlaneIntersection
    {
        private const float Epsilon = 1e-6f;

        /// <summary>射线是否与平面相交；相交时 distance 是沿射线的距离（&gt; 0）。</summary>
        public static bool TryIntersect(Ray3 ray, Plane3 plane, out float distance)
        {
            distance = 0f;

            // 射线方向与平面法线几乎平行 → 不可能相交
            var vdot = ray.DirX * plane.Nx + ray.DirY * plane.Ny + ray.DirZ * plane.Nz;
            if (Mathf(vdot) < Epsilon && Mathf(vdot) > -Epsilon)
            {
                return false;
            }

            // 平面方程：dot(n, p) = dot(n, planePoint)
            var d = plane.Nx * plane.Px + plane.Ny * plane.Py + plane.Nz * plane.Pz;

            // 代入射线 p = origin + t * dir 求 t
            var ndotO = plane.Nx * ray.OriginX + plane.Ny * ray.OriginY + plane.Nz * ray.OriginZ;
            var t = (d - ndotO) / vdot;

            if (t <= 0f)
            {
                return false;
            }

            distance = t;
            return true;
        }

        /// <summary>射线与平面相交时给出命中点。</summary>
        public static bool TryIntersectAt(Ray3 ray, Plane3 plane, out Vector3Position point)
        {
            point = Vector3Position.Zero;

            float distance;
            if (!TryIntersect(ray, plane, out distance))
            {
                return false;
            }

            point = new Vector3Position(
                ray.OriginX + ray.DirX * distance,
                ray.OriginY + ray.DirY * distance,
                ray.OriginZ + ray.DirZ * distance);
            return true;
        }

        /// <summary>
        /// 在多个平面里找**离射线原点最近**的那个命中点。
        /// </summary>
        public static bool TryIntersectNearest(
            Ray3 ray,
            Plane3[] planes,
            out Vector3Position point,
            out int planeIndex)
        {
            point = Vector3Position.Zero;
            planeIndex = -1;

            if (planes == null || planes.Length == 0)
            {
                return false;
            }

            var best = float.MaxValue;
            for (var i = 0; i < planes.Length; i++)
            {
                float distance;
                if (!TryIntersect(ray, planes[i], out distance))
                {
                    continue;
                }

                if (distance < best)
                {
                    best = distance;
                    planeIndex = i;
                }
            }

            if (planeIndex < 0)
            {
                return false;
            }

            return TryIntersectAt(ray, planes[planeIndex], out point);
        }

        private static float Mathf(float v)
        {
            return v < 0f ? -v : v;
        }
    }
}
