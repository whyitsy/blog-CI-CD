namespace Blog.Application.Interfaces
{
    /// <summary>文件存储抽象（本地磁盘实现，可替换为 OSS/S3）</summary>
    public interface IFileStorageService
    {
        /// <summary>保存文件，返回可访问的相对 URL（如 /media/2026/09/xxx.png）</summary>
        Task<string> SaveAsync(Stream content, string fileName, string? contentType = null, CancellationToken cancellationToken = default);

        /// <summary>按相对路径读取文件，不存在时返回 null</summary>
        Task<(Stream Stream, string ContentType)?> GetAsync(string relativePath, CancellationToken cancellationToken = default);

        /// <summary>校验并解析相对路径为物理路径（防目录穿越）</summary>
        bool TryResolveSafePath(string relativePath, out string fullPath);
    }
}
