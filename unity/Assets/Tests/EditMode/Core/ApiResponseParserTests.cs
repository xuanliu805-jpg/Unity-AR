using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>
    /// 后端响应的解析测试。
    /// Unity 端需要在**不引入 JSON 库**的前提下从响应里取出问题 ID 和错误提示，
    /// 因此这里把解析逻辑放进 Core 并单独测试。
    /// </summary>
    [TestFixture]
    public class ApiResponseParserTests
    {
        [Test]
        public void 从成功响应里取出问题ID()
        {
            const string json = "{\"issue\":{\"id\":\"ISS-1-ABC\",\"title\":\"t\",\"status\":\"open\"}}";

            string id;
            var ok = ApiResponseParser.TryExtractIssueId(json, out id);

            Assert.That(ok, Is.True);
            Assert.That(id, Is.EqualTo("ISS-1-ABC"));
        }

        [Test]
        public void 从列表响应里取不到问题ID()
        {
            const string json = "{\"issues\":[{\"id\":\"ISS-1\"}],\"count\":1}";

            string id;
            var ok = ApiResponseParser.TryExtractIssueId(json, out id);

            Assert.That(ok, Is.False);
            Assert.That(id, Is.Null);
        }

        [Test]
        public void 空响应取不到问题ID()
        {
            string id;
            Assert.That(ApiResponseParser.TryExtractIssueId("", out id), Is.False);
            Assert.That(ApiResponseParser.TryExtractIssueId(null, out id), Is.False);
        }

        [Test]
        public void 从错误响应里取出中文提示()
        {
            const string json = "{\"error\":{\"code\":\"invalid_argument\",\"message\":\"标题(title)不能为空\",\"field\":\"title\"}}";

            string message;
            var ok = ApiResponseParser.TryExtractErrorMessage(json, out message);

            Assert.That(ok, Is.True);
            Assert.That(message, Is.EqualTo("标题(title)不能为空"));
        }

        [Test]
        public void 没有error字段时取不到提示()
        {
            string message;
            Assert.That(ApiResponseParser.TryExtractErrorMessage("{\"issue\":{}}", out message), Is.False);
            Assert.That(message, Is.Null);
        }

        [Test]
        public void 错误提示里的转义引号被还原()
        {
            const string json = "{\"error\":{\"code\":\"invalid_argument\",\"message\":\"标题 \\\"a\\\" 不能为空\"}}";

            string message;
            var ok = ApiResponseParser.TryExtractErrorMessage(json, out message);

            Assert.That(ok, Is.True);
            Assert.That(message, Does.Contain("\"a\""));
        }

        [Test]
        public void 能从错误响应里取出字段名()
        {
            const string json = "{\"error\":{\"code\":\"invalid_argument\",\"message\":\"x\",\"field\":\"priority\"}}";

            string field;
            Assert.That(ApiResponseParser.TryExtractErrorField(json, out field), Is.True);
            Assert.That(field, Is.EqualTo("priority"));
        }

        [Test]
        public void 健康检查响应被识别为可用()
        {
            Assert.That(ApiResponseParser.IsHealthy("{\"status\":\"ok\",\"service\":\"unity-ar-inspection\",\"issues\":0}"), Is.True);
            Assert.That(ApiResponseParser.IsHealthy("{\"status\":\"down\"}"), Is.False);
            Assert.That(ApiResponseParser.IsHealthy(""), Is.False);
            Assert.That(ApiResponseParser.IsHealthy(null), Is.False);
        }
    }
}
