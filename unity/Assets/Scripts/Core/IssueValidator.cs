namespace UnityAR.Core
{
    /// <summary>
    /// 上报前的本地校验。规则与 Go 后端的校验保持一致，
    /// 目的是让巡检员**当场**就能看到错误，而不是提交后被后端拒绝。
    /// </summary>
    public static class IssueValidator
    {
        /// <summary>标题最大长度，避免有人把整段话粘进标题。</summary>
        public const int MaxTitleLength = 100;

        /// <summary>描述最大长度。</summary>
        public const int MaxDescriptionLength = 500;

        /// <summary>
        /// 校验草稿。返回第一个发现的问题，字段顺序：标题 → 优先级 → 位置。
        /// </summary>
        public static ValidationResult Validate(IssueDraft draft)
        {
            if (draft == null)
            {
                return ValidationResult.Fail("title", "没有可提交的问题内容");
            }

            var title = (draft.Title ?? string.Empty).Trim();
            if (title.Length == 0)
            {
                return ValidationResult.Fail("title", "标题不能为空");
            }

            if (title.Length > MaxTitleLength)
            {
                return ValidationResult.Fail("title", "标题不能超过 " + MaxTitleLength + " 个字符");
            }

            var description = (draft.Description ?? string.Empty).Trim();
            if (description.Length > MaxDescriptionLength)
            {
                return ValidationResult.Fail("description", "描述不能超过 " + MaxDescriptionLength + " 个字符");
            }

            if (!IssuePriorities.IsValid(draft.Priority))
            {
                return ValidationResult.Fail("priority", "优先级必须是 low、medium 或 high 之一");
            }

            if (!draft.Position.IsFinite)
            {
                return ValidationResult.Fail("position", "位置坐标无效，请重新放置标记");
            }

            return ValidationResult.Ok();
        }
    }
}
