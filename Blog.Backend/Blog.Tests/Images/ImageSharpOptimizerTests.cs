using Blog.Infrastructure.Images;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Blog.Tests.Images;

/// <summary>
/// 上传图片转 WebP 的边界行为测试。
///
/// 这里的重点是「**不该转**的情况一个都不能转错**」：把动画压成单帧、把损坏文件丢掉、
/// 把已优化的小图转得更大，都是静默的数据/流量损失，比「没转成功」严重得多。
/// </summary>
public class ImageSharpOptimizerTests
{
    private static ImageSharpOptimizer CreateOptimizer(ImageOptimizationOptions? options = null) =>
        new(Options.Create(options ?? new ImageOptimizationOptions()), NullLogger<ImageSharpOptimizer>.Instance);

    /// <summary>造一张有渐变与噪点的位图 —— 纯色图会被压得极小，不适合观察压缩效果。</summary>
    private static Image<Rgba32> CreatePhotoLikeImage(int width, int height)
    {
        var image = new Image<Rgba32>(width, height);
        var random = new Random(1234); // 固定种子，保证可重复
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (byte)((x * 255 / Math.Max(1, width - 1)) ^ random.Next(0, 32));
                var g = (byte)((y * 255 / Math.Max(1, height - 1)) ^ random.Next(0, 32));
                var b = (byte)(random.Next(0, 256));
                image[x, y] = new Rgba32(r, g, b);
            }
        }
        return image;
    }

    private static byte[] Encode(Image image, IImageEncoder encoder)
    {
        using var ms = new MemoryStream();
        image.Save(ms, encoder);
        return ms.ToArray();
    }

    private static byte[] PngBytes(int w, int h) => Encode(CreatePhotoLikeImage(w, h), new PngEncoder());
    private static byte[] JpegBytes(int w, int h, int quality = 90) =>
        Encode(CreatePhotoLikeImage(w, h), new JpegEncoder { Quality = quality });

    private static async Task<OptimizationResult> RunAsync(byte[] input, string extension, ImageOptimizationOptions? options = null)
    {
        await using var stream = new MemoryStream(input);
        return await CreateOptimizer(options).OptimizeAsync(stream, extension);
    }

    [Fact]
    public async Task PNG_应转成_WebP_且体积显著下降()
    {
        var input = PngBytes(800, 600);

        var result = await RunAsync(input, ".png");

        Assert.True(result.Converted);
        Assert.Equal(".webp", result.Extension);
        Assert.True(result.Content.Length < input.Length, $"预期变小：{input.Length} → {result.Content.Length}");
        Assert.Equal(input.Length, result.OriginalLength);

        // 产物必须真的是 WebP，且尺寸不变
        using var decoded = Image.Load(result.Content);
        Assert.Same(WebpFormat.Instance, decoded.Metadata.DecodedImageFormat);
        Assert.Equal(800, decoded.Width);
        Assert.Equal(600, decoded.Height);
    }

    [Fact]
    public async Task JPEG_也应转成_WebP()
    {
        var input = JpegBytes(800, 600);

        var result = await RunAsync(input, ".jpg");

        Assert.True(result.Converted);
        Assert.Equal(".webp", result.Extension);
    }

    [Fact]
    public async Task 扩展名大小写不敏感()
    {
        var input = PngBytes(400, 300);

        var result = await RunAsync(input, ".PNG");

        Assert.True(result.Converted);
        Assert.Equal(".webp", result.Extension);
    }

    [Theory]
    [InlineData(".gif")]   // 动画风险，默认不在名单
    [InlineData(".svg")]   // 矢量图，不能栅格化
    [InlineData(".ico")]
    [InlineData(".webp")]  // 已是目标格式
    public async Task 不在转换名单的扩展名应原样返回(string extension)
    {
        var input = new byte[] { 1, 2, 3, 4, 5 };

        var result = await RunAsync(input, extension);

        Assert.False(result.Converted);
        Assert.Equal(extension, result.Extension);
        Assert.Equal(input, result.Content); // 字节必须完全一致
    }

    [Fact]
    public async Task 损坏的_PNG_不得抛异常且字节原样保留()
    {
        var corrupt = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xDE, 0xAD, 0xBE, 0xEF };

        var result = await RunAsync(corrupt, ".png");

        Assert.False(result.Converted);
        Assert.Equal(".png", result.Extension);
        Assert.Equal(corrupt, result.Content);
    }

    [Fact]
    public async Task 非图片内容伪装成_PNG_也不得抛异常()
    {
        var notAnImage = System.Text.Encoding.UTF8.GetBytes("这不是一张图片，只是普通文本。");

        var result = await RunAsync(notAnImage, ".png");

        Assert.False(result.Converted);
        Assert.Equal(notAnImage, result.Content);
    }

    [Fact]
    public async Task 多帧图片应原样返回而非压成单帧()
    {
        // 默认名单里没有 .gif，所以这里把 .gif 加进名单，
        // 专门验证「多帧守卫」而不是被扩展名名单提前挡掉。
        using var animated = new Image<Rgba32>(64, 64);
        animated.Frames.AddFrame(CreatePhotoLikeImage(64, 64).Frames.RootFrame);
        animated.Frames.AddFrame(CreatePhotoLikeImage(64, 64).Frames.RootFrame);
        Assert.True(animated.Frames.Count > 1);
        var input = Encode(animated, new GifEncoder());

        var result = await RunAsync(input, ".gif", new ImageOptimizationOptions
        {
            ConvertExtensions = [".gif", ".png"],
        });

        Assert.False(result.Converted);
        Assert.Equal(".gif", result.Extension);
        Assert.Equal(input, result.Content);
    }

    [Fact]
    public async Task 超过最长边应等比缩放()
    {
        var input = PngBytes(3000, 1500); // 3:2

        var result = await RunAsync(input, ".png", new ImageOptimizationOptions { MaxDimension = 1000 });

        Assert.True(result.Converted);
        using var decoded = Image.Load(result.Content);
        Assert.Equal(1000, decoded.Width);
        Assert.Equal(500, decoded.Height); // 保持 3:2
    }

    [Fact]
    public async Task 未超过最长边不应缩放()
    {
        var input = PngBytes(1200, 600);

        var result = await RunAsync(input, ".png", new ImageOptimizationOptions { MaxDimension = 2560 });

        using var decoded = Image.Load(result.Content);
        Assert.Equal(1200, decoded.Width);
        Assert.Equal(600, decoded.Height);
    }

    [Fact]
    public async Task MaxDimension_为_0_表示不缩放()
    {
        var input = PngBytes(4000, 100);

        var result = await RunAsync(input, ".png", new ImageOptimizationOptions { MaxDimension = 0 });

        using var decoded = Image.Load(result.Content);
        Assert.Equal(4000, decoded.Width);
    }

    [Fact]
    public async Task 关掉开关后一切原样返回()
    {
        var input = PngBytes(800, 600);

        var result = await RunAsync(input, ".png", new ImageOptimizationOptions { Enabled = false });

        Assert.False(result.Converted);
        Assert.Equal(input, result.Content);
    }

    [Fact]
    public async Task 转换后更大时应保留原图()
    {
        // 用一张已经被压到很低质量的 JPEG，再用高 WebP 质量重编码 —— 转出来必然更大。
        var input = JpegBytes(600, 400, quality: 20);

        var result = await RunAsync(input, ".jpg", new ImageOptimizationOptions { WebpQuality = 100 });

        Assert.False(result.Converted);
        Assert.Equal(".jpg", result.Extension);
        Assert.Equal(input, result.Content);
    }

    [Fact]
    public async Task 像素数超上限应跳过转换()
    {
        var input = PngBytes(400, 400); // 160_000 像素

        var result = await RunAsync(input, ".png", new ImageOptimizationOptions { MaxPixels = 100_000 });

        Assert.False(result.Converted);
        Assert.Equal(input, result.Content);
    }

    [Fact]
    public async Task 任何情况下产物都不得比原图更大()
    {
        // 不变量：Converted=true 时必然更小；否则就是原样返回。
        var cases = new (byte[] Bytes, string Ext, ImageOptimizationOptions Options)[]
        {
            (PngBytes(200, 200), ".png", new ImageOptimizationOptions()),
            (JpegBytes(200, 200, 20), ".jpg", new ImageOptimizationOptions { WebpQuality = 100 }),
            (PngBytes(64, 64), ".png", new ImageOptimizationOptions { WebpQuality = 100 }),
            (PngBytes(50, 50), ".png", new ImageOptimizationOptions { WebpQuality = 95, MaxDimension = 10 }),
        };

        foreach (var (bytes, ext, options) in cases)
        {
            var result = await RunAsync(bytes, ext, options);
            Assert.True(
                result.Content.Length <= bytes.Length,
                $"扩展名 {ext}：产物 {result.Content.Length} 大于原始 {bytes.Length}（Converted={result.Converted}）");
            if (result.Converted)
                Assert.True(result.Content.Length < bytes.Length);
        }
    }

    [Fact]
    public async Task 空流不应抛异常()
    {
        await using var empty = new MemoryStream([]);

        var result = await CreateOptimizer().OptimizeAsync(empty, ".png");

        Assert.False(result.Converted);
        Assert.Empty(result.Content);
    }
}
