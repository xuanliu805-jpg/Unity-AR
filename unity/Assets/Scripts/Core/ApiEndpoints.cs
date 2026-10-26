using System;

namespace UnityAR.Core
{
    /// <summary>
    /// 后端接口路径。集中在一处，避免各脚本各写一份字符串导致拼错。
    /// </summary>
    public static class ApiEndpoints
    {
        public const string Health = "/api/health";
        public const string Issues = "/api/issues";

        /// <summary>修改某条问题状态的路径。</summary>
        public static string IssueStatus(string id)
        {
            return "/api/issues/" + Uri.EscapeDataString(id ?? string.Empty) + "/status";
        }

        /// <summary>删除某条问题的路径。</summary>
        public static string Issue(string id)
        {
            return "/api/issues/" + Uri.EscapeDataString(id ?? string.Empty);
        }
    }
}
