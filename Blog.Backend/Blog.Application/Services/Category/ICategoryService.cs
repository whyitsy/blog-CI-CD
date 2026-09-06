using Blog.Application.Common;
using Blog.Application.Services.Category;

namespace Blog.Application.Services.Category
{
    public interface ICategoryService
    {
        Task<List<CategoryDto>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);
        Task<CategoryDto> UpdateAsync(Guid id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid id, int version, CancellationToken cancellationToken = default);
    }
}
