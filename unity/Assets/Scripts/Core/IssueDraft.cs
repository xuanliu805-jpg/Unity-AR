namespace UnityAR.Core
{
    /// <summary>
    /// 巡检员在手机上填写的问题草稿。字段与后端 <c>Issue</c> 对齐，
    /// 但 <c>id</c> / <c>status</c> / <c>createdAt</c> / <c>updatedAt</c> 由后端生成，客户端不填写。
    /// </summary>
    public class IssueDraft
    {
        public string Title = string.Empty;
        public string Description = string.Empty;
        public string Priority = IssuePriorities.Medium;
        public Vector3Position Position = Vector3Position.Zero;

        public IssueDraft()
        {
        }

        public IssueDraft(string title, string description, string priority, Vector3Position position)
        {
            Title = title;
            Description = description;
            Priority = priority;
            Position = position;
        }
    }
}
