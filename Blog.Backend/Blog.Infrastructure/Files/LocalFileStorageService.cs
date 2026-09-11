using Blog.Application.Interfaces;
using Blog.Infrastructure.Images;
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
        private readonly IImageOptimizer _optimizer;
        private readonly ILogger<LocalFileStorageService> _logger;

        public LocalFileStorageService(
            IOptions<FileStorageOptions> options,
            IImageOptimizer optimizer,
            ILogger<LocalFileStorageService> logger)
        {
            _options = options.Value;
            _optimizer = optimizer;
            _logger = logger;
        }

        public async Task<StoredFile> SaveAsync(Stream content, string fileName, string? contentType = null, CancellationToken cancellationToken = default)
        {
            var extension = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
                throw new ArgumentException($"不支持的文件类型：{extension}");

            // 位图先转 WebP（转换失败或不该转时原样返回，见 IImageOptimizer 约定）
            var optimized = await _optimizer.OptimizeAsync(content, extension, cancellationToken);

            var now = DateTimeOffset.UtcNow;
            var relativeDir = Path.Combine(now.Year.ToString("D4"), now.Month.ToString("D2"));
            var storedName = $"{Guid.NewGuid():N}{optimized.Extension}";
            var relativePath = Path.Combine(relativeDir, storedName);

            if (!TryResolveSafePath(relativePath, out var fullPath))
                throw new InvalidOperationException("文件路径解析失败");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

            await using (var output = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await output.WriteAsync(optimized.Content, cancellationToken);
            }

            _logger.LogInformation("文件已保存 {Path} ({Size} bytes，原始 {Original} bytes，转换={Converted})",
                relativePath, optimized.Content.Length, optimized.OriginalLength, optimized.Converted);

            // 统一返回正斜杠的 URL 相对路径
            return new StoredFile(
                $"/api/files/{now.Year:D4}/{now.Month:D2}/{storedName}",
                optimized.Content.Length,
                optimized.OriginalLength,
                optimized.Converted);
        }

        public Task<(Stream Stream, string ContentType)?> GetAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            // 先查运行期上传目录；未命中再查**只读种子目录**（默认头像等随仓库分发的资源）。
            // 两个根目录都走同一套路径穿越校验，种子目录不会成为绕过点。
            if (!TryResolveExistingFile(relativePath, out var fullPath))
                return Task.FromResult<(Stream, string)?>(null);

            var extension = Path.GetExtension(fullPath);
            var contentType = ContentTypes.TryGetValue(extension, out var mapped) ? mapped : "application/octet-stream";

            Stream stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult<(Stream, string)?>((stream, contentType));
        }

        /// <summary>
        /// 依次在「上传根目录 → 种子根目录」中查找文件。
        /// 文件必须已存在；不存在的路径一律返回 false（调用方据此返回 404）。
        /// </summary>
        private bool TryResolveExistingFile(string relativePath, out string fullPath)
        {
            if (TryResolveSafePath(relativePath, out fullPath) && File.Exists(fullPath))
                return true;

            if (!string.IsNullOrWhiteSpace(_options.SeedRoot) &&
                TryResolveSafePathIn(_options.SeedRoot, relativePath, out fullPath) &&
                File.Exists(fullPath))
                return true;

            fullPath = string.Empty;
            return false;
        }

        public bool TryResolveSafePath(string relativePath, out string fullPath) =>
            TryResolveSafePathIn(_options.Root, relativePath, out fullPath);

        /// <summary>
        /// 在指定根目录下解析相对路径，并做目录穿越防护。
        /// 拒绝 `..` 与绝对路径，且解析后的绝对路径必须仍在根目录内。
        /// </summary>
        private static bool TryResolveSafePathIn(string root, string relativePath, out string fullPath)
        {
            fullPath = string.Empty;
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;
            if (string.IsNullOrWhiteSpace(root))
                return false;

            // 防目录穿越：拒绝 .. 与绝对路径
            if (relativePath.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(relativePath))
                return false;

            var rootFull = Path.GetFullPath(root);
            var candidate = Path.GetFullPath(Path.Combine(rootFull, relativePath));

            // 必须严格位于根目录之下（用分隔符界定，避免 /media-seed 被 /media 前缀匹配）
            if (!candidate.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;

            fullPath = candidate;
            return true;
        }
    }

    /// <summary>文件存储配置（对应 appsettings 的 FileStorage 节）</summary>
    public class FileStorageOptions
    {
        public const string SectionName = "FileStorage";

        /// <summary>运行期上传目录（相对工作目录或绝对路径）。**可写**。</summary>
        public string Root { get; set; } = "media";

        /// <summary>
        /// 只读种子资源目录（相对工作目录或绝对路径），用于随仓库分发的默认图片。
        /// 读取时作为 <see cref="Root"/> 未命中后的回退；**不参与上传写入**。为空则禁用回退。
        /// </summary>
        public string SeedRoot { get; set; } = "media-seed";

        /// <summary>单文件大小上限（字节），默认 10MB</summary>
        public long MaxFileSize { get; set; } = 10 * 1024 * 1024;
    }
}
