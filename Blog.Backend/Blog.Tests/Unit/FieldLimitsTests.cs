using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Domain.Entities;
using Blog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Blog.Tests.Unit;

/// <summary>
/// <see cref="FieldLimits"/> 的单元测试，外加一条**结构性护栏**。
///
/// <para><b>为什么需要"护栏"而不只是"校验测试"</b></para>
/// 这次的真实事故（docs/14 第 5 条）不是"校验写错了"，而是
/// **数据库有约束、Application 层却根本没写校验**。这类漏项写多少个
/// "超长被拒绝"的用例都发现不了 —— 因为没人会想到给一个不存在的校验写测试。
///
/// 所以这里用 EF 的**模型元数据**反查每个字段真实的 <c>HasMaxLength</c>，
/// 再和 <see cref="FieldLimits"/> 的常量逐个比对：
/// 以后有人改了 <c>BlogDbContext</c> 却忘了同步常量，这条测试会直接变红。
/// </summary>
public sealed class FieldLimitsTests
{
    // ---------------------------------------------------------------- 结构性护栏

    /// <summary>
    /// 每个「用户可输入的字符串字段」都必须满足：<see cref="FieldLimits"/> 的常量 == EF 模型的 HasMaxLength。
    ///
    /// <para>左侧是 <c>(实体类型, 属性名)</c>，右侧是 <see cref="FieldLimits"/> 里对应的常量。</para>
    /// </summary>
    public static TheoryData<Type, string, int> DeclaredLimits() => new()
    {
        { typeof(Post), nameof(Post.Title), FieldLimits.PostTitle },
        { typeof(Post), nameof(Post.Summary), FieldLimits.PostSummary },
        { typeof(Post), nameof(Post.CoverImage), FieldLimits.PostCoverImage },

        { typeof(Collection), nameof(Collection.Title), FieldLimits.CollectionTitle },
        { typeof(Collection), nameof(Collection.Slug), FieldLimits.CollectionSlug },
        { typeof(Collection), nameof(Collection.Description), FieldLimits.CollectionDescription },
        { typeof(Collection), nameof(Collection.CoverImage), FieldLimits.CollectionCoverImage },

        { typeof(Category), nameof(Category.Name), FieldLimits.CategoryName },
        { typeof(Tag), nameof(Tag.Name), FieldLimits.TagName },

        { typeof(Author), nameof(Author.Name), FieldLimits.AuthorName },
        { typeof(Author), nameof(Author.Email), FieldLimits.AuthorEmail },
        { typeof(Author), nameof(Author.Avatar), FieldLimits.AuthorAvatar },
        { typeof(Author), nameof(Author.Bio), FieldLimits.AuthorBio },

        { typeof(User), nameof(User.Email), FieldLimits.UserEmail },

        { typeof(SocialLink), nameof(SocialLink.Name), FieldLimits.SocialLinkName },
        { typeof(SocialLink), nameof(SocialLink.Icon), FieldLimits.SocialLinkIcon },
        { typeof(SocialLink), nameof(SocialLink.Url), FieldLimits.SocialLinkUrl },

        { typeof(SiteConfig), nameof(SiteConfig.Key), FieldLimits.SiteConfigKey },
        { typeof(SiteConfig), nameof(SiteConfig.Description), FieldLimits.SiteConfigDescription },
    };

    [Theory]
    [MemberData(nameof(DeclaredLimits))]
    public void 常量必须与EF模型的最大长度一致(Type entityType, string propertyName, int declaredLimit)
    {
        var actual = MaxLengthOf(entityType, propertyName);

        Assert.True(actual.HasValue,
            $"{entityType.Name}.{propertyName} 在 EF 模型里没有 HasMaxLength —— " +
            "要么补上，要么它不该出现在这份清单里");

        Assert.True(declaredLimit == actual.Value,
            $"{entityType.Name}.{propertyName}：FieldLimits 写的是 {declaredLimit}，" +
            $"而 EF 模型是 {actual.Value}。改了 BlogDbContext 就要同步改 FieldLimits（见该类的注释）");
    }

    /// <summary>
    /// 媒体地址的通用上限不能宽于**最小的那个目标列**，否则会出现
    /// 「过了白名单却写不进去」的怪事（见 MediaPath.MaxLength 的注释）。
    /// </summary>
    [Fact]
    public void 媒体地址上限不得超过最小的目标列()
    {
        var avatar = MaxLengthOf(typeof(Author), nameof(Author.Avatar))!.Value;
        var postCover = MaxLengthOf(typeof(Post), nameof(Post.CoverImage))!.Value;

        Assert.True(MediaPath.MaxLength <= avatar,
            $"MediaPath.MaxLength={MediaPath.MaxLength} 超过了 Authors.Avatar 的 {avatar}");
        Assert.True(MediaPath.MaxLength <= postCover,
            $"MediaPath.MaxLength={MediaPath.MaxLength} 超过了 Posts.CoverImage 的 {postCover}");
    }

    /// <summary>
    /// 建模型**不需要连库** —— <c>OnModelCreating</c> 只做元数据配置。
    /// 用一条指向不存在库的连接串即可，纯内存操作。
    /// </summary>
    private static int? MaxLengthOf(Type entityType, string propertyName)
    {
        var options = new DbContextOptionsBuilder<BlogDbContext>()
            .UseNpgsql("Host=model-only;Database=model-only;Username=x;Password=x")
            .Options;

        using var db = new BlogDbContext(options);
        return db.Model.FindEntityType(entityType)?.FindProperty(propertyName)?.GetMaxLength();
    }

    // ---------------------------------------------------------------- 判定行为

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 未填放行(string? value)
    {
        FieldLimits.EnsureLength(value, 10, "字段");
    }

    [Fact]
    public void 恰好等于上限放行()
    {
        FieldLimits.EnsureLength(new string('中', 200), FieldLimits.PostSummary, "摘要");
    }

    [Fact]
    public void 超过上限抛4001并带上限数字()
    {
        var ex = Assert.Throws<BusinessException>(
            () => FieldLimits.EnsureLength(new string('中', 201), FieldLimits.PostSummary, "摘要"));

        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
        Assert.Contains("摘要", ex.Message);
        Assert.Contains("200", ex.Message);
    }

    [Fact]
    public void 按Trim之后的长度判定()
    {
        // 200 个字 + 尾部空格：入库前会 Trim，所以应当放行
        var value = new string('中', 200) + "   ";
        FieldLimits.EnsureLength(value, FieldLimits.PostSummary, "摘要");
    }
}
