using Blog.Application.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Blog.Infrastructure.Files
{
    /// <summary>
    /// 本地磁盘文件存储。文件保存在 {Root}/yyyy/MM/{guid}{ext}，
    /// 通过 WebApi 的独立文件接口（/api/files/**）读取，不启用静态文件中间件。
    /// </summary>
    public class LocalFileStorageService : IFileStorageService
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".ico",
            ".mp4", ".webm", ".pdf", ".zip"
        };

        private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".ico"] = "image/x-icon",
            [".mp4"] = "video/mp4",
            [".webm"] = "video/webm",
            [".pdf"] = "application/pdf",
            [".zip"] = "application/zip"
        };

        private readonly FileStorageOptions _options;
        private readonly ILogger<LocalFileStorageService> _logger;

        public LocalFileStorageService(IOptions<FileStorageOptions> options, ILogger<LocalFileStorageService> logger)
        {
            _options = options.Value;
            _logger = logger;
        }

        public async Task<string> SaveAsync(Stream content, string fileName, string? contentType = null, CancellationToken cancellationToken = default)
        {
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
                throw new ArgumentException($"不支持的文件类型：{extension}");

            var now = DateTimeOffset.UtcNow;
            var relativeDir = Path.Combine(now.Year.ToString("D4"), now.Month.ToString("D2"));
            var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            var relativePath = Path.Combine(relativeDir, storedName);

            if (!TryResolveSafePath(relativePath, out var fullPath))
                throw new InvalidOperationException("文件路径解析失败");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(output, cancellationToken);
            }

            _logger.LogInformation("文件已保存 {Path} ({Size} bytes)", relativePath, new FileInfo(fullPath).Length);

            // 统一返回正斜杠的 URL 相对路径
            return $"/api/files/{now.Year:D4}/{now.Month:D2}/{storedName}";
        }

        public Task<(Stream Stream, string ContentType)?> GetAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            if (!TryResolveSafePath(relativePath, out var fullPath) || !File.Exists(fullPath))
                return Task.FromResult<(Stream, string)?>(null);

            var extension = Path.GetExtension(fullPath);
            var contentType = ContentTypes.TryGetValue(extension, out var mapped) ? mapped : "application/octet-stream";

            Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult<(Stream, string)?>((stream, contentType));
        }

        public bool TryResolveSafePath(string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            // 防目录穿越：拒绝 .. 与绝对路径
            if (relativePath.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
                return false;

            var root = Path.GetFullPath(_options.Root);
            var candidate = Path.GetFullPath(Path.Combine(root, relativePath));

            if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return false;

            fullPath = candidate;
            return true;
        }
    }

    /// <summary>文件存储配置（对应 appsettings 的 FileStorage 节）</summary>
    public class FileStorageOptions
    {
        public const string SectionName = "FileStorage";

        /// <summary>存储根目录（相对工作目录或绝对路径）</summary>
        public string Root { get; set; } = "media";

        /// <summary>单文件大小上限（字节），默认 10MB</summary>
        public long MaxFileSize { get; set; } = 10 * 1024 * 1024;
    }
}
