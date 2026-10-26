using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>
    /// 请求体序列化的单元测试。重点是「坐标原样传给后端」和「特殊字符不会破坏 JSON」。
    /// </summary>
    [TestFixture]
    public class IssuePayloadBuilderTests
    {
        [Test]
        public void 生成的请求体包含全部四个字段()
        {
            var draft = new IssueDraft("入口墙面破损", "左侧墙体存在裂缝", IssuePriorities.High,
                new Vector3Position(0.42f, 0.03f, 1.26f));

            var json = IssuePayloadBuilder.Build(draft);

            Assert.That(json, Does.Contain("\"title\":\"入口墙面破损\""));
            Assert.That(json, Does.Contain("\"description\":\"左侧墙体存在裂缝\""));
            Assert.That(json, Does.Contain("\"priority\":\"high\""));
            Assert.That(json, Does.Contain("\"position\":{"));
            Assert.That(json, Does.Contain("\"x\":0.42"));
            Assert.That(json, Does.Contain("\"y\":0.03"));
            Assert.That(json, Does.Contain("\"z\":1.26"));
        }

        [Test]
        public void 请求体不包含由后端生成的字段()
        {
            var draft = new IssueDraft("标题", "描述", IssuePriorities.Low, Vector3Position.Zero);

            var json = IssuePayloadBuilder.Build(draft);

            Assert.That(json, Does.Not.Contain("\"id\""));
            Assert.That(json, Does.Not.Contain("\"status\""));
            Assert.That(json, Does.Not.Contain("\"createdAt\""));
            Assert.That(json, Does.Not.Contain("\"updatedAt\""));
        }

        [Test]
        public void 标题与描述首尾空白被去掉()
        {
            var draft = new IssueDraft("  入口墙面破损  ", "  裂缝  ", IssuePriorities.Medium, Vector3Position.Zero);

            var json = IssuePayloadBuilder.Build(draft);

            Assert.That(json, Does.Contain("\"title\":\"入口墙面破损\""));
            Assert.That(json, Does.Contain("\"description\":\"裂缝\""));
        }

        [Test]
        public void 标题中的双引号被转义()
        {
            var draft = new IssueDraft("墙上有\"裂缝\"", string.Empty, IssuePriorities.Low, Vector3Position.Zero);

            var json = IssuePayloadBuilder.Build(draft);

            Assert.That(json, Does.Contain("\\\"裂缝\\\""));
            Assert.That(json, Does.Not.Contain("\"裂缝\""));
        }

        [Test]
        public void 标题中的反斜杠被转义()
        {
            var draft = new IssueDraft("a\\b", string.Empty, IssuePriorities.Low, Vector3Position.Zero);

            var json = IssuePayloadBuilder.Build(draft);

            Assert.That(json, Does.Contain("\"title\":\"a\\\\b\""));
        }

        [Test]
        public void 描述中的换行被转义成n()
        {
            var draft = new IssueDraft("标题", "第一行\n第二行", IssuePriorities.Low, Vector3Position.Zero);

            var json = IssuePayloadBuilder.Build(draft);

            Assert.That(json, Does.Contain("\\n"));
            Assert.That(json, Does.Not.Contain("\n"));
        }

        [Test]
        public void 坐标为零时不会输出多余精度()
        {
            var draft = new IssueDraft("标题", string.Empty, IssuePriorities.Low, Vector3Position.Zero);

            var json = IssuePayloadBuilder.Build(draft);

            Assert.That(json, Does.Contain("\"x\":0"));
            Assert.That(json, Does.Contain("\"y\":0"));
            Assert.That(json, Does.Contain("\"z\":0"));
        }

        [Test]
        public void 负坐标可以被正常表达()
        {
            var draft = new IssueDraft("标题", string.Empty, IssuePriorities.Low, new Vector3Position(-1.5f, -0.25f, 2f));

            var json = IssuePayloadBuilder.Build(draft);

            Assert.That(json, Does.Contain("\"x\":-1.5"));
            Assert.That(json, Does.Contain("\"y\":-0.25"));
            Assert.That(json, Does.Contain("\"z\":2"));
        }

        [Test]
        public void 空草稿参数抛出异常()
        {
            Assert.Throws<System.ArgumentNullException>(() => IssuePayloadBuilder.Build(null));
        }
    }
}
