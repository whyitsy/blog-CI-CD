namespace Blog.Application.Services.Category
{
    /// <summary>分类列表项（含文章数，用于分类墙；Version 为乐观锁版本号）</summary>
    public record CategoryDto(Guid Id, string Name, int PostCount, int Version);

    public record CreateCategoryRequest(string Name);

    /// <summary>更新分类：Version 为乐观锁版本号，并发冲突时返回 409</summary>
    public record UpdateCategoryRequest(string Name, int Version);
}
