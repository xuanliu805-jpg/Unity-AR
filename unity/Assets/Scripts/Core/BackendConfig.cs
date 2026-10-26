using System;

namespace UnityAR.Core
{
    /// <summary>
    /// 后端地址配置。真机上后端通常跑在开发机的局域网 IP 上（例如 http://192.168.1.20:8080），
    /// 不能写死 localhost，所以这里只做「规范化」这一件事，具体取值由 Unity 侧负责。
    /// </summary>
    public static class BackendConfig
    {
        public const string DefaultBaseUrl = "http://localhost:8080";

        /// <summary>
        /// 规范化后端地址：去掉末尾多余的 '/'，空值回退到默认地址。
        /// </summary>
        public static string NormalizeBaseUrl(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return DefaultBaseUrl;
            }

            var url = raw.Trim();
            if (url.Length == 0)
            {
                return DefaultBaseUrl;
            }

            while (url.EndsWith("/", StringComparison.Ordinal))
            {
                url = url.Substring(0, url.Length - 1);
            }

            return url.Length == 0 ? DefaultBaseUrl : url;
        }

        /// <summary>拼接成完整 URL。</summary>
        public static string BuildUrl(string baseUrl, string path)
        {
            var b = NormalizeBaseUrl(baseUrl);
            if (string.IsNullOrEmpty(path))
            {
                return b;
            }

            if (!path.StartsWith("/", StringComparison.Ordinal))
            {
                path = "/" + path;
            }

            return b + path;
        }
    }
}
