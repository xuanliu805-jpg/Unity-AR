using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>优先级 / 状态枚举与后端保持一致的测试。</summary>
    [TestFixture]
    public class IssueEnumsTests
    {
        [Test]
        public void 优先级集合与后端一致()
        {
            Assert.That(IssuePriorities.All.Length, Is.EqualTo(3));
            Assert.That(IssuePriorities.All[0], Is.EqualTo("low"));
            Assert.That(IssuePriorities.All[1], Is.EqualTo("medium"));
            Assert.That(IssuePriorities.All[2], Is.EqualTo("high"));
        }

        [Test]
        public void 状态集合与后端一致()
        {
            Assert.That(IssueStatuses.All.Length, Is.EqualTo(3));
            Assert.That(IssueStatuses.All[0], Is.EqualTo("open"));
            Assert.That(IssueStatuses.All[1], Is.EqualTo("in_progress"));
            Assert.That(IssueStatuses.All[2], Is.EqualTo("resolved"));
        }

        [Test]
        public void 非法优先级被拒绝()
        {
            Assert.That(IssuePriorities.IsValid("urgent"), Is.False);
            Assert.That(IssuePriorities.IsValid("LOW"), Is.False);
            Assert.That(IssuePriorities.IsValid(" low"), Is.False);
            Assert.That(IssuePriorities.IsValid(""), Is.False);
            Assert.That(IssuePriorities.IsValid(null), Is.False);
        }

        [Test]
        public void 非法状态被拒绝()
        {
            Assert.That(IssueStatuses.IsValid("closed"), Is.False);
            Assert.That(IssueStatuses.IsValid("OPEN"), Is.False);
            Assert.That(IssueStatuses.IsValid(""), Is.False);
            Assert.That(IssueStatuses.IsValid(null), Is.False);
        }

        [Test]
        public void 优先级显示名是中文()
        {
            Assert.That(IssuePriorities.DisplayName("low"), Is.EqualTo("低"));
            Assert.That(IssuePriorities.DisplayName("medium"), Is.EqualTo("中"));
            Assert.That(IssuePriorities.DisplayName("high"), Is.EqualTo("高"));
        }

        [Test]
        public void 状态显示名是中文()
        {
            Assert.That(IssueStatuses.DisplayName("open"), Is.EqualTo("待处理"));
            Assert.That(IssueStatuses.DisplayName("in_progress"), Is.EqualTo("处理中"));
            Assert.That(IssueStatuses.DisplayName("resolved"), Is.EqualTo("已解决"));
        }

        [Test]
        public void 显示名数组与取值数组一一对应()
        {
            var names = IssuePriorities.DisplayNames();
            Assert.That(names.Length, Is.EqualTo(IssuePriorities.All.Length));
            for (var i = 0; i < names.Length; i++)
            {
                Assert.That(names[i], Is.EqualTo(IssuePriorities.DisplayName(IssuePriorities.All[i])));
            }

            var statusNames = IssueStatuses.DisplayNames();
            Assert.That(statusNames.Length, Is.EqualTo(IssueStatuses.All.Length));
            for (var i = 0; i < statusNames.Length; i++)
            {
                Assert.That(statusNames[i], Is.EqualTo(IssueStatuses.DisplayName(IssueStatuses.All[i])));
            }
        }
    }
}
