using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>
    /// 「什么时候允许放置标记」的判定逻辑测试。
    ///
    /// 这条规则直接对应任务书 3.1 的最后一条：
    /// 「点击按钮或输入框时，不应误在 AR 场景中放置标记」。
    /// 判定逻辑被刻意抽到不依赖 UnityEngine 的 Core 里，才能在真机之外被自动化测试覆盖。
    /// </summary>
    [TestFixture]
    public class PlacementGateTests
    {
        private static PlacementContext Ready()
        {
            return new PlacementContext
            {
                PlacementEnabled = true,
                PointerOverUI = false,
                PlaneTracked = true,
                TapBegan = true
            };
        }

        [Test]
        public void 一切就绪时允许放置()
        {
            var decision = PlacementGate.Evaluate(Ready());

            Assert.That(decision.ShouldPlace, Is.True);
            Assert.That(decision.Reason, Is.EqualTo(PlacementReasons.Ok));
        }

        [Test]
        public void 触点落在界面上时不允许放置()
        {
            var ctx = Ready();
            ctx.PointerOverUI = true;

            var decision = PlacementGate.Evaluate(ctx);

            Assert.That(decision.ShouldPlace, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(PlacementReasons.PointerOverUI));
        }

        [Test]
        public void 面板打开导致放置被禁用时不允许放置()
        {
            var ctx = Ready();
            ctx.PlacementEnabled = false;

            var decision = PlacementGate.Evaluate(ctx);

            Assert.That(decision.ShouldPlace, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(PlacementReasons.Disabled));
        }

        [Test]
        public void 没有检测到平面时不允许放置()
        {
            var ctx = Ready();
            ctx.PlaneTracked = false;

            var decision = PlacementGate.Evaluate(ctx);

            Assert.That(decision.ShouldPlace, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(PlacementReasons.NoPlane));
        }

        [Test]
        public void 非点击手势不允许放置()
        {
            var ctx = Ready();
            ctx.TapBegan = false;

            var decision = PlacementGate.Evaluate(ctx);

            Assert.That(decision.ShouldPlace, Is.False);
            Assert.That(decision.Reason, Is.EqualTo(PlacementReasons.NotATap));
        }

        [Test]
        public void 界面遮挡优先于平面缺失来报告原因()
        {
            // 巡检员点的是按钮，此时即便还没识别出平面，也要明确告诉用户「点在界面上了」
            var ctx = Ready();
            ctx.PointerOverUI = true;
            ctx.PlaneTracked = false;

            var decision = PlacementGate.Evaluate(ctx);

            Assert.That(decision.Reason, Is.EqualTo(PlacementReasons.PointerOverUI));
        }

        [Test]
        public void 禁用优先于其它原因()
        {
            var ctx = Ready();
            ctx.PlacementEnabled = false;
            ctx.PointerOverUI = true;
            ctx.PlaneTracked = false;
            ctx.TapBegan = false;

            var decision = PlacementGate.Evaluate(ctx);

            Assert.That(decision.Reason, Is.EqualTo(PlacementReasons.Disabled));
        }

        [Test]
        public void 每种被拒绝的原因都有中文提示()
        {
            foreach (var reason in new[]
            {
                PlacementReasons.Disabled,
                PlacementReasons.PointerOverUI,
                PlacementReasons.NoPlane,
                PlacementReasons.NotATap
            })
            {
                Assert.That(PlacementGate.Describe(reason), Is.Not.Empty, "原因 " + reason + " 需要有中文提示");
                Assert.That(PlacementGate.Describe(reason), Does.Not.Contain(reason),
                    "提示文案里不应该直接暴露内部原因码 " + reason);
            }
        }

        [Test]
        public void 空上下文按拒绝处理而不是抛异常()
        {
            var decision = PlacementGate.Evaluate(null);

            Assert.That(decision.ShouldPlace, Is.False);
        }
    }
}
