using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Blog.Tests.Infrastructure;

/// <summary>
/// 集成测试用的 HTTP 客户端包装：统一处理统一响应体、认证头与 JSON 解析。
///
/// 后端所有接口都返回 <c>{ code, message, data }</c>，因此这里集中解包，
/// 让测试代码只关心业务语义（状态码、code、data）。
/// </summary>
public sealed class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;

    public ApiClient(HttpClient http) => _http = http;

    /// <summary>带认证的请求（token 为空则匿名）</summary>
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token = null, object? body = null)
    {
        var req = new HttpRequestMessage(method, path);
        if (token is not null)
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            req.Content = JsonContent.Create(body);

        return _http.SendAsync(req);
    }

    public async Task<(HttpStatusCode Status, int Code, T? Data, string Message)> CallAsync<T>(
        HttpMethod method, string path, string? token = null, object? body = null)
    {
        var res = await SendAsync(method, path, token, body);
        var raw = await res.Content.ReadAsStringAsync();

        if (string.IsNullOrWhiteSpace(raw))
            return (res.StatusCode, -1, default, string.Empty);

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var code = root.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
        var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
        var data = root.TryGetProperty("data", out var d) && d.ValueKind != JsonValueKind.Null
            ? d.Deserialize<T>(Json)
            : default;

        return (res.StatusCode, code, data, message);
    }

    /// <summary>登录并返回 token</summary>
    public async Task<string> LoginAsync(string email, string password)
    {
        var (status, code, data, message) = await CallAsync<LoginResponseDto>(
            HttpMethod.Post, "/api/auth/login", body: new { email, password });

        if (status != HttpStatusCode.OK || code != 0 || data is null)
            throw new InvalidOperationException($"登录失败 status={status} code={code} message={message}");

        return data.Token;
    }
}

public sealed record LoginResponseDto(string Token, DateTimeOffset ExpiresAt, string Role, CurrentUserDto User);

public sealed record CurrentUserDto(Guid Id, string Email, string Role, bool IsActive, Guid? AuthorId, string? AuthorName);

/// <summary>统一业务码（与 Blog.Application/Common/ErrorCodes.cs 对齐）</summary>
public static class Codes
{
    public const int Ok = 0;
    public const int InvalidArgument = 4001;
    public const int BusinessRule = 4002;
    public const int DuplicateResource = 4003;
    public const int Unauthorized = 4010;
    public const int Forbidden = 4030;
    public const int NotFound = 4040;
    public const int ConcurrencyConflict = 4090;

    /// <summary>请求体过大（如上传文件超过体积限制）。HTTP 状态码为 413</summary>
    public const int PayloadTooLarge = 4130;

    /// <summary>系统异常。断言「不能是 5xx」时要用到</summary>
    public const int InternalError = 5000;
}
