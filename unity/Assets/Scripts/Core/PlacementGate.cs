namespace UnityAR.Core
{
    /// <summary>放置被拒绝的原因码。</summary>
    public static class PlacementReasons
    {
        public const string Ok = "ok";
        public const string Disabled = "disabled";
        public const string PointerOverUI = "pointer_over_ui";
        public const string NoPlane = "no_plane";
        public const string NotATap = "not_a_tap";
    }

    /// <summary>
    /// 一次放置判定的输入条件。
    /// </summary>
    public class PlacementContext
    {
        /// <summary>放置总开关。填写面板打开时置 false，避免在填表过程中误放标记。</summary>
        public bool PlacementEnabled;

        /// <summary>本次触点是否落在 UI 控件上（按钮 / 输入框 / 下拉框）。</summary>
        public bool PointerOverUI;

        /// <summary>当前是否已经检测到可用的水平平面。</summary>
        public bool PlaneTracked;

        /// <summary>本次是否是一次「点击」（按下瞬间），而不是拖动。</summary>
        public bool TapBegan;
    }

    /// <summary>判定结果。</summary>
    public class PlacementDecision
    {
        public bool ShouldPlace;
        public string Reason;

        public PlacementDecision(bool shouldPlace, string reason)
        {
            ShouldPlace = shouldPlace;
            Reason = reason;
        }
    }

    /// <summary>
    /// 「这次点击要不要放置标记」的判定规则。
    ///
    /// 抽成纯逻辑是为了能在没有真机的情况下自动化验证任务书 3.1 的关键约束：
    /// 点击按钮或输入框时不能误放标记。
    ///
    /// 判定优先级（从高到低）：
    ///   1. 全局禁用（填写面板打开中）
    ///   2. 触点落在 UI 上
    ///   3. 没有检测到平面
    ///   4. 不是点击手势
    /// </summary>
    public static class PlacementGate
    {
        public static PlacementDecision Evaluate(PlacementContext context)
        {
            if (context == null)
            {
                return new PlacementDecision(false, PlacementReasons.Disabled);
            }

            if (!context.PlacementEnabled)
            {
                return new PlacementDecision(false, PlacementReasons.Disabled);
            }

            if (context.PointerOverUI)
            {
                return new PlacementDecision(false, PlacementReasons.PointerOverUI);
            }

            if (!context.PlaneTracked)
            {
                return new PlacementDecision(false, PlacementReasons.NoPlane);
            }

            if (!context.TapBegan)
            {
                return new PlacementDecision(false, PlacementReasons.NotATap);
            }

            return new PlacementDecision(true, PlacementReasons.Ok);
        }

        /// <summary>把原因码翻译成可以直接显示给巡检员的中文提示。</summary>
        public static string Describe(string reason)
        {
            switch (reason)
            {
                case PlacementReasons.Ok:
                    return "已放置标记";
                case PlacementReasons.Disabled:
                    return "填写面板打开中，关闭后再放置标记";
                case PlacementReasons.PointerOverUI:
                    return "点击了界面控件，已忽略该操作";
                case PlacementReasons.NoPlane:
                    return "还没有检测到地面平面，请移动手机扫描地面";
                case PlacementReasons.NotATap:
                    return "不是点击操作，已忽略";
                default:
                    return "操作已忽略";
            }
        }
    }
}
