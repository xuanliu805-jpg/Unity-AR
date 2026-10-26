using System;
using System.Text;

namespace UnityAR.Core
{
    /// <summary>
    /// 后端响应的极简解析器。
    ///
    /// 为什么不用现成的 JSON 库：
    /// Unity 端只需要从响应里取出「新建的问题 ID」「错误提示」「错误字段」三样东西，
    /// 引入 Newtonsoft 会给 IL2CPP 打包体积和依赖管理带来额外成本。
    /// 这里只做定向查找 + 转义还原，足以覆盖后端约定的固定响应格式，
    /// 并且能被单元测试完整覆盖。
    /// </summary>
    public static class ApiResponseParser
    {
        /// <summary>从 {"issue":{"id":"..."}} 中取出问题 ID。</summary>
        public static bool TryExtractIssueId(string json, out string id)
        {
            id = null;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            var marker = "\"issue\"";
            var issueIndex = json.IndexOf(marker, StringComparison.Ordinal);
            if (issueIndex < 0)
            {
                return false;
            }

            var idMarker = "\"id\"";
            var idIndex = json.IndexOf(idMarker, issueIndex, StringComparison.Ordinal);
            if (idIndex < 0)
            {
                return false;
            }

            return TryReadStringValue(json, idIndex + idMarker.Length, out id);
        }

        /// <summary>从 {"error":{"message":"..."}} 中取出中文错误提示。</summary>
        public static bool TryExtractErrorMessage(string json, out string message)
        {
            message = null;
            var errorIndex = IndexOfErrorObject(json);
            if (errorIndex < 0)
            {
                return false;
            }

            var marker = "\"message\"";
            var index = json.IndexOf(marker, errorIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            return TryReadStringValue(json, index + marker.Length, out message);
        }

        /// <summary>从 {"error":{"field":"..."}} 中取出出错的字段名。</summary>
        public static bool TryExtractErrorField(string json, out string field)
        {
            field = null;
            var errorIndex = IndexOfErrorObject(json);
            if (errorIndex < 0)
            {
                return false;
            }

            var marker = "\"field\"";
            var index = json.IndexOf(marker, errorIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            return TryReadStringValue(json, index + marker.Length, out field);
        }

        /// <summary>判断健康检查响应是否表示服务正常。</summary>
        public static bool IsHealthy(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            var marker = "\"status\"";
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return false;
            }

            string status;
            if (!TryReadStringValue(json, index + marker.Length, out status))
            {
                return false;
            }

            return status == "ok";
        }

        /// <summary>定位 "error" 对象所在位置；没有则返回 -1。</summary>
        private static int IndexOfErrorObject(string json)
        {
            var marker = "\"error\"";
            var index = json.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
            {
                return -1;
            }

            var brace = json.IndexOf('{', index);
            return brace < 0 ? -1 : brace;
        }

        /// <summary>
        /// 从 <c>start</c> 处开始读取一个 JSON 字符串值（跳过冒号与空白）。
        /// </summary>
        private static bool TryReadStringValue(string json, int start, out string value)
        {
            value = null;

            var i = start;
            while (i < json.Length && (json[i] == ':' || char.IsWhiteSpace(json[i])))
            {
                i++;
            }

            if (i >= json.Length || json[i] != '"')
            {
                return false;
            }

            i++;
            var sb = new StringBuilder();
            while (i < json.Length)
            {
                var c = json[i];
                if (c == '\\' && i + 1 < json.Length)
                {
                    i++;
                    var escaped = json[i];
                    switch (escaped)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            // \uXXXX：转成 char（只处理 BMP 内的代理对以外的常见情况）
                            if (i + 4 < json.Length)
                            {
                                var hex = json.Substring(i + 1, 4);
                                int code;
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                                    System.Globalization.CultureInfo.InvariantCulture, out code))
                                {
                                    sb.Append((char)code);
                                    i += 4;
                                }
                            }
                            break;
                        default: sb.Append(escaped); break;
                    }
                    i++;
                    continue;
                }

                if (c == '"')
                {
                    value = sb.ToString();
                    return true;
                }

                sb.Append(c);
                i++;
            }

            return false;
        }
    }
}
