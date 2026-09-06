namespace Blog.Application.Common
{
    /// <summary>统一响应码</summary>
    public static class ErrorCodes
    {
        /// <summary>成功</summary>
        public const int Ok = 0;

        /// <summary>参数校验失败</summary>
        public const int InvalidArgument = 4001;

        /// <summary>业务规则不满足（如分类名重复）</summary>
        public const int BusinessRule = 4002;

        /// <summary>资源不存在</summary>
        public const int NotFound = 4040;

        /// <summary>乐观锁并发冲突</summary>
        public const int ConcurrencyConflict = 4090;

        /// <summary>触发限流</summary>
        public const int RateLimited = 4091;

        /// <summary>系统异常</summary>
        public const int InternalError = 5000;
    }
}
