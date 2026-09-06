using Blog.Application.Common;
using Blog.Application.Services.Category;
using Microsoft.AspNetCore.Mvc;

namespace Blog.WebApi.Controllers
{
    /// <summary>分类：按钮墙 + 管理</summary>
    [ApiController]
    [Route("api/categories")]
    public class CategoriesController : ControllerBase
    {
        private readonly ICategoryService _categories;

        public CategoriesController(ICategoryService categories)
        {
            _categories = categories;
        }

        /// <summary>全部分类（含已发布文章数，用于分类页按钮墙）</summary>
        [HttpGet]
        public async Task<ApiResponse<List<CategoryDto>>> GetAll(CancellationToken cancellationToken)
        {
            var items = await _categories.GetAllAsync(cancellationToken);
            return ApiResponse<List<CategoryDto>>.Ok(items);
        }

        [HttpPost]
        public async Task<ApiResponse<CategoryDto>> Create([FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
        {
            var created = await _categories.CreateAsync(request, cancellationToken);
            return ApiResponse<CategoryDto>.Ok(created);
        }

        [HttpPut("{id:guid}")]
        public async Task<ApiResponse<CategoryDto>> Update(Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken)
        {
            var updated = await _categories.UpdateAsync(id, request, cancellationToken);
            return ApiResponse<CategoryDto>.Ok(updated);
        }

        [HttpDelete("{id:guid}")]
        public async Task<ApiResponse<object?>> Delete(Guid id, [FromQuery] int version, CancellationToken cancellationToken)
        {
            await _categories.DeleteAsync(id, version, cancellationToken);
            return ApiResponse.Ok();
        }
    }
}
