using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>
    /// 「射线 × 水平平面」求交的单元测试。
    ///
    /// 为什么要自己做这个而不用 ARRaycastManager：
    /// ARCore 的原生 raycast 在部分机型/驱动上会返回空结果（ARFoundation 的
    /// TrackableType.Planes 里含 ARCore 不支持的 PlaneWithinInfinity 标志位），
    /// 而巡检场景其实只需要「点地面」，用已经检测到的水平平面自己求交更稳、更可控。
    /// </summary>
    [TestFixture]
    public class RayPlaneIntersectionTests
    {
        private static Ray3 DownwardRay(float originY, float dirY = -1f)
        {
            return new Ray3
            {
                OriginX = 0f, OriginY = originY, OriginZ = 0f,
                DirX = 0f, DirY = dirY, DirZ = 0f
            };
        }

        private static Plane3 Ground(float y = 0f)
        {
            return new Plane3
            {
                Nx = 0f, Ny = 1f, Nz = 0f,
                Px = 0f, Py = y, Pz = 0f
            };
        }

        [Test]
        public void 垂直向下的射线命中地面()
        {
            var ray = DownwardRay(2f);
            var plane = Ground(0f);

            float distance;
            var hit = RayPlaneIntersection.TryIntersect(ray, plane, out distance);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void 命中点坐标正确()
        {
            var ray = new Ray3
            {
                OriginX = 1f, OriginY = 3f, OriginZ = -2f,
                DirX = 0f, DirY = -1f, DirZ = 0f
            };
            var plane = Ground(0.5f);

            Vector3Position point;
            var hit = RayPlaneIntersection.TryIntersectAt(ray, plane, out point);

            Assert.That(hit, Is.True);
            Assert.That(point.X, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(point.Y, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(point.Z, Is.EqualTo(-2f).Within(0.0001f));
        }

        [Test]
        public void 与平面平行的射线不命中()
        {
            var ray = new Ray3
            {
                OriginX = 0f, OriginY = 2f, OriginZ = 0f,
                DirX = 1f, DirY = 0f, DirZ = 0f
            };

            float distance;
            Assert.That(RayPlaneIntersection.TryIntersect(ray, Ground(0f), out distance), Is.False);
        }

        [Test]
        public void 朝反方向背离平面的射线不命中()
        {
            // 射线起点在地面上方，且朝上背着地面飞 → 永远不会打到地面
            var ray = DownwardRay(2f, 1f);

            float distance;
            Assert.That(RayPlaneIntersection.TryIntersect(ray, Ground(0f), out distance), Is.False);
        }

        [Test]
        public void 地面下方向上的射线会穿过地面并命中()
        {
            // 起点在地面下方、朝上走 → 会穿过地面，应当命中
            var ray = DownwardRay(-2f, 1f);

            float distance;
            var hit = RayPlaneIntersection.TryIntersect(ray, Ground(0f), out distance);

            Assert.That(hit, Is.True);
            Assert.That(distance, Is.EqualTo(2f).Within(0.0001f));
        }

        [Test]
        public void 多个平面时取最近的那个()
        {
            var near = Ground(1f);
            var far = Ground(0f);

            Vector3Position point;
            int index;
            var hit = RayPlaneIntersection.TryIntersectNearest(
                DownwardRay(5f), new[] { far, near }, out point, out index);

            Assert.That(hit, Is.True);
            Assert.That(index, Is.EqualTo(1));
            Assert.That(point.Y, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void 没有任何平面时不命中()
        {
            Vector3Position point;
            int index;
            Assert.That(
                RayPlaneIntersection.TryIntersectNearest(DownwardRay(5f), new Plane3[0], out point, out index),
                Is.False);
        }

        [Test]
        public void 斜射的射线也能算出命中点()
        {
            // 从 (0,2,0) 朝 (1,-1,0) 方向射出，与 y=0 的地面相交于 (2,0,0)
            var ray = new Ray3
            {
                OriginX = 0f, OriginY = 2f, OriginZ = 0f,
                DirX = 1f, DirY = -1f, DirZ = 0f
            };

            Vector3Position point;
            var hit = RayPlaneIntersection.TryIntersectAt(ray, Ground(0f), out point);

            Assert.That(hit, Is.True);
            Assert.That(point.X, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(point.Y, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void 法线朝下的平面也能命中()
        {
            // 有些设备/姿态下平面的法线可能朝下（天花板），求交不应该依赖法线方向
            var plane = new Plane3 { Nx = 0f, Ny = -1f, Nz = 0f, Px = 0f, Py = 3f, Pz = 0f };

            Vector3Position point;
            var hit = RayPlaneIntersection.TryIntersectAt(DownwardRay(5f), plane, out point);

            Assert.That(hit, Is.True);
            Assert.That(point.Y, Is.EqualTo(3f).Within(0.0001f));
        }
    }
}
