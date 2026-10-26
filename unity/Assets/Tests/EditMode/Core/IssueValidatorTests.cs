using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>
    /// 表单校验的单元测试。与 Go 后端的校验规则一一对应：
    /// 客户端先拦一道，后端再兜一道，两边规则一致才不会出现「客户端放过、后端拒绝」。
    /// </summary>
    [TestFixture]
    public class IssueValidatorTests
    {
        private static IssueDraft ValidDraft()
        {
            return new IssueDraft("入口墙面破损", "左侧墙体存在裂缝", IssuePriorities.High,
                new Vector3Position(0.42f, 0.03f, 1.26f));
        }

        [Test]
        public void 合法草稿通过校验()
        {
            var result = IssueValidator.Validate(ValidDraft());
            Assert.That(result.IsValid, Is.True, "合法草稿应当通过校验");
        }

        [Test]
        public void 描述为空时依然通过校验()
        {
            var draft = ValidDraft();
            draft.Description = string.Empty;

            var result = IssueValidator.Validate(draft);

            Assert.That(result.IsValid, Is.True, "描述是可选的");
        }

        [Test]
        public void 标题为空时校验失败且指向title字段()
        {
            var draft = ValidDraft();
            draft.Title = string.Empty;

            var result = IssueValidator.Validate(draft);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Field, Is.EqualTo("title"));
            Assert.That(result.Message, Is.Not.Empty);
        }

        [Test]
        public void 标题为空白字符时校验失败()
        {
            var draft = ValidDraft();
            draft.Title = "   \t  ";

            var result = IssueValidator.Validate(draft);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Field, Is.EqualTo("title"));
        }

        [Test]
        public void 标题为null时校验失败()
        {
            var draft = ValidDraft();
            draft.Title = null;

            var result = IssueValidator.Validate(draft);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Field, Is.EqualTo("title"));
        }

        [Test]
        public void 标题超长时校验失败()
        {
            var draft = ValidDraft();
            draft.Title = new string('测', IssueValidator.MaxTitleLength + 1);

            var result = IssueValidator.Validate(draft);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Field, Is.EqualTo("title"));
        }

        [Test]
        public void 优先级非法时校验失败且指向priority字段()
        {
            foreach (var bad in new[] { "urgent", "LOW", "1", "", null })
            {
                var draft = ValidDraft();
                draft.Priority = bad;

                var result = IssueValidator.Validate(draft);

                Assert.That(result.IsValid, Is.False, "优先级 " + (bad ?? "null") + " 应当被拒绝");
                Assert.That(result.Field, Is.EqualTo("priority"));
            }
        }

        [Test]
        public void 三种合法优先级都通过校验()
        {
            foreach (var good in IssuePriorities.All)
            {
                var draft = ValidDraft();
                draft.Priority = good;

                var result = IssueValidator.Validate(draft);

                Assert.That(result.IsValid, Is.True, "优先级 " + good + " 应当被接受");
            }
        }

        [Test]
        public void 描述超长时校验失败()
        {
            var draft = ValidDraft();
            draft.Description = new string('描', IssueValidator.MaxDescriptionLength + 1);

            var result = IssueValidator.Validate(draft);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Field, Is.EqualTo("description"));
        }

        [Test]
        public void 坐标含NaN时校验失败()
        {
            var draft = ValidDraft();
            draft.Position = new Vector3Position(float.NaN, 0f, 0f);

            var result = IssueValidator.Validate(draft);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Field, Is.EqualTo("position"));
        }

        [Test]
        public void 坐标含无穷时校验失败()
        {
            var draft = ValidDraft();
            draft.Position = new Vector3Position(0f, float.PositiveInfinity, 0f);

            var result = IssueValidator.Validate(draft);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Field, Is.EqualTo("position"));
        }

        [Test]
        public void 空草稿校验失败而不是抛异常()
        {
            var result = IssueValidator.Validate(null);

            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Field, Is.EqualTo("title"));
        }
    }
}
