using Blog.Domain.Entities;
using Blog.Domain.Entities.Base;
using Microsoft.EntityFrameworkCore;
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

        // 种子数据固定主键（HasData 要求）
        private static readonly Guid DefaultAuthorId = new("6f2a1b3c-0000-0000-0000-000000000001");
        private static readonly Guid SeedSiteNameId = new("6f2a1b3c-0000-0000-0000-000000000010");
        private static readonly Guid SeedSubtitlesId = new("6f2a1b3c-0000-0000-0000-000000000011");
        private static readonly Guid SeedFoundingId = new("6f2a1b3c-0000-0000-0000-000000000012");
        private static readonly Guid SeedGithubLinkId = new("6f2a1b3c-0000-0000-0000-000000000020");
        private static readonly Guid SeedBilibiliLinkId = new("6f2a1b3c-0000-0000-0000-000000000021");

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

                entity.HasOne(e => e.Category)
                      .WithMany(c => c.Posts)
                      .HasForeignKey(e => e.CategoryId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasMany(e => e.Tags)
                      .WithMany(t => t.Posts);

                entity.HasQueryFilter(e => !e.IsDeleted);
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

            modelBuilder.Entity<SocialLink>().HasData(
                new { Id = SeedGithubLinkId, Name = "GitHub", Icon = "github", Url = "https://github.com", SortOrder = 0, IsVisible = true, CreatedAt = now, IsDeleted = false, DeletedAt = (DateTimeOffset?)null, Version = 1 },
                new { Id = SeedBilibiliLinkId, Name = "Bilibili", Icon = "bilibili", Url = "https://www.bilibili.com", SortOrder = 1, IsVisible = true, CreatedAt = now, IsDeleted = false, DeletedAt = (DateTimeOffset?)null, Version = 1 });
        }
    }
}
