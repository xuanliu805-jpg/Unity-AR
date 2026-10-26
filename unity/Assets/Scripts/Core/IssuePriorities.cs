namespace UnityAR.Core
{
    /// <summary>
    /// 巡检问题的优先级。与后端 <c>priority</c> 字段取值严格一致：low / medium / high。
    /// 本文件刻意不引用 UnityEngine，因此可以被 Unity 与普通 .NET 测试工程共同编译。
    /// </summary>
    public static class IssuePriorities
    {
        public const string Low = "low";
        public const string Medium = "medium";
        public const string High = "high";

        /// <summary>所有合法优先级，写入下拉框用。</summary>
        public static readonly string[] All = { Low, Medium, High };

        /// <summary>严格判断字符串是否为合法优先级（不做大小写折叠、不裁剪空白）。</summary>
        public static bool IsValid(string value)
        {
            return value == Low || value == Medium || value == High;
        }

        /// <summary>中文显示名，用于 Unity 界面下拉框。</summary>
        public static string DisplayName(string value)
        {
            switch (value)
            {
                case Low: return "低";
                case Medium: return "中";
                case High: return "高";
                default: return value;
            }
        }

        /// <summary>下拉框显示文案数组，顺序与 <see cref="All"/> 一致。</summary>
        public static string[] DisplayNames()
        {
            return new[] { DisplayName(Low), DisplayName(Medium), DisplayName(High) };
        }
    }
}
