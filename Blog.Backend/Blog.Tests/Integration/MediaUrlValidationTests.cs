using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Blog.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Blog.Tests.Integration;

/// <summary>
/// 媒体地址（作者头像 / 文章封面 / 站点 Logo / 首屏背景图）的**端点级**校验测试。
///
/// <para><b>为什么单元测试不够</b></para>
/// MediaPathTests 证明的是「判定逻辑本身对不对」。而这次缺陷的真正成因是
/// **判定根本不存在**：前端把自由文本框藏起来看着像修好了，可 <c>curl</c> 直接 PUT
/// 依然能写进任意 URL。所以这里刻意绕开前端、直接打 HTTP 接口断言服务端会拒——
/// 这是唯一能区分「真的修好了」和「只是前端看不见了」的证据
/// （同 docs/06-技术债与待办.md §1.1 记的那条教训）。
///
/// <para><b>为什么每条负向用例都配一条正向用例</b></para>
/// 如果校验被写成「一律拒绝」，纯负向断言照样全绿。正向用例（本站地址必须放行、
/// 清空必须放行）才能把这种退化实现挡住。
///
/// <para><b>为什么按字段名断言 message</b></para>
/// 4001 是「参数校验失败」的通用码，标题为空、版本号非法同样是 4001。
/// 只断言 code 会让「因为别的原因失败」也算通过，所以再断言提示里带字段名。
/// </summary>
[Collection(BlogApiCollection.Name)]
public sealed class MediaUrlValidationTests
{
    private const string EvilUrl = "https://evil.com/x.png";
    private const string GoodUrl = "/api/files/2026/09/0123456789abcdef0123456789abcdef.webp";

    private readonly BlogApiFixture _fixture;
    private readonly ApiClient _api;
    private string? _adminToken;

    public MediaUrlValidationTests(BlogApiFixture fixture)
    {
        _fixture = fixture;
        _api = new ApiClient(fixture.CreateClient());
    }

    private async Task<string> AdminAsync() =>
        _adminToken ??= await _api.LoginAsync("admin@example.com", "Admin@12345");

    // ------------------------------------------------------------------ 文件上传：SVG（G11）

