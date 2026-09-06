using Blog.Application.Services.Category;
using Blog.Application.Services.Tag;

namespace Blog.Application.Interfaces
{
    /// <summary>分类只读查询</summary>
    public interface ICategoryQueryRepository
    {
        Task<List<CategoryDto>> GetAllWithPostCountAsync(CancellationToken cancellationToken = default);
    }

    /// <summary>标签只读查询</summary>
    public interface ITagQueryRepository
    {
        Task<List<TagDto>> GetAllWithPostCountAsync(CancellationToken cancellationToken = default);
    }
}
