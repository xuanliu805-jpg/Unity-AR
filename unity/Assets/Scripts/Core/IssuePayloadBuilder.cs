using System.Globalization;
using System.Text;

namespace UnityAR.Core
{
    /// <summary>
    /// 把 <see cref="IssueDraft"/> 序列化成后端约定的 JSON 请求体。
    /// 自己手写序列化是为了让这段代码不依赖 UnityEngine.JsonUtility，
    /// 从而能在普通 .NET 测试工程里直接断言「请求体长什么样」。
    /// </summary>
    public static class IssuePayloadBuilder
    {
        /// <summary>
        /// 生成 POST /api/issues 的请求体。
        /// 只包含 title / description / priority / position —— id、status、时间戳都由后端生成，客户端不传。
        /// </summary>
        public static string Build(IssueDraft draft)
        {
            if (draft == null)
            {
                throw new System.ArgumentNullException("draft");
            }

            var sb = new StringBuilder();
            sb.Append('{');
            AppendString(sb, "title", (draft.Title ?? string.Empty).Trim());
            sb.Append(',');
            AppendString(sb, "description", (draft.Description ?? string.Empty).Trim());
            sb.Append(',');
            AppendString(sb, "priority", draft.Priority ?? string.Empty);
            sb.Append(',');
            sb.Append("\"position\":{");
            sb.Append("\"x\":").Append(Number(draft.Position.X)).Append(',');
            sb.Append("\"y\":").Append(Number(draft.Position.Y)).Append(',');
            sb.Append("\"z\":").Append(Number(draft.Position.Z));
            sb.Append('}');
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendString(StringBuilder sb, string name, string value)
        {
            sb.Append('"').Append(name).Append("\":\"");
            AppendEscaped(sb, value);
            sb.Append('"');
        }

        /// <summary>
        /// JSON 字符串转义。中文按原样输出（UTF-8 传输），只处理必须转义的字符。
        /// </summary>
        private static void AppendEscaped(StringBuilder sb, string value)
        {
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 数字格式化：使用不变文化，避免在中文 Windows 上把小数点写成逗号导致 JSON 解析失败。
        /// </summary>
        private static string Number(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "0";
            }
            return value.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
