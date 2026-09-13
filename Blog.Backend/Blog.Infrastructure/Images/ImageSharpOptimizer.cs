using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace Blog.Infrastructure.Images
{
    /// <summary>图片优化配置（对应 appsettings 的 FileStorage:ImageOptimization 节）</summary>
    public sealed class ImageOptimizationOptions
    {
        public const string SectionName = "FileStorage:ImageOptimization";

        /// <summary>总开关。关闭时所有上传原样保存。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>WebP 有损压缩质量（0-100）。</summary>
        public int WebpQuality { get; set; } = 82;

        /// <summary>最长边上限（像素）。超过则等比缩小；0 表示不缩放。</summary>
        public int MaxDimension { get; set; } = 2560;

        /// <summary>
        /// 像素总数上限，用于防「解压炸弹」（一个几十 KB 的 PNG 可解成几 GB 内存）。
        /// 超过则跳过转换、原样保存。默认 4000 万像素。
        /// </summary>
        public long MaxPixels { get; set; } = 40_000_000;

        /// <summary>参与转换的扩展名（小写，含点）。其余一律原样保存。</summary>
        public string[] ConvertExtensions { get; set; } = [".png", ".jpg", ".jpeg"];
    }

    /// <summary>
    /// 基于 ImageSharp 的 WebP 转换实现。
    ///
    /// 放行（不转换）的每一类情况都在 <see cref="OptimizeAsync"/> 里显式列出，
    /// 因为「没转成功」和「不该转」在运维上是两回事，必须能在日志里区分。
    /// </summary>
    public sealed class ImageSharpOptimizer : IImageOptimizer
    {
        private readonly ImageOptimizationOptions _options;
        private readonly ILogger<ImageSharpOptimizer> _logger;
        private readonly HashSet<string> _convertible;

        public ImageSharpOptimizer(IOptions<ImageOptimizationOptions> options, ILogger<ImageSharpOptimizer> logger)
        {
            _options = options.Value;
            _logger = logger;
            _convertible = new HashSet<string>(
                _options.ConvertExtensions.Select(e => e.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
        }

        public async Task<OptimizationResult> OptimizeAsync(
            Stream source, string extension, CancellationToken cancellationToken = default)
        {
            var normalized = extension.ToLowerInvariant();
            var original = await ReadAllAsync(source, cancellationToken);
            var passthrough = new OptimizationResult(original, normalized, false) { OriginalLength = original.Length };

            if (!_options.Enabled)
                return passthrough;

            if (!_convertible.Contains(normalized))
                return passthrough; // .webp 已是最优；.gif/.ico/.mp4/... 不做转换（.svg 已不允许上传，见 G11）

            try
            {
                // 只读文件头拿尺寸，**不整图解码**，先挡掉解压炸弹
                var info = Image.Identify(original);
                if (info is null)
                {
                    _logger.LogWarning("图片识别失败，原样保存（扩展名 {Extension}）", normalized);
                    return passthrough;
                }

                var pixels = (long)info.Width * info.Height;
                if (_options.MaxPixels > 0 && pixels > _options.MaxPixels)
                {
                    _logger.LogWarning("图片像素数 {Pixels} 超过上限 {Limit}，跳过转换（防解压炸弹）", pixels, _options.MaxPixels);
                    return passthrough;
                }

                using var image = Image.Load(original);

                // 多帧（APNG 是唯一可能落进转换名单的动画格式）转 WebP 有丢帧风险，直接放行。
                // 注：ImageInfo.FrameCount 是 ImageSharp v4 才有的 API，v3 只能加载后看 Frames。
                if (image.Frames.Count > 1)
                {
                    _logger.LogInformation("多帧图片（{Frames} 帧），跳过 WebP 转换以避免丢失动画", image.Frames.Count);
                    return passthrough;
                }

                ResizeIfNeeded(image);

                using var output = new MemoryStream();
                await image.SaveAsWebpAsync(output, new WebpEncoder
                {
                    FileFormat = WebpFileFormatType.Lossy,
                    Quality = _options.WebpQuality,
                }, cancellationToken);

                var webp = output.ToArray();

                // 已经压得很好的小图（典型是优化过的 JPEG），转完可能更大 —— 保留更小的那个
                if (webp.Length >= original.Length)
                {
                    _logger.LogInformation(
                        "WebP 转换无收益（{Webp} >= {Original} 字节），保留原图", webp.Length, original.Length);
                    return passthrough;
                }

                _logger.LogInformation(
                    "图片已转 WebP：{Original} → {Webp} 字节（{Ratio:P0}）",
                    original.Length, webp.Length, 1 - (double)webp.Length / original.Length);

                return new OptimizationResult(webp, ".webp", true) { OriginalLength = original.Length };
            }
            catch (Exception ex)
            {
                // 关键约定：优化失败绝不能导致上传失败
                _logger.LogWarning(ex, "WebP 转换失败，原样保存（扩展名 {Extension}）", normalized);
                return passthrough;
            }
        }

        private void ResizeIfNeeded(Image image)
        {
            var max = _options.MaxDimension;
            if (max <= 0) return;

            var longest = Math.Max(image.Width, image.Height);
            if (longest <= max) return;

            var scale = (double)max / longest;
            var width = Math.Max(1, (int)Math.Round(image.Width * scale));
            var height = Math.Max(1, (int)Math.Round(image.Height * scale));

            _logger.LogInformation("图片超长边 {Max}px，等比缩放 {W}x{H} → {NW}x{NH}",
                max, image.Width, image.Height, width, height);

            image.Mutate(x => x.Resize(width, height));
        }

        private static async Task<byte[]> ReadAllAsync(Stream source, CancellationToken cancellationToken)
        {
            if (source is MemoryStream ms && ms.Position == 0)
                return ms.ToArray();

            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, cancellationToken);
            return buffer.ToArray();
        }
    }
}
