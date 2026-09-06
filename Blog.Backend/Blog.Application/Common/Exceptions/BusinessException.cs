using Blog.Application.Common;

namespace Blog.Application.Common.Exceptions
{
    /// <summary>
    /// 业务异常：由全局异常中间件转换为对应 code 的 ApiResponse
    /// </summary>
    public class BusinessException : Exception
    {
        public int Code { get; }

        public BusinessException(string message, int code = ErrorCodes.BusinessRule) : base(message)
        {
            Code = code;
        }
    }
}
