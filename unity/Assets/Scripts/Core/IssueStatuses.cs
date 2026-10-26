namespace UnityAR.Core
{
    /// <summary>
    /// 巡检问题的处理状态。与后端 <c>status</c> 字段取值严格一致：open / in_progress / resolved。
    /// </summary>
    public static class IssueStatuses
    {
        public const string Open = "open";
        public const string InProgress = "in_progress";
        public const string Resolved = "resolved";

        /// <summary>所有合法状态。</summary>
        public static readonly string[] All = { Open, InProgress, Resolved };

        /// <summary>严格判断字符串是否为合法状态。</summary>
        public static bool IsValid(string value)
        {
            return value == Open || value == InProgress || value == Resolved;
        }

        /// <summary>中文显示名。</summary>
        public static string DisplayName(string value)
        {
            switch (value)
            {
                case Open: return "待处理";
                case InProgress: return "处理中";
                case Resolved: return "已解决";
                default: return value;
            }
        }

        /// <summary>下拉框显示文案数组，顺序与 <see cref="All"/> 一致。</summary>
        public static string[] DisplayNames()
        {
            return new[] { DisplayName(Open), DisplayName(InProgress), DisplayName(Resolved) };
        }
    }
}
