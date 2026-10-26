using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>接口路径与后端地址拼接的测试。</summary>
    [TestFixture]
    public class ApiEndpointsTests
    {
        [Test]
        public void 健康检查路径固定()
        {
            Assert.That(ApiEndpoints.Health, Is.EqualTo("/api/health"));
        }

        [Test]
        public void 问题列表路径固定()
        {
            Assert.That(ApiEndpoints.Issues, Is.EqualTo("/api/issues"));
        }

        [Test]
        public void 状态路径包含问题ID()
        {
            Assert.That(ApiEndpoints.IssueStatus("ISS-123"), Is.EqualTo("/api/issues/ISS-123/status"));
        }

        [Test]
        public void 状态路径对特殊字符做转义()
        {
            var path = ApiEndpoints.IssueStatus("ISS 1/2");

            Assert.That(path, Does.Not.Contain(" "));
            Assert.That(path, Does.Not.Contain("/2"));
        }

        [Test]
        public void 空ID不会让路径消失()
        {
            Assert.That(ApiEndpoints.IssueStatus(null), Is.EqualTo("/api/issues//status"));
        }

        [Test]
        public void 空后端地址回落到默认地址()
        {
            Assert.That(BackendConfig.NormalizeBaseUrl(null), Is.EqualTo(BackendConfig.DefaultBaseUrl));
            Assert.That(BackendConfig.NormalizeBaseUrl(""), Is.EqualTo(BackendConfig.DefaultBaseUrl));
            Assert.That(BackendConfig.NormalizeBaseUrl("   "), Is.EqualTo(BackendConfig.DefaultBaseUrl));
        }

        [Test]
        public void 后端地址去掉末尾斜杠()
        {
            Assert.That(BackendConfig.NormalizeBaseUrl("http://192.168.1.20:8080/"), Is.EqualTo("http://192.168.1.20:8080"));
            Assert.That(BackendConfig.NormalizeBaseUrl("http://192.168.1.20:8080///"), Is.EqualTo("http://192.168.1.20:8080"));
        }

        [Test]
        public void 后端地址去掉首尾空白()
        {
            Assert.That(BackendConfig.NormalizeBaseUrl("  http://10.0.0.5:8080  "), Is.EqualTo("http://10.0.0.5:8080"));
        }

        [Test]
        public void 拼接完整URL()
        {
            Assert.That(BackendConfig.BuildUrl("http://10.0.0.5:8080/", ApiEndpoints.Issues),
                Is.EqualTo("http://10.0.0.5:8080/api/issues"));
        }

        [Test]
        public void 拼接时自动补斜杠()
        {
            Assert.That(BackendConfig.BuildUrl("http://10.0.0.5:8080", "api/health"),
                Is.EqualTo("http://10.0.0.5:8080/api/health"));
        }

        [Test]
        public void 真机局域网地址可以正常拼接()
        {
            var url = BackendConfig.BuildUrl("192.168.1.20:8080", ApiEndpoints.IssueStatus("ISS-1"));

            Assert.That(url, Is.EqualTo("192.168.1.20:8080/api/issues/ISS-1/status"));
        }
    }
}
