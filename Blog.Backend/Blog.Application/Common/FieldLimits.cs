using Blog.Application.Common.Exceptions;

namespace Blog.Application.Common
{
    /// <summary>
    /// 用户可输入的「字符串字段长度上限」的**唯一来源**。
    ///
    /// <para><b>为什么需要这个类</b></para>
    /// 长度限制天然存在于两层：数据库的 <c>HasMaxLength</c>（最终防线）与
    /// Application 层的显式校验（业务规则）。两者一旦不一致，用户就会撞上
    /// 「本以为能存、结果 500」。本项目的真实事故（见 docs/14 第 5 条）：
    /// <c>Posts.Summary</c> 是 <c>varchar(120)</c>，而 Application 层**根本没有校验摘要长度**，
    /// 前端也没有 <c>maxlength</c>，于是超出时直接抛 <c>DbUpdateException</c>，
    /// 用户看到的是「服务器内部错误」。
    ///
    /// <para><b>用法约定</b></para>
    /// 改动 <c>BlogDbContext</c> 里任何 <c>HasMaxLength</c> 时，**必须同步改这里**，
    /// 并补一条边界测试（<c>恰好等于上限放行 / 超一个拒绝</c>）。
    /// 数字集中之后，「DB 与应用层是否一致」变成一件可以逐行核对的事。
    ///
    /// <para><b>为什么数据库约束不能省</b></para>
    /// 应用层校验可能被绕过（新增了别的写入路径、直接改库、未来的批量导入），
    /// 数据库约束是最后一道防线。三层各司其职：
    /// 前端管「别让他填错」、Application 管「填错了要说清楚」、数据库管「绝不能写坏数据」。
    /// </summary>
    public static class FieldLimits
    {
        // ---------------- Post ----------------
        /// <summary>Posts.Title（varchar 200）</summary>
        public const int PostTitle = 200;

        /// <summary>Posts.Summary（varchar 200）。注意自动摘要只取正文前 50 字，与此无关</summary>
        public const int PostSummary = 200;

        /// <summary>Posts.CoverImage（varchar 500）。同时受 MediaPath.MaxLength 约束</summary>
        public const int PostCoverImage = 500;

        // ---------------- Collection ----------------
        /// <summary>Collections.Title（varchar 200）</summary>
        public const int CollectionTitle = 200;

        /// <summary>Collections.Slug（varchar 200）</summary>
        public const int CollectionSlug = 200;

        /// <summary>Collections.Description（varchar 500）</summary>
        public const int CollectionDescription = 500;

        /// <summary>Collections.CoverImage（varchar 500）。同时受 MediaPath.MaxLength 约束</summary>
        public const int CollectionCoverImage = 500;

        // ---------------- Category / Tag ----------------
        /// <summary>Categories.Name（varchar 100）</summary>
        public const int CategoryName = 100;

        /// <summary>Tags.Name（varchar 50）</summary>
        public const int TagName = 50;

        // ---------------- Author ----------------
        /// <summary>Authors.Name（varchar 100）</summary>
        public const int AuthorName = 100;

        /// <summary>Authors.Email（varchar 100）</summary>
        public const int AuthorEmail = 100;

        /// <summary>Authors.Avatar（varchar 200）。**这是 <see cref="MediaPath.MaxLength"/> 的上界来源**</summary>
        public const int AuthorAvatar = 200;

        /// <summary>Authors.Bio（varchar 500）</summary>
        public const int AuthorBio = 500;

        // ---------------- User ----------------
        /// <summary>Users.Email（varchar 100）</summary>
        public const int UserEmail = 100;

        // ---------------- SocialLink ----------------
        /// <summary>SocialLinks.Name（varchar 50）</summary>
        public const int SocialLinkName = 50;

        /// <summary>SocialLinks.Icon（varchar 50）</summary>
        public const int SocialLinkIcon = 50;

        /// <summary>SocialLinks.Url（varchar 500）</summary>
        public const int SocialLinkUrl = 500;

        // ---------------- SiteConfig ----------------
        /// <summary>SiteConfigs.Key（varchar 100）</summary>
        public const int SiteConfigKey = 100;

        /// <summary>SiteConfigs.Description（varchar 500）</summary>
        public const int SiteConfigDescription = 500;

        /// <summary>
        /// 长度校验：<c>null</c> / 空白视为「未填」直接放行，超限抛 4001。
        ///
        /// <para>按 <b>Trim 之后</b>的长度判定 —— 因为各服务入库前普遍会 Trim，
        /// 按未 Trim 的长度判会把「尾部多打了一个空格」这种输入误判为超限。</para>
        /// </summary>
        /// <param name="value">待校验的值</param>
        /// <param name="max">上限（请传本类的常量，不要写裸数字）</param>
        /// <param name="fieldName">字段中文名，用于拼出可读提示（如「摘要」「个人简介」）</param>
        public static void EnsureLength(string? value, int max, string fieldName)
        {
            if (value is null) return;

            if (value.Trim().Length > max)
                throw new BusinessException($"{fieldName}长度不能超过 {max}", ErrorCodes.InvalidArgument);
        }
    }
}
