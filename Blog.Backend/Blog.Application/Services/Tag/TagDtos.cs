namespace Blog.Application.Services.Tag
{
    /// <summary>标签列表项（含文章数，用于标签墙；Version 为乐观锁版本号）</summary>
    public record TagDto(Guid Id, string Name, int PostCount, int Version);

    public record CreateTagRequest(string Name);

    /// <summary>更新标签：Version 为乐观锁版本号，并发冲突时返回 409</summary>
    public record UpdateTagRequest(string Name, int Version);
}