    /// <summary>
    /// SVG 必须被拒绝上传。
    ///
    /// <para>为什么这是安全问题而不是"少支持一种格式"：SVG 可以内嵌 <c>&lt;script&gt;</c>，
    /// 而本站的文件读取接口是**同源**下发的。拥有 ContentWriter 的作者上传一个恶意 SVG，
    /// 再把 <c>/api/files/xxx.svg</c> 发给管理员，管理员**直接打开链接**时脚本就在
    /// 站点源上执行 —— 存储型 XSS。</para>
    ///
    /// <para>同样要配一条**正向**用例：如果白名单被改成"什么扩展名都拒"，
    /// 只断言拒绝的测试照样全绿。</para>
    /// </summary>
    [Fact]
    public async Task 上传_SVG被拒绝_位图放行()
    {
        var token = await AdminAsync();

        var svg = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>";

        var bad = await UploadAsync(token, "evil.svg", "image/svg+xml", Encoding.UTF8.GetBytes(svg));
        // 状态码必须是 4xx：以前这里是 HTTP 200、只有 body 里 code=4001
        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal(Codes.InvalidArgument, bad.Code);
        Assert.Contains("不支持的文件类型", bad.Message);
        Assert.Null(bad.Url);

        // 正向：普通位图仍然放行
        var ok = await UploadAsync(token, "probe.png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.Equal(Codes.Ok, ok.Code);
        Assert.False(string.IsNullOrWhiteSpace(ok.Url));
    }

    /// <summary>
    /// 上传接口的错误必须用 **HTTP 状态码**表达，不能返回「200 + 错误 code」。
    ///
    /// <para>改造前 <c>FilesController.Upload</c> 是 <c>return ApiResponse.Fail(...)</c>，
    /// 于是「参数不合法」这个语义在上传接口上是 200、在其它接口上是 400，
    /// 客户端必须记得"上传只看 code 不看状态码" —— 这正是要消除的不一致。</para>
    /// </summary>
    [Fact]
    public async Task 上传_空文件返回400而不是200()
    {
        var token = await AdminAsync();

        var r = await UploadAsync(token, "empty.png", "image/png", []);

        Assert.Equal(HttpStatusCode.BadRequest, r.Status);
        Assert.Equal(Codes.InvalidArgument, r.Code);
        Assert.Contains("不能为空", r.Message);
    }

    /// <summary>超过业务上限（<c>FileStorage:MaxFileSize</c>）应当返回 <b>413</b>，而不是 200 或 500</summary>
    [Fact]
    public async Task 上传_超过业务上限返回413()
    {
        var token = await AdminAsync();

        // 从配置读上限，而不是把 10MB 硬编码进测试 —— 改了配置测试仍然对
        var max = _fixture.Factory.Services
            .GetRequiredService<IConfiguration>()
            .GetValue<long>("FileStorage:MaxFileSize");
        Assert.True(max > 0, "测试前置：FileStorage:MaxFileSize 应当是正数");

        var r = await UploadAsync(token, "too-big.png", "image/png", new byte[max + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.Status);
        Assert.Equal(Codes.PayloadTooLarge, r.Code);
        Assert.Contains("文件大小超过限制", r.Message);
    }

    /// <summary>multipart 上传（<see cref="ApiClient"/> 只封装 JSON，这里单独构造请求）</summary>
    private async Task<(HttpStatusCode Status, int Code, string Message, string? Url)> UploadAsync(
        string token, string fileName, string contentType, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(part, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/files/upload") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _fixture.CreateClient().SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(raw))
            return (response.StatusCode, -1, string.Empty, null);

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;
        var code = root.TryGetProperty("code", out var c) ? c.GetInt32() : -1;
        var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? string.Empty : string.Empty;
        var url = root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object
                  && d.TryGetProperty("url", out var u) ? u.GetString() : null;

        return (response.StatusCode, code, message, url);
    }

    // ------------------------------------------------------------------ 作者头像

    [Theory]
    [InlineData("https://evil.com/x.png")]
    [InlineData("//evil.com/x.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("/api/files/../../etc/passwd")]
    [InlineData("/api/files//evil.com/x.png")]
    public async Task 创建作者_非本站头像_被拒(string avatar)
    {
        var token = await AdminAsync();

        var (status, code, _, message) = await _api.CallAsync<object>(
            HttpMethod.Post, "/api/authors", token,
            new { name = "越权试探", email = $"probe-{Guid.NewGuid():N}@example.com", bio = "", avatar });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(Codes.InvalidArgument, code);
        Assert.Contains("头像", message);
    }

    [Fact]
    public async Task 更新作者_外部头像被拒_本站头像与清空放行()
    {
        var token = await AdminAsync();

        // 用自己新建的作者做样本，不动种子作者（避免影响其它测试的前置数据）
        var author = await CreateAuthorAsync(token, avatar: "");

        var bad = await _api.CallAsync<object>(
            HttpMethod.Put, $"/api/authors/{author.Id}", token,
            new
            {
                name = author.Name,
                email = author.Email,
                bio = author.Bio,
                avatar = EvilUrl,
                version = author.Version,
            });

        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
        Assert.Equal(Codes.InvalidArgument, bad.Code);
        Assert.Contains("头像", bad.Message);

        // 正向：本站地址放行
        var ok = await _api.CallAsync<AuthorDetail>(
            HttpMethod.Put, $"/api/authors/{author.Id}", token,
            new
            {
                name = author.Name,
                email = author.Email,
                bio = author.Bio,
                avatar = GoodUrl,
                version = author.Version,
            });

        Assert.Equal(HttpStatusCode.OK, ok.Status);
        Assert.Equal(Codes.Ok, ok.Code);
        Assert.Equal(GoodUrl, ok.Data!.Avatar);

        // 正向：清空头像（「未设置」是合法取值，用于回退到首字母占位）
        var cleared = await _api.CallAsync<AuthorDetail>(
            HttpMethod.Put, $"/api/authors/{author.Id}", token,
            new
            {
                name = author.Name,
                email = author.Email,
                bio = author.Bio,
                avatar = "",
                version = ok.Data.Version,
            });

        Assert.Equal(HttpStatusCode.OK, cleared.Status);
        Assert.Equal(string.Empty, cleared.Data!.Avatar);
    }

    // ------------------------------------------------------------------ 文章封面

    [Theory]
    [InlineData("https://evil.com/x.png")]
    [InlineData("//evil.com/x.png")]
    [InlineData("javascript:alert(1)")]
    public async Task 创建文章_非本站封面_被拒(string coverImage)
    {
        var token = await AdminAsync();

        var (status, code, _, message) = await _api.CallAsync<object>(
            HttpMethod.Post, "/api/posts", token,
            new
            {
                title = $"封面越权试探-{Guid.NewGuid():N}",
                content = "正文内容，保证不是被「内容不能为空」拦下的。",
                summary = (string?)null,
                coverImage,
                categoryId = (Guid?)null,
                tagIds = Array.Empty<Guid>(),
                collectionIds = Array.Empty<Guid>(),
                authorId = (Guid?)null,
                publish = false,
            });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(Codes.InvalidArgument, code);
        Assert.Contains("封面", message);
    }

    [Fact]
    public async Task 创建文章_本站封面_放行()
    {
        var token = await AdminAsync();

        var (status, code, data, message) = await _api.CallAsync<PostDetail>(
            HttpMethod.Post, "/api/posts", token,
            new
            {
                title = $"封面放行-{Guid.NewGuid():N}",
                content = "正文内容。",
                summary = (string?)null,
                coverImage = GoodUrl,
                categoryId = (Guid?)null,
                tagIds = Array.Empty<Guid>(),
                collectionIds = Array.Empty<Guid>(),
                authorId = (Guid?)null,
                publish = false,
            });

        Assert.True(status == HttpStatusCode.OK && code == Codes.Ok,
            $"本站封面应放行，实际 status={status} code={code} message={message}");
        Assert.NotNull(data);
    }

    // ------------------------------------------------------------------ 站点配置：Logo

    [Theory]
    [InlineData("https://evil.com/logo.png")]
    [InlineData("//evil.com/logo.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/api/files/../../etc/passwd")]
    public async Task 站点配置_Logo非法地址_被拒(string logo)
    {
        var token = await AdminAsync();
        var version = await VersionOfAsync("SiteLogo");

        var (status, code, _, message) = await _api.CallAsync<object>(
            HttpMethod.Put, "/api/site/config", token,
            new { key = "SiteLogo", value = logo, version });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(Codes.InvalidArgument, code);
        Assert.Contains("Logo", message);
    }

    [Fact]
    public async Task 站点配置_Logo本站地址_放行()
    {
        var token = await AdminAsync();
        var version = await VersionOfAsync("SiteLogo");

        var (status, code, data, message) = await _api.CallAsync<SiteConfigSnapshot>(
            HttpMethod.Put, "/api/site/config", token,
            new { key = "SiteLogo", value = GoodUrl, version });

        Assert.True(status == HttpStatusCode.OK && code == Codes.Ok,
            $"本站 Logo 应放行，实际 status={status} code={code} message={message}");
        Assert.Equal(GoodUrl, data!.SiteLogo);
    }

    // ------------------------------------------------------------------ 站点配置：首屏背景图

    [Theory]
    [InlineData("""["/api/files/a.png","https://evil.com/x.png"]""")]   // 混进一个外部地址
    [InlineData("""["javascript:alert(1)"]""")]
    [InlineData("""["/api/files/../../etc/passwd"]""")]
    [InlineData("https://evil.com/x.png")]                              // 裸地址形式
    public async Task 站点配置_首屏背景图含非法项_整批被拒(string value)
    {
        var token = await AdminAsync();
        var version = await VersionOfAsync("HeroBackground");

        var (status, code, _, message) = await _api.CallAsync<object>(
            HttpMethod.Put, "/api/site/config", token,
            new { key = "HeroBackground", value, version });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(Codes.InvalidArgument, code);
        Assert.Contains("背景图", message);
    }

    [Fact]
    public async Task 站点配置_首屏背景图JSON损坏_报错而不是静默存空()
    {
        var token = await AdminAsync();
        var version = await VersionOfAsync("HeroBackground");

        var (status, code, _, message) = await _api.CallAsync<object>(
            HttpMethod.Put, "/api/site/config", token,
            new { key = "HeroBackground", value = "[这不是 JSON", version });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(Codes.InvalidArgument, code);
        Assert.Contains("JSON", message);
    }

    [Fact]
    public async Task 站点配置_首屏背景图合法数组_放行且按数组读回()
    {
        var token = await AdminAsync();
        var version = await VersionOfAsync("HeroBackground");
        var two = new[] { GoodUrl, "/api/files/avatar-default.webp" };

        var (status, code, data, message) = await _api.CallAsync<SiteConfigSnapshot>(
            HttpMethod.Put, "/api/site/config", token,
            new { key = "HeroBackground", value = System.Text.Json.JsonSerializer.Serialize(two), version });

        Assert.True(status == HttpStatusCode.OK && code == Codes.Ok,
            $"合法背景图数组应放行，实际 status={status} code={code} message={message}");

        Assert.Equal(two, data!.HeroBackgrounds.ToArray());
    }

    // ------------------------------------------------------------------ 读取侧

    [Fact]
    public async Task 站点配置_读取侧_LogoName有缺省值_背景图始终是数组()
    {
        var config = await GetConfigAsync();

        // LogoName 缺省 "k"，与改造前导航栏写死的那个字符一致
        Assert.False(string.IsNullOrWhiteSpace(config.LogoName));
        // 读取侧必须永远是数组，前端才能无脑遍历；历史遗留的裸地址由 ParseList 兜住
        Assert.NotNull(config.HeroBackgrounds);
    }

    // ------------------------------------------------------------------ 辅助

    private async Task<SiteConfigSnapshot> GetConfigAsync()
    {
        var (status, code, data, message) = await _api.CallAsync<SiteConfigSnapshot>(
            HttpMethod.Get, "/api/site/config");

        Assert.True(status == HttpStatusCode.OK && code == Codes.Ok && data is not null,
            $"读取站点配置失败 status={status} code={code} message={message}");

        return data!;
    }

    /// <summary>
    /// 取某个配置项当前的乐观锁版本号；不存在则返回 0（表示新增）。
    /// **必须**这样做：否则一旦别的测试先建过这个 Key，版本号 0 会先被
    /// 「缺少合法版本号」拦下，而它同样是 4001——负向用例就会因为错误的原因变绿。
    /// </summary>
    private async Task<int> VersionOfAsync(string key)
    {
        var config = await GetConfigAsync();
        return config.Versions.TryGetValue(key, out var version) ? version : 0;
    }

    private async Task<AuthorDetail> CreateAuthorAsync(string token, string avatar, string name = "媒体校验测试作者")
    {
        var (status, code, data, message) = await _api.CallAsync<AuthorDetail>(
            HttpMethod.Post, "/api/authors", token,
            new { name, email = $"media-{Guid.NewGuid():N}@example.com", bio = "", avatar });

        Assert.True(status == HttpStatusCode.OK && code == Codes.Ok && data is not null,
            $"创建测试作者失败 status={status} code={code} message={message}");

        return data!;
    }
}

/// <summary>作者信息（只声明本测试用得到的字段）</summary>
public sealed record AuthorDetail(
    Guid Id,
    string Name,
    string Email,
    string Avatar,
    string Bio,
    DateTimeOffset CreatedAt,
    int Version);

/// <summary>
/// 站点配置聚合（只声明本测试用得到的字段）。
/// Versions 的 Key **保留后端原始大小写**（System.Text.Json 默认不转换字典键），
/// 所以这里按 "SiteLogo" / "HeroBackground" 精确取值。
/// </summary>
public sealed record SiteConfigSnapshot(
    string SiteName,
    string LogoName,
    string? SiteLogo,
    List<string> HeroSubtitles,
    List<string> HeroBackgrounds,
    DateTimeOffset? FoundingDate,
    Dictionary<string, int> Versions);
