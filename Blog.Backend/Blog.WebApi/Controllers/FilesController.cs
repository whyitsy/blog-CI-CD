using Blog.Application.Common;
using Blog.Application.Interfaces;
using Blog.Infrastructure.Files;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Blog.WebApi.Controllers
{
    /// <summary>
    /// 文件接口：所有图片等文件的上传与读取都经过此独立接口（不使用静态文件中间件）。
    /// </summary>
    [ApiController]
    [Route("api/files")]
    public class FilesController : ControllerBase
    {
        private readonly IFileStorageService _storage;
        private readonly FileStorageOptions _options;

        public FilesController(IFileStorageService storage, IOptions<FileStorageOptions> options)
        {
            _storage = storage;
            _options = options.Value;
        }

        /// <summary>上传文件，返回可访问的相对 URL</summary>
        [HttpPost("upload")]
        [RequestSizeLimit(50 * 1024 * 1024)]
        public async Task<ApiResponse<object>> Upload(IFormFile file, CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0)
                return ApiResponse<object>.Fail(ErrorCodes.InvalidArgument, "文件不能为空");

            if (file.Length > _options.MaxFileSize)
                return ApiResponse<object>.Fail(ErrorCodes.InvalidArgument,
                    $"文件大小超过限制（{(_options.MaxFileSize / 1024 / 1024)}MB）");

            try
            {
                await using var stream = file.OpenReadStream();
                var url = await _storage.SaveAsync(stream, file.FileName, file.ContentType, cancellationToken);
                return ApiResponse<object>.Ok(new { url });
            }
            catch (ArgumentException ex)
            {
                return ApiResponse<object>.Fail(ErrorCodes.InvalidArgument, ex.Message);
            }
        }

        /// <summary>读取文件（如 /api/files/2026/09/xxx.png），带缓存头</summary>
        [HttpGet("{**path}")]
        [ResponseCache(Duration = 86400, Location = ResponseCacheLocation.Any)]
        public async Task<IActionResult> Get(string path, CancellationToken cancellationToken)
        {
            var result = await _storage.GetAsync(path, cancellationToken);
            if (result is null)
                return NotFound(ApiResponse.Fail(ErrorCodes.NotFound, "文件不存在"));

            var (stream, contentType) = result.Value;
            return File(stream, contentType, enableRangeProcessing: true);
        }
    }
}
