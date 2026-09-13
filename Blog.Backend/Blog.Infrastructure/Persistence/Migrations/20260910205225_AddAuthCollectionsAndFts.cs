using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Blog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthCollectionsAndFts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ══════════════════════════════════════════════════════════════════════
            // ⚠️ 这两段 SQL **必须排在最前面**，不能挪到下面去（G12，2026-09-13 修复）。
            //
            // 原因：本迁移下面会 `AddColumn<SearchVector>`，而它是一个**生成列**，
            // 表达式里引用了 `to_tsvector('chinese', …)` —— 也就是说它**依赖
            // `chinese` 这个检索配置已经存在**。
            //
            // 原来的顺序是「先 AddColumn<SearchVector>，再在这里建配置」，
            // 于是整条迁移链**无法在一个干净的 PostgreSQL 上从零建库**：
            //
            //     42704: text search configuration "chinese" does not exist
            //
            // 它之所以一直没暴露，是因为 PG 镜像的 entrypoint 脚本
            // （deploy/postgres-init/01-zhparser.sql）会在数据目录为空时**先**把
            // 扩展与配置建好 —— 靠的是部署巧合，而不是这条迁移自己能站得住。
            // 换标准 postgres 镜像 / 还原 schema-only dump / 手工建库后跑迁移，都会失败，
            // 且失败发生在应用启动时的 Database.Migrate()，表现为「应用起不来」。
            //
            // 两段 SQL 都是幂等的（IF NOT EXISTS / 条件判断），重复执行安全。
            // 已应用过本迁移的库会被 EF 按 ID 直接跳过，因此调整顺序对它们没有任何影响。
            // ══════════════════════════════════════════════════════════════════════
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS zhparser;");

            migrationBuilder.Sql(@"
DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_ts_config WHERE cfgname = 'chinese') THEN
        CREATE TEXT SEARCH CONFIGURATION chinese (PARSER = zhparser);
        ALTER TEXT SEARCH CONFIGURATION chinese ADD MAPPING FOR n,v,a,i,e,l,j,q WITH simple;
    END IF;
END
$$;");

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorId",
                table: "Posts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByUserId",
                table: "Posts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Posts",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "setweight(to_tsvector('chinese', coalesce(\"Title\", '')), 'A') || setweight(to_tsvector('chinese', coalesce(\"Summary\", '')), 'B') || setweight(to_tsvector('chinese', coalesce(\"Content\", '')), 'C')",
                stored: true);

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CoverImage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TokenVersion = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Authors_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Authors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PostCollections",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CollectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostCollections", x => new { x.PostId, x.CollectionId });
                    table.ForeignKey(
                        name: "FK_PostCollections_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostCollections_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Collections",
                columns: new[] { "Id", "CoverImage", "CreatedAt", "DeletedAt", "Description", "IsDeleted", "IsPublished", "Slug", "SortOrder", "Title", "Version" },
                values: new object[] { new Guid("6f2a1b3c-0000-0000-0000-000000000040"), "", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "把多篇文章组织成一个系列（专栏）。可在后台「专栏管理」中修改或删除。", false, true, "sample-collection", 0, "示例专栏", 1 });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "AuthorId", "CreatedAt", "DeletedAt", "Email", "IsActive", "IsDeleted", "LastLoginAt", "PasswordHash", "Role", "TokenVersion", "Version" },
                values: new object[] { new Guid("6f2a1b3c-0000-0000-0000-000000000030"), null, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "admin@example.com", true, false, null, "pbkdf2-sha512$210000$Rc6nVI3/2LQ+vmRlYDCCyQ==$RbxCnKNUm29IVZ72izuS7pFoH/W8t5KkIvGZiOu/YLQ=", "Admin", 1, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CreatedByUserId",
                table: "Posts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_Slug",
                table: "Collections",
                column: "Slug",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_SortOrder",
                table: "Collections",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_PostCollections_CollectionId",
                table: "PostCollections",
                column: "CollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_AuthorId",
                table: "Users",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_Posts_Users_CreatedByUserId",
                table: "Posts",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // GIN 索引：tsvector 只在有索引时才能高效检索（倒排索引）
            // 注意：`chinese` 检索配置在 **本方法最前面** 就已经建好了（见那里的说明，G12）。
            migrationBuilder.Sql(@"CREATE INDEX ix_posts_search ON ""Posts"" USING gin (""SearchVector"");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Posts_Users_CreatedByUserId",
                table: "Posts");

            migrationBuilder.DropTable(
                name: "PostCollections");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropIndex(
                name: "IX_Posts_CreatedByUserId",
                table: "Posts");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_posts_search;");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Posts");

            migrationBuilder.AlterColumn<Guid>(
                name: "AuthorId",
                table: "Posts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
