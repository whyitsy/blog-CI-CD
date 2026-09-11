namespace Blog.Infrastructure.Images
{
    /// <summary>图片优化结果。</summary>
    /// <param name="Content">最终要落盘的字节。未转换时是原字节的副本。</param>
    /// <param name="Extension">最终扩展名（含点，小写）。未转换时等于输入扩展名。</param>
    /// <param name="Converted">是否真的做了转换（用于日志与响应体统计）。</param>
    public sealed record OptimizationResult(byte[] Content, string Extension, bool Converted)
    {
        /// <summary>原图字节数，用于计算压缩率。</summary>
        public long OriginalLength { get; init; }
    }

    /// <summary>
    /// 上传图片优化：把位图转成 WebP 以节省传输流量。
    ///
    /// **约定：本接口永不抛异常、永不因为「优化失败」而让上传失败。**
    /// 任何无法安全转换的情况都返回原字节并让调用方照常保存
    /// （宁可存一张大图，也不能让用户丢文件）。
    /// </summary>
    public interface IImageOptimizer
    {
        /// <summary>
        /// 尝试把 <paramref name="source"/> 转为 WebP。
        /// 以下情况**原样返回**：优化被关闭、扩展名不在转换名单、多帧/动画图、
        /// 识别或解码失败、超出像素上限、转换后体积反而更大。
        /// </summary>
        /// <param name="source">输入流（会被完整读取）。</param>
        /// <param name="extension">原始扩展名（含点）。</param>
        Task<OptimizationResult> OptimizeAsync(Stream source, string extension, CancellationToken cancellationToken = default);
    }
}
