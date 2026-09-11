using Blog.Domain.Entities;
using Blog.Domain.Entities.Base;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System;
using System.Collections.Generic;
using System.Text;

namespace Blog.Infrastructure.Persistence
{
    public class BlogDbContext : DbContext
    {
        public BlogDbContext(DbContextOptions<BlogDbContext> options) : base(options) { }

        public DbSet<Post> Posts { get; set; }
        public DbSet<Author> Authors { get; set; }
        public DbSet<Tag> Tags { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<SocialLink> SocialLinks { get; set; }
        public DbSet<SiteConfig> SiteConfigs { get; set; }

        /// <summary>登录账号（与 Author 分离，见 docs/10-决策记录.md §2.1 / T1）</summary>
        public DbSet<User> Users { get; set; }

        /// <summary>专栏</summary>
        public DbSet<Collection> Collections { get; set; }

        /// <summary>文章↔专栏 连接表（带专栏内排序）</summary>
        public DbSet<PostCollection> PostCollections { get; set; }

        // 种子数据固定主键（HasData 要求）
        private static readonly Guid DefaultAuthorId = new("6f2a1b3c-0000-0000-0000-000000000001");
        private static readonly Guid SeedSiteNameId = new("6f2a1b3c-0000-0000-0000-000000000010");
        private static readonly Guid SeedSubtitlesId = new("6f2a1b3c-0000-0000-0000-000000000011");
        private static readonly Guid SeedFoundingId = new("6f2a1b3c-0000-0000-0000-000000000012");
        private static readonly Guid SeedGithubLinkId = new("6f2a1b3c-0000-0000-0000-000000000020");
        private static readonly Guid SeedBilibiliLinkId = new("6f2a1b3c-0000-0000-0000-000000000021");
        private static readonly Guid SeedAdminUserId = new("6f2a1b3c-0000-0000-0000-000000000030");
        private static readonly Guid SeedCollectionId = new("6f2a1b3c-0000-0000-0000-000000000040");

        /// <summary>
        /// 初始管理员密码（开发用）：Admin@12345
        ///
        /// 为什么把哈希写进种子：让全新环境开箱即可登录后台，不必手工插数据。
        /// 哈希是 PBKDF2-SHA512（210000 次迭代 + 每用户随机盐），无法反推密码，
        /// 但**这个密码是公开在仓库里的**，因此：
        ///   - 生产部署后必须立刻登录并修改密码（或在部署流程中用脚本重置）
        ///   - 生产环境不应依赖此种子账号，建议由部署脚本创建专属管理员
        ///
        /// 复现方式（如需换成别的初始密码）：
        ///   var hash = new Blog.Infrastructure.Security.Pbkdf2PasswordHasher().Hash("你的密码");
        /// 然后替换下面的常量即可。也可在后台用「重置密码」接口改，无需改代码。
        /// </summary>
        private const string SeedAdminPasswordHash =
            "pbkdf2-sha512$210000$Rc6nVI3/2LQ+vmRlYDCCyQ==$RbxCnKNUm29IVZ72izuS7pFoH/W8t5KkIvGZiOu/YLQ=";

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // PostCollection 是纯连接实体，而 Collection 带全局软删除过滤器（QueryFilter）。
            // EF 会提示「必需端被过滤可能导致意外结果」；我们从不单独查询 PostCollection
            // （始终经 Post/Collection 导航访问），因此该场景不会发生，显式抑制以免噪音。
            optionsBuilder.ConfigureWarnings(w =>
                w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Post>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Content).IsRequired();
                entity.Property(e => e.Summary).HasMaxLength(120);
                entity.Property(e => e.CoverImage).HasMaxLength(500);
                entity.HasIndex(e => e.PublishedAt);                 // 列表页按发布时间排序
                entity.HasIndex(e => e.CategoryId);                 // 按分类过滤
                entity.HasIndex(e => new { e.IsDeleted, e.PublishedAt });

                entity.HasOne(e => e.Author)
                      .WithMany(a => a.Posts)
                      .HasForeignKey(e => e.AuthorId)
                      .OnDelete(DeleteBehavior.SetNull);

                // 创建者账号：账号删除时置空（不删文章），用于归属校验与审计
                entity.HasOne<User>()
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByUserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.Category)
                      .WithMany(c => c.Posts)
                      .HasForeignKey(e => e.CategoryId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasMany(e => e.Tags)
                      .WithMany(t => t.Posts);

                // 中文全文检索列（tsvector 生成列）。表达式在迁移中手写 SQL（需要 zhparser 的
                // chinese 检索配置），这里只声明**影子属性**并告诉 EF「由数据库生成」。
                //
                // 为什么用影子属性而不是 Post 上的实体属性：Blog.Domain 必须保持零框架依赖
                // （见 docs/03-后端设计.md §1.1），而 NpgsqlTsVector 来自 Npgsql，
                // 因此该列只在 Infrastructure 层可见，查询时用 EF.Property 访问。
                entity.Property<NpgsqlTypes.NpgsqlTsVector>("SearchVector")
                      .HasColumnName("SearchVector")
                      .HasColumnType("tsvector")
                      .HasComputedColumnSql(
                          "setweight(to_tsvector('chinese', coalesce(\"Title\", '')), 'A') || " +
                          "setweight(to_tsvector('chinese', coalesce(\"Summary\", '')), 'B') || " +
                          "setweight(to_tsvector('chinese', coalesce(\"Content\", '')), 'C')",
                          stored: true);

                entity.HasIndex(e => e.CreatedByUserId);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
                entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(500);
                // 角色存字符串，可读性优于魔法数字，且加角色时不用改数据库类型
                entity.Property(e => e.Role).IsRequired().HasConversion<string>().HasMaxLength(20);

                // 过滤唯一索引：软删除后允许复用同一邮箱
                entity.HasIndex(e => e.Email).IsUnique().HasFilter("\"IsDeleted\" = false");

                // 关联署名对象：作者被删时置空，账号仍可登录（只是失去署名身份）
                entity.HasOne(e => e.Author)
                      .WithMany()
                      .HasForeignKey(e => e.AuthorId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<Collection>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Slug).IsRequired().HasMaxLength(200);
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.Property(e => e.CoverImage).HasMaxLength(500);
                entity.HasIndex(e => e.Slug).IsUnique().HasFilter("\"IsDeleted\" = false");
                entity.HasIndex(e => e.SortOrder);
                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // 文章 ↔ 专栏：显式连接实体，因为需要携带「专栏内排序」
            modelBuilder.Entity<PostCollection>(entity =>
            {
                entity.HasKey(e => new { e.PostId, e.CollectionId });

                entity.HasOne(e => e.Post)
                      .WithMany(p => p.CollectionLinks)
                      .HasForeignKey(e => e.PostId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Collection)
                      .WithMany(c => c.PostLinks)
                      .HasForeignKey(e => e.CollectionId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.CollectionId);
            });

            modelBuilder.Entity<Author>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Avatar).HasMaxLength(200);
                entity.Property(e => e.Bio).HasMaxLength(500);
                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<Tag>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
                // 过滤唯一索引：软删除后允许重建同名标签
                entity.HasIndex(e => e.Name).IsUnique().HasFilter("\"IsDeleted\" = false");
                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<Category>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
                entity.HasIndex(e => e.Name).IsUnique().HasFilter("\"IsDeleted\" = false");
                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<SocialLink>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Icon).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Url).IsRequired().HasMaxLength(500);
                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            modelBuilder.Entity<SiteConfig>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Key).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Value).IsRequired();
                entity.Property(e => e.Description).HasMaxLength(500);
                entity.HasIndex(e => e.Key).IsUnique().HasFilter("\"IsDeleted\" = false");
                entity.HasQueryFilter(e => !e.IsDeleted);
            });

            // 乐观锁：Version 为整数并发令牌（跨数据库）。
            // 更新 SQL 由仓储层手动控制（见 BaseRepository.ApplyOptimisticVersion）：
            // UPDATE ... SET "Version" = @expected + 1 WHERE "Id" = @id AND "Version" = @expected
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var versionProperty = entityType.FindProperty(nameof(BaseEntity.Version));
                if (versionProperty is not null)
                    versionProperty.IsConcurrencyToken = true;
            }

            SeedData(modelBuilder);
        }

        private static void SeedData(ModelBuilder modelBuilder)
        {
            var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

            modelBuilder.Entity<Author>().HasData(new
            {
                Id = DefaultAuthorId,
                Name = "kky",
                Email = "kky@example.com",
                Avatar = "/media/avatar-default.png",
                Bio = "coding slayer",
                CreatedAt = now,
                IsDeleted = false,
                DeletedAt = (DateTimeOffset?)null,
                Version = 1
            });

            modelBuilder.Entity<SiteConfig>().HasData(
                new { Id = SeedSiteNameId, Key = SiteConfigKeys.SiteName, Value = "kky's blog", Description = "站点名称", CreatedAt = now, IsDeleted = false, DeletedAt = (DateTimeOffset?)null, Version = 1 },
                new { Id = SeedSubtitlesId, Key = SiteConfigKeys.HeroSubtitles, Value = "[\"Hello, World!\",\"Welcome to my blog.\",\"Stay hungry, stay foolish.\"]", Description = "首屏打字机文案", CreatedAt = now, IsDeleted = false, DeletedAt = (DateTimeOffset?)null, Version = 1 },
                new { Id = SeedFoundingId, Key = SiteConfigKeys.FoundingDate, Value = "2026-01-01", Description = "建站日期", CreatedAt = now, IsDeleted = false, DeletedAt = (DateTimeOffset?)null, Version = 1 });

            // 初始管理员账号。作者账号**不预置** —— 按 T1 由管理员登录后在后台创建。
            modelBuilder.Entity<User>().HasData(new
            {
                Id = SeedAdminUserId,
                Email = "admin@example.com",
                PasswordHash = SeedAdminPasswordHash,
                Role = UserRole.Admin,
                IsActive = true,
                AuthorId = (Guid?)null,
                LastLoginAt = (DateTimeOffset?)null,
                TokenVersion = 1,
                CreatedAt = now,
                IsDeleted = false,
                DeletedAt = (DateTimeOffset?)null,
                Version = 1
            });

            modelBuilder.Entity<Collection>().HasData(new
            {
                Id = SeedCollectionId,
                Title = "示例专栏",
                Slug = "sample-collection",
                Description = "把多篇文章组织成一个系列（专栏）。可在后台「专栏管理」中修改或删除。",
                CoverImage = string.Empty,
                SortOrder = 0,
                IsPublished = true,
                CreatedAt = now,
                IsDeleted = false,
                DeletedAt = (DateTimeOffset?)null,
                Version = 1
            });

            modelBuilder.Entity<SocialLink>().HasData(
                new { Id = SeedGithubLinkId, Name = "GitHub", Icon = "github", Url = "https://github.com", SortOrder = 0, IsVisible = true, CreatedAt = now, IsDeleted = false, DeletedAt = (DateTimeOffset?)null, Version = 1 },
                new { Id = SeedBilibiliLinkId, Name = "Bilibili", Icon = "bilibili", Url = "https://www.bilibili.com", SortOrder = 1, IsVisible = true, CreatedAt = now, IsDeleted = false, DeletedAt = (DateTimeOffset?)null, Version = 1 });
        }
    }
}
