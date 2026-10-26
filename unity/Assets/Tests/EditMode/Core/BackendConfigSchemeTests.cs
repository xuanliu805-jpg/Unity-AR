using NUnit.Framework;
using UnityAR.Core;

namespace UnityAR.Tests.EditMode
{
    /// <summary>
    /// 后端地址规范化必须拒绝不合法输入。
    /// 曾经的 bug：while (url.EndsWith("/")) 会把 "http://" 削成 "http:"，
    /// 拼出来的 "http:/api/issues" 是非法 URL，真机上会直接连不上。
    /// </summary>
    [TestFixture]
    public class BackendConfigSchemeTests
    {
        [Test]
        public void 不带协议的地址回落到默认地址()
        {
            Assert.That(BackendConfig.NormalizeBaseUrl("192.168.1.20:8080"), Is.EqualTo(BackendConfig.DefaultBaseUrl));
        }

        [Test]
        public void 只有斜杠的地址回落到默认地址()
        {
            Assert.That(BackendConfig.NormalizeBaseUrl("///"), Is.EqualTo(BackendConfig.DefaultBaseUrl));
        }

        [Test]
        public void http协议被完整保留()
        {
            Assert.That(BackendConfig.NormalizeBaseUrl("http://192.168.1.20:8080"),
                Is.EqualTo("http://192.168.1.20:8080"));
        }

        [Test]
        public void https协议被完整保留()
        {
            Assert.That(BackendConfig.NormalizeBaseUrl("https://ar.example.com/"),
                Is.EqualTo("https://ar.example.com"));
        }

        [Test]
        public void 拼出来的URL不会出现丢斜杠的协议头()
        {
            var url = BackendConfig.BuildUrl("http://10.0.0.5:8080", ApiEndpoints.Issues);

            Assert.That(url, Does.StartWith("http://"));
            Assert.That(url, Is.EqualTo("http://10.0.0.5:8080/api/issues"));
        }
    }
}
