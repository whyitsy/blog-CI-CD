namespace Blog.Application.Interfaces
{
    /// <summary>文件存储抽象（本地磁盘实现，可替换为 OSS/S3）</summary>
    public interface IFileStorageService
    {
        /// <summary>
        /// 保存文件，返回可访问的相对 URL 与体积统计。
        /// 位图会被自动转为 WebP（配置见 FileStorage:ImageOptimization），
        /// 因此落盘的扩展名可能与上传时的扩展名不同。
        /// </summary>
        Task<StoredFile> SaveAsync(Stream content, string fileName, string? contentType = null, CancellationToken cancellationToken = default);

        /// <summary>按相对路径读取文件，不存在时返回 null</summary>
        Task<(Stream Stream, string ContentType)?> GetAsync(string relativePath, CancellationToken cancellationToken = default);

        /// <summary>校验并解析相对路径为物理路径（防目录穿越）</summary>
        bool TryResolveSafePath(string relativePath, out string fullPath);
    }

    /// <summary>保存结果。</summary>
    /// <param name="Url">可访问的相对 URL。</param>
    /// <param name="StoredBytes">实际落盘字节数。</param>
    /// <param name="OriginalBytes">上传时的原始字节数。</param>
    /// <param name="Converted">是否经过 WebP 转换（false 表示原样保存）。</param>
    public sealed record StoredFile(string Url, long StoredBytes, long OriginalBytes, bool Converted);
}
