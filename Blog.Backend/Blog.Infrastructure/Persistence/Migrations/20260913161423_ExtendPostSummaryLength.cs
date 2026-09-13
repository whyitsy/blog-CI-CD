using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Blog.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// 把 <c>Posts.Summary</c> 从 <c>varchar(120)</c> 扩到 <c>varchar(200)</c>。
    ///
    /// <para>⚠️ <b>这里不能只写一个 AlterColumn。</b></para>
    /// Summary 被**生成列** <c>SearchVector</c> 引用
    /// （<c>setweight(to_tsvector('chinese', …Summary…), 'B')</c>），
    /// 而 PostgreSQL 明确禁止修改被生成列依赖的列类型：
    ///
    /// <code>
    /// ERROR:  cannot alter type of a column used by a generated column
    /// DETAIL:  Column "Summary" is used by generated column "SearchVector".
    /// </code>
    ///
    /// 所以顺序必须是「**先拆掉生成列与它的 GIN 索引 → 改类型 → 再按原表达式重建**」。
    /// 重建生成列时 PostgreSQL 会为全部既有行重算 SearchVector，索引随之重建。
    ///
    /// <para>这个坑不会在编译期暴露，单元测试也发现不了 —— 只有真正对库执行迁移才会炸。
    /// 而迁移是在**应用启动时**跑的（<c>Program.cs</c> 的 <c>Database.Migrate()</c>），
    /// 也就是说：**写错这一处，应用直接起不来**。
    /// 好在 EF 只是把「改列类型」翻译成了一条 ALTER，并不会替我们发现这个依赖，
    /// 所以自动生成的迁移必须人工复核。</para>
    /// </summary>
    public partial class ExtendPostSummaryLength : Migration
    {
        /// <summary>
        /// SearchVector 的生成表达式，与 <c>20260910205225_AddAuthCollectionsAndFts</c> 中完全一致。
        /// 抽成常量是为了让 Up/Down 不会因为手抄不一致而产生"来回迁移后检索行为变了"。
        /// </summary>
        private const string SearchVectorSql =
            "setweight(to_tsvector('chinese', coalesce(\"Title\", '')), 'A') || " +
            "setweight(to_tsvector('chinese', coalesce(\"Summary\", '')), 'B') || " +
            "setweight(to_tsvector('chinese', coalesce(\"Content\", '')), 'C')";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ① 拆掉依赖 Summary 的生成列。GIN 索引建在它上面，会随列一起被删掉，
            //    但这里仍然显式 DROP INDEX —— 免得将来有人调整顺序时踩空。
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_posts_search;");
            migrationBuilder.DropColumn(name: "SearchVector", table: "Posts");

            // ② 现在才可以改类型
            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "Posts",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            // ③ 按原表达式重建生成列与 GIN 索引（既有行会被重算）
            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Posts",
                type: "tsvector",
                nullable: true,
                computedColumnSql: SearchVectorSql,
                stored: true);

            migrationBuilder.Sql(@"CREATE INDEX ix_posts_search ON ""Posts"" USING gin (""SearchVector"");");
        }

        /// <inheritdoc />
        /// <remarks>
        /// 回滚是**尽力而为**：如果库里已经有超过 120 字的摘要，改回 120 会被 PostgreSQL 拒绝。
        /// 这是符合预期的 —— 缩短列宽本来就该失败，而不是悄悄截断数据。
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ix_posts_search;");
            migrationBuilder.DropColumn(name: "SearchVector", table: "Posts");

            migrationBuilder.AlterColumn<string>(
                name: "Summary",
                table: "Posts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Posts",
                type: "tsvector",
                nullable: true,
                computedColumnSql: SearchVectorSql,
                stored: true);

            migrationBuilder.Sql(@"CREATE INDEX ix_posts_search ON ""Posts"" USING gin (""SearchVector"");");
        }
    }
}
