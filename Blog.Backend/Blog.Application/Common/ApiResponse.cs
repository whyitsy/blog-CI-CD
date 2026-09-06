namespace Blog.Application.Common
{
    /// <summary>
    /// 统一 API 响应体：{ "code": 0, "data": {}, "message": "ok" }
    /// </summary>
    public class ApiResponse<T>
    {
        public int Code { get; init; }
        public string Message { get; init; } = "ok";
        public T? Data { get; init; }

        public static ApiResponse<T> Ok(T data) => new() { Code = ErrorCodes.Ok, Data = data };

        public static ApiResponse<T> Fail(int code, string message, T? data = default) =>
            new() { Code = code, Message = message, Data = data };
    }

    public static class ApiResponse
    {
        /// <summary>无数据的成功响应</summary>
        public static ApiResponse<object?> Ok() => new() { Code = ErrorCodes.Ok };

        public static ApiResponse<object?> Fail(int code, string message) =>
            new() { Code = code, Message = message };
    }
}
