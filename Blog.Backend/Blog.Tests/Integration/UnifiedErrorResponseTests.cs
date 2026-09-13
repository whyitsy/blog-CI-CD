using System.Net;
using System.Text.Json;
using Blog.Tests.Infrastructure;

namespace Blog.Tests.Integration;

/// <summary>
/// 「统一响应体」的边界测试。
///
/// <para><b>为什么需要这一组</b></para>
/// docs/02 §4.1 与 docs/03 §1.1 都承诺「<b>所有</b>接口（含错误）返回 {code,message,data}」。
/// 但 <c>[ApiController]</c> 的自动模型校验发生在 Action <b>之前</b>，
/// 默认输出 RFC 7807 <c>ProblemDetails</c>（{type,title,status,errors,traceId}）——
/// 于是同一个 API 并存两种错误结构。
///
/// <para><b>怎么发现的</b></para>
/// 手工验证全文检索时漏传 <c>keyword</c>，返回的是：
/// <code>{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"keyword":["The keyword field is required."]},...}</code>
///
/// <para><b>为什么断言"没有 type/title"而不只是"有 code"</b></para>
/// 只断言 <c>code == 4001</c> 的话，一个「两种结构都塞进去」的实现也能变绿。
/// 必须钉住<b>不含</b> ProblemDetails 的字段。
/// </summary>
[Collection(BlogApiCollection.Name)]
public sealed class UnifiedErrorResponseTests
{
    private readonly ApiClient _api;

    public UnifiedErrorResponseTests(BlogApiFixture fixture) => _api = new ApiClient(fixture.CreateClient());

    /// <summary>断言响应体是统一结构，且不是 ProblemDetails</summary>
    private static void AssertUnifiedErrorBody(string raw, int expectedCode)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("code", out var code), $"缺少 code 字段：{raw}");
        Assert.Equal(expectedCode, code.GetInt32());

        Assert.True(root.TryGetProperty("message", out var message), $"缺少 message 字段：{raw}");
        Assert.False(string.IsNullOrWhiteSpace(message.GetString()), $"message 不能为空：{raw}");

        // 反面断言：ProblemDetails 的特征字段一个都不能出现
        Assert.False(root.TryGetProperty("type", out _), $"不应出现 ProblemDetails 的 type：{raw}");
        Assert.False(root.TryGetProperty("title", out _), $"不应出现 ProblemDetails 的 title：{raw}");
        Assert.False(root.TryGetProperty("errors", out _), $"不应出现 ProblemDetails 的 errors：{raw}");
        Assert.False(root.TryGetProperty("traceId", out _), $"不应出现 ProblemDetails 的 traceId：{raw}");
    }

    [Fact]
    public async Task 缺少必填查询参数_返回统一响应体()
    {
        // /api/posts/search 的 keyword 是必填的（PostsController.Search 的第一个参数无默认值）
        var res = await _api.SendAsync(HttpMethod.Get, "/api/posts/search");
        var raw = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        AssertUnifiedErrorBody(raw, Codes.InvalidArgument);
        Assert.Contains("keyword", raw); // 提示里要能指出是哪个参数
    }

    [Fact]
    public async Task 请求体类型不匹配_返回统一响应体()
    {
        // 给一个"合法 JSON 但不是对象"的请求体：模型绑定会失败，走的是同一条工厂路径
        var res = await _api.SendAsync(HttpMethod.Post, "/api/auth/login", body: "this-is-not-an-object");
        var raw = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        AssertUnifiedErrorBody(raw, Codes.InvalidArgument);
    }

    [Fact]
    public async Task 正常请求不受影响_仍返回统一成功体()
    {
        // 反向护栏：接管工厂不能把正常路径也改坏
        var res = await _api.SendAsync(HttpMethod.Get, "/api/site/config");
        var raw = await res.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(Codes.Ok, doc.RootElement.GetProperty("code").GetInt32());
    }
}
