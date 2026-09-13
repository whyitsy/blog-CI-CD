using Blog.Application.Common;
using Blog.Application.Common.Exceptions;

namespace Blog.Tests.Unit;

/// <summary>
/// MediaPath 白名单校验的单元测试。
///
/// <para><b>为什么这组测试必须有</b></para>
/// 安全校验最容易出的问题不是「拦不住」，而是**看起来在拦、其实没生效**——
/// 比如校验只写在前端隐藏输入框里，或者被别处的兜底逻辑悄悄绕过。
/// 所以要两类样本都覆盖：
/// <list type="bullet">
///   <item>攻击样本：全部必须被拒（负向断言）</item>
///   <item>合法样本：必须放行（正向断言）——只测拒绝的话，
///         「把所有输入一律拒掉」这种退化实现也能全绿</item>
/// </list>
///
/// 端点层面的行为（curl 直打 API 也被拒）由 Integration/MediaUrlValidationTests 覆盖，
/// 两者互补：这里保证判定逻辑对，那里保证判定真的被调用。
/// </summary>
public sealed class MediaPathTests
{
    // ------------------------------------------------------------------ 正向

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 未设置_视为合法并归一化为空串(string? value)
    {
        Assert.True(MediaPath.IsOwned(value, out var error), $"应放行，实际被拒：{error}");
        Assert.Equal(string.Empty, MediaPath.Normalize(value));
    }

    [Theory]
    [InlineData("/api/files/2026/09/0123456789abcdef0123456789abcdef.webp")]
    [InlineData("/api/files/avatar-default.webp")]
    [InlineData("/api/files/2026/09/a.png")]
    [InlineData("/api/files/2026/09/A-B_c.9.webp")]
    public void 本服务签发的地址_放行(string value)
    {
        Assert.True(MediaPath.IsOwned(value, out var error), $"应放行，实际被拒：{error}");
    }

    [Fact]
    public void 首尾空白_先裁剪再判定()
    {
        Assert.True(MediaPath.IsOwned("  /api/files/avatar-default.webp  ", out var error), error);
        Assert.Equal("/api/files/avatar-default.webp", MediaPath.Normalize("  /api/files/avatar-default.webp  "));
    }

    [Fact]
    public void 长度上限边界_恰好等于上限放行()
    {
        var value = MediaPath.OwnedPrefix + new string('a', MediaPath.MaxLength - MediaPath.OwnedPrefix.Length);
        Assert.Equal(MediaPath.MaxLength, value.Length);

        Assert.True(MediaPath.IsOwned(value, out var error), error);
    }

    // ------------------------------------------------------------------ 负向：外部地址

    [Theory]
    [InlineData("https://evil.com/x.png")]
    [InlineData("http://evil.com/x.png")]
    [InlineData("//evil.com/x.png")]                 // 协议相对 URL
    [InlineData("HTTPS://EVIL.COM/x.png")]           // 大小写不敏感的前缀伪装
    [InlineData("ftp://evil.com/x.png")]
    public void 外部地址_一律拒绝(string value)
    {
        Assert.False(MediaPath.IsOwned(value, out var error));
        Assert.Equal(MediaPath.ExternalUrlMessage, error);
    }

