using Blog.Application.Common;
using Blog.Application.Common.Exceptions;
using Blog.Application.Interfaces;
using Blog.Infrastructure.Files;
using Microsoft.AspNetCore.Authorization;
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

        /// <summary>
        /// 上传文件，返回可访问的相对 URL。
        /// 位图会自动转 WebP，响应里带回转换前后的字节数以便前端提示压缩效果。
        ///
        /// **需要 ContentWriter**（Admin 或 Author）：上传是写操作，
        /// 匿名开放会让任何人都能往服务器塞文件（耗尽磁盘）。
        /// 读取（下面的 GET）保持公开——文章封面/头像必须能被匿名访客看到。
        ///
        /// <para><b>错误一律走异常，不在这里 return 错误响应体。</b></para>
        /// 直接 <c>return ApiResponse.Fail(...)</c> 会得到一个 **HTTP 200** 的响应，
        /// 只有 body 里的 <c>code</c> 是错误码 —— 于是「参数不合法」这一个语义，
        /// 在上传接口上是 200、在其它接口上是 400，客户端必须记得"上传只看 code"。
        /// 抛 <see cref="BusinessException"/> 则由全局中间件统一映射为
        /// 400 / 413 + 统一响应体，与其它接口完全一致。
        /// </summary>
        [HttpPost("upload")]
        [Authorize(Policy = "ContentWriter")]
        [RequestSizeLimit(50 * 1024 * 1024)]
        public async Task<ApiResponse<object>> Upload(IFormFile file, CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0)
                throw new BusinessException("文件不能为空", ErrorCodes.InvalidArgument);

            if (file.Length > _options.MaxFileSize)
                throw new BusinessException(
                    $"文件大小超过限制（{_options.MaxFileSize / 1024 / 1024}MB）",
                    // 413 才是"请求体过大"的标准状态码；4130 这个业务码此前定义了却从没被用过
                    ErrorCodes.PayloadTooLarge);

            StoredFile stored;
            try
            {
                await using var stream = file.OpenReadStream();
                stored = await _storage.SaveAsync(stream, file.FileName, file.ContentType, cancellationToken);
            }
            catch (ArgumentException ex)
            {
                // 存储层用 ArgumentException 表达「这个文件我不收」（扩展名不在白名单等）。
                // 转成业务异常，交给全局中间件统一处理。
                throw new BusinessException(ex.Message, ErrorCodes.InvalidArgument);
            }

            return ApiResponse<object>.Ok(new
            {
                url = stored.Url,
                storedBytes = stored.StoredBytes,
                originalBytes = stored.OriginalBytes,
                converted = stored.Converted,
            });
        }

        /// <summary>读取文件（如 /api/files/2026/09/xxx.png），命中时下发长缓存</summary>
        [HttpGet("{**path}")]
        public async Task<IActionResult> Get(string path, CancellationToken cancellationToken)
        {
            var result = await _storage.GetAsync(path, cancellationToken);

            // 注意：缓存头**只能**在命中文件时下发。
            // 之前这里用 [ResponseCache] 标注 action，会给「文件不存在」的 404 也盖上
            // `public, max-age=86400`，于是浏览器把 404 缓存一整天：等文件补回来（或部署完成后）
            // 用户仍然长时间看到空白背景，必须手动强刷才能恢复。
            if (result is null)
                return NotFound(ApiResponse.Fail(ErrorCodes.NotFound, "文件不存在"));

            var (stream, contentType) = result.Value;
            Response.Headers.CacheControl = "public,max-age=86400";
            return File(stream, contentType, enableRangeProcessing: true);
        }
    }
}
