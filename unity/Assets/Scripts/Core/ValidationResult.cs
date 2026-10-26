namespace UnityAR.Core
{
    /// <summary>
    /// 校验结果。<see cref="IsValid"/> 为 false 时，<see cref="Field"/> 指出出错的字段，
    /// <see cref="Message"/> 是可以直接显示给巡检员的中文提示。
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid;
        public string Field;
        public string Message;

        public ValidationResult(bool isValid, string field, string message)
        {
            IsValid = isValid;
            Field = field;
            Message = message;
        }

        public static ValidationResult Ok()
        {
            return new ValidationResult(true, null, null);
        }

        public static ValidationResult Fail(string field, string message)
        {
            return new ValidationResult(false, field, message);
        }
    }
}