    // ------------------------------------------------------------------ 负向：危险 scheme

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("blob:https://evil.com/uuid")]
    public void 危险scheme_一律拒绝(string value)
    {
        Assert.False(MediaPath.IsOwned(value, out _));
    }

    // ------------------------------------------------------------------ 负向：路径穿越与归一化绕过

    [Theory]
    [InlineData("/api/files/../../etc/passwd")]
    [InlineData("/api/files/2026/09/../../../etc/passwd")]
    [InlineData("/api/files/..%2f..%2fetc/passwd")]
    [InlineData("/api/files/%2e%2e/%2e%2e/etc/passwd")]   // 百分号编码的 ..
    [InlineData("/api/files//evil.com/x.png")]             // 双斜杠 -> 某些客户端会当成域名
    [InlineData("/api/files/2026//09/x.png")]
    [InlineData("/api/files/2026/09/x.png?a=1")]           // 查询串
    [InlineData("/api/files/2026/09/x.png#frag")]          // 片段
    [InlineData("/api/files/2026\\09\\x.png")]             // Windows 分隔符
    [InlineData("/api/files/2026/09/x.png\nSet-Cookie: a=b")]
    public void 路径穿越与编码绕过_一律拒绝(string value)
    {
        Assert.False(MediaPath.IsOwned(value, out _), $"应被拒：{value}");
    }

    // ------------------------------------------------------------------ 负向：前缀伪装与形状错误

    [Theory]
    [InlineData("/apifiles/2026/09/x.png")]     // 少一个斜杠
    [InlineData("api/files/2026/09/x.png")]     // 缺前导斜杠
    [InlineData("/api/file/2026/09/x.png")]     // 少一个 s
    [InlineData("/api/files")]                  // 只有前缀，没有结尾斜杠
    [InlineData("/api/files/")]                 // 没有文件名
    [InlineData("/api/files/2026/09/")]         // 只有目录
    [InlineData("C:\\Windows\\x.png")]          // 绝对路径
    [InlineData("/etc/passwd")]
    [InlineData("x.png")]
    public void 前缀伪装与形状错误_一律拒绝(string value)
    {
        Assert.False(MediaPath.IsOwned(value, out _), $"应被拒：{value}");
    }

    [Fact]
    public void 超长地址_拒绝()
    {
        var value = MediaPath.OwnedPrefix + new string('a', MediaPath.MaxLength);
        Assert.False(MediaPath.IsOwned(value, out var error));
        Assert.Contains("长度", error);
    }

    // ------------------------------------------------------------------ Validate：异常形态

    [Fact]
    public void Validate_非法时抛4001并带上字段名()
    {
        var ex = Assert.Throws<BusinessException>(
            () => MediaPath.Validate("https://evil.com/x.png", "封面"));

        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
        Assert.StartsWith("封面", ex.Message);
    }

    [Fact]
    public void Validate_合法时返回归一化结果()
    {
        Assert.Equal("/api/files/a.png", MediaPath.Validate("  /api/files/a.png ", "头像"));
        Assert.Equal(string.Empty, MediaPath.Validate(null, "头像"));
        Assert.Equal(string.Empty, MediaPath.Validate("   ", "头像"));
    }

    // ------------------------------------------------------------------ ParseList：读取侧（宽容）

    [Fact]
    public void ParseList_空值得到空列表()
    {
        Assert.Empty(MediaPath.ParseList(null));
        Assert.Empty(MediaPath.ParseList(""));
        Assert.Empty(MediaPath.ParseList("   "));
    }

    [Fact]
    public void ParseList_历史遗留的裸地址_当成单元素列表()
    {
        // 本次改造前 HeroBackground 存的就是一个裸地址字符串，读取侧必须还能认
        var result = MediaPath.ParseList("/api/files/2026/09/old.webp");
        Assert.Equal("/api/files/2026/09/old.webp", Assert.Single(result));
    }

    [Fact]
    public void ParseList_JSON数组_逐项读出()
    {
        var result = MediaPath.ParseList("""["/api/files/a.png","/api/files/b.webp"]""");
        Assert.Equal(new[] { "/api/files/a.png", "/api/files/b.webp" }, result.ToArray());
    }

    [Fact]
    public void ParseList_数组里的空白项被丢掉()
    {
        var result = MediaPath.ParseList("""["/api/files/a.png","","   "]""");
        Assert.Equal("/api/files/a.png", Assert.Single(result));
    }

    [Fact]
    public void ParseList_格式损坏_返回空而不是抛异常()
    {
        // 读取不能因为一条脏数据就把整个站点配置接口打挂
        Assert.Empty(MediaPath.ParseList("[这不是 JSON"));
    }

    // ------------------------------------------------------------------ ValidateJsonList：写入侧（严格）

    [Fact]
    public void ValidateJsonList_空值写成空数组()
    {
        Assert.Equal("[]", MediaPath.ValidateJsonList(null, "首屏背景图"));
        Assert.Equal("[]", MediaPath.ValidateJsonList("", "首屏背景图"));
    }

    [Fact]
    public void ValidateJsonList_裸地址升级为JSON数组()
    {
        Assert.Equal("""["/api/files/a.png"]""", MediaPath.ValidateJsonList("/api/files/a.png", "首屏背景图"));
    }

    [Fact]
    public void ValidateJsonList_合法数组_规范化后原样返回()
    {
        var result = MediaPath.ValidateJsonList("""["/api/files/a.png","/api/files/b.webp"]""", "首屏背景图");
        Assert.Equal("""["/api/files/a.png","/api/files/b.webp"]""", result);
    }

    [Theory]
    [InlineData("""["https://evil.com/x.png"]""")]
    [InlineData("""["/api/files/a.png","javascript:alert(1)"]""")]
    [InlineData("""["/api/files/../../etc/passwd"]""")]
    [InlineData("https://evil.com/x.png")]
    public void ValidateJsonList_任一项非法_整批拒绝(string json)
    {
        var ex = Assert.Throws<BusinessException>(() => MediaPath.ValidateJsonList(json, "首屏背景图"));
        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
    }

    [Fact]
    public void ValidateJsonList_JSON格式损坏_报错而不是静默存空()
    {
        // 关键差异：写入侧不能像读取侧那样吞掉错误，否则配置会被悄悄清空
        var ex = Assert.Throws<BusinessException>(() => MediaPath.ValidateJsonList("[这不是 JSON", "首屏背景图"));
        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
        Assert.Contains("JSON", ex.Message);
    }

    [Fact]
    public void ValidateJsonList_超过条数上限_拒绝()
    {
        var items = Enumerable.Range(0, MediaPath.MaxItems + 1)
            .Select(i => $"/api/files/2026/09/{i}.png")
            .ToArray();
        var json = System.Text.Json.JsonSerializer.Serialize(items);

        var ex = Assert.Throws<BusinessException>(() => MediaPath.ValidateJsonList(json, "首屏背景图"));
        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
    }

    [Fact]
    public void ValidateJsonList_恰好等于条数上限_放行()
    {
        var items = Enumerable.Range(0, MediaPath.MaxItems)
            .Select(i => $"/api/files/2026/09/{i}.png")
            .ToArray();
        var json = System.Text.Json.JsonSerializer.Serialize(items);

        var result = MediaPath.ValidateJsonList(json, "首屏背景图");
        Assert.Equal(MediaPath.MaxItems, MediaPath.ParseList(result).Count);
    }
}
