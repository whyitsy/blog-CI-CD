using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Blog.Application.Common.Exceptions;

namespace Blog.Application.Common
{
    /// <summary>
    /// 站内媒体地址白名单校验。
    ///
    /// <para><b>背景</b></para>
    /// 作者头像、文章封面、站点 Logo、首屏背景图这四个字段本质上是「一个图片地址」。
    /// 改造前前端把它们做成了**自由文本框**，后端又**原样存库、原样下发**，
    /// 于是任何能调写接口的人（作者可以改自己的头像、可以发文章）都能塞进任意 URL：
    /// <list type="bullet">
    ///   <item><c>https://evil.com/x.png</c> —— 外链图片：访客 IP/UA 泄露给第三方，还会触发混合内容告警</item>
    ///   <item><c>//evil.com/x.png</c> —— 协议相对 URL，同上但更隐蔽</item>
    ///   <item><c>javascript:alert(1)</c> —— 今天只进 <c>&lt;img src&gt;</c> 看似无害，但哪天被复用到
    ///         <c>&lt;a href&gt;</c> 或 <c>window.open</c> 就是 XSS</item>
    ///   <item><c>data:image/svg+xml;base64,...</c> —— data URI，SVG 内部可携带脚本</item>
    /// </list>
    ///
    /// <para><b>为什么是白名单</b></para>
    /// 刻意不去写「禁止 http: / javascript: / data:」这种黑名单：黑名单只能挡住已经想到的写法，
    /// 而白名单（只认本服务 <c>/api/files/</c> 前缀 + 受限字符集）天然挡住宿主没想到的。
    ///
    /// <para><b>职责边界</b></para>
    /// 本类只回答「这个字符串能不能当作站内图片地址」。真正的路径穿越防护仍在
    /// <c>LocalFileStorageService.TryResolveSafePathIn</c>（读文件那一层），两者是纵深防御：
    /// 即使这里被绕过，文件读取层依然会把 <c>..</c> 挡掉。
    ///
    /// <para><b>读写策略不同</b></para>
    /// 写入（Create/Update）走 <see cref="Validate"/>，非法即拒绝；
    /// 读取走 <see cref="TryParseList"/>，对历史数据保持宽容，避免一次校验规则升级就把老数据读崩。
    /// </summary>
    public static class MediaPath
    {
        /// <summary>本服务签发媒体地址的固定前缀（见 LocalFileStorageService.SaveAsync 的返回值）</summary>
        public const string OwnedPrefix = "/api/files/";

        /// <summary>
        /// 单个媒体地址的长度上限。
        ///
        /// <para>取值 = <b>所有目标列里最小的那一个</b>：<c>Authors.Avatar</c> 是
        /// <c>varchar(200)</c>，而 <c>Posts.CoverImage</c> / <c>Collections.CoverImage</c>
        /// 是 <c>varchar(500)</c>。取最小值才能保证「通过白名单的值一定能存进任何一个目标列」——
        /// 否则会出现 201~500 字符的地址**过了白名单、却在写头像时撞数据库约束**的怪事，
        /// 而那又会表现为「服务器内部错误」。</para>
        ///
        /// <para>实际上本站生成的地址约 53 字符
        /// （<c>/api/files/yyyy/MM/{32位十六进制}.webp</c>），200 有充足余量。</para>
        /// </summary>
        public const int MaxLength = FieldLimits.AuthorAvatar;

        /// <summary>列表型字段（如首屏背景图）最多允许的条数</summary>
        public const int MaxItems = 20;

        /// <summary>非法地址的统一提示（前缀会被拼上字段名，如「封面只允许使用…」）</summary>
        public const string ExternalUrlMessage = "只允许使用本站上传的图片（地址须以 /api/files/ 开头），不支持填写外部链接";

        /// <summary>
        /// 判断地址是否为本服务自己签发的媒体地址。
        /// <para>合法取值有两类：<c>null</c>/空白（表示「未设置」，会被归一化为空串），
        /// 以及以 <see cref="OwnedPrefix"/> 开头、且路径部分只含白名单字符的相对路径。</para>
        /// </summary>
        /// <param name="value">待校验的地址</param>
        /// <param name="error">不合法时的原因（不包含字段名，由调用方拼接）</param>
        public static bool IsOwned(string? value, [NotNullWhen(false)] out string? error)
        {
            error = null;

            if (value is null)
                return true;

            var trimmed = value.Trim();
            if (trimmed.Length == 0)
                return true;

            if (trimmed.Length > MaxLength)
            {
                error = $"地址长度不能超过 {MaxLength}";
                return false;
            }

            if (!trimmed.StartsWith(OwnedPrefix, StringComparison.Ordinal))
            {
                error = ExternalUrlMessage;
                return false;
            }

            var relative = trimmed[OwnedPrefix.Length..];
            if (relative.Length == 0)
            {
                error = "地址缺少文件名";
                return false;
            }

            // 形状检查。本站签发的地址形如 {yyyy}/{MM}/{32位十六进制}.{ext}，
            // 于是路径部分有三条硬约束：
            //   1. 不能以 / 开头 —— 否则是 /api/files//... 这种归一化歧义写法
            //   2. 不能以 / 结尾 —— 指向目录，永远不可能是文件（如 /api/files/2026/09/）
            //   3. 不能含 .. 或 // —— 目录穿越 / 协议相对 URL
            if (relative[0] == '/')
            {
                error = "地址包含非法的路径片段";
                return false;
            }

            if (relative[^1] == '/')
            {
                error = "地址缺少文件名";
                return false;
            }

            if (relative.Contains("..", StringComparison.Ordinal) ||
                relative.Contains("//", StringComparison.Ordinal))
            {
                error = "地址包含非法的路径片段";
                return false;
            }

            // 字符集白名单：只放行我们自己生成文件名会用到的字符
            // （LocalFileStorageService 生成的是 32 位十六进制 GUID + 扩展名，种子文件是 avatar-default.webp）。
            // 这一步顺带挡掉 %（编码绕过）、:（scheme）、? / #（查询与片段）、\（Windows 分隔符）。
            foreach (var c in relative)
            {
                if (!IsAllowedPathChar(c))
                {
                    error = "地址包含非法字符";
                    return false;
                }
            }

            return true;
        }

        /// <summary>把空白归一化为空串（「未设置」的唯一表示），其余仅去首尾空白</summary>
        public static string Normalize(string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

        /// <summary>
        /// 写入前的强校验：不合法直接抛 <see cref="BusinessException"/>（错误码 4001）。
        /// </summary>
        /// <param name="value">待校验的地址</param>
        /// <param name="fieldName">字段中文名，用于拼出可读的错误提示（如「封面」「头像」）</param>
        /// <returns>归一化后的地址（未设置时为空串）</returns>
        public static string Validate(string? value, string fieldName)
        {
            if (!IsOwned(value, out var error))
                throw new BusinessException($"{fieldName}：{error}", ErrorCodes.InvalidArgument);

            return Normalize(value);
        }

        /// <summary>
        /// 解析「媒体地址列表」配置项（Value 存 JSON 数组，如首屏背景图）。
        /// 对历史数据宽容：解析失败时把整个字符串当作单元素列表——
        /// 本次改造之前 HeroBackground 存的就是一个裸地址。
        /// </summary>
        public static List<string> ParseList(string? json)
        {
            var raw = json?.Trim();
            if (string.IsNullOrEmpty(raw))
                return [];

            if (raw[0] != '[')
                return [raw];

            try
            {
                return JsonSerializer.Deserialize<List<string>>(raw)?
                    .Select(Normalize)
                    .Where(s => s.Length > 0)
                    .ToList() ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        /// <summary>
        /// 写入前的「列表型」配置强校验（首屏背景图）：解析 → 逐项白名单 → 回写规范化后的 JSON 数组。
        /// 与 <see cref="ParseList"/> 的关键区别是**不做静默兜底**——JSON 格式错误直接报 4001，
        /// 否则一段打错的 JSON 会被悄悄存成空数组，配置项就这么无声无息地丢了。
        /// </summary>
        /// <param name="json">前端提交的 JSON 数组字符串；兼容改造前存的单个裸地址</param>
        /// <param name="fieldName">字段中文名</param>
        /// <returns>规范化后的 JSON 数组字符串（可直接存库）</returns>
        public static string ValidateJsonList(string? json, string fieldName)
        {
            var items = ParseListForWrite(json, fieldName);

            if (items.Count > MaxItems)
                throw new BusinessException($"{fieldName}：最多允许 {MaxItems} 张", ErrorCodes.InvalidArgument);

            var result = new List<string>(items.Count);
            foreach (var item in items)
            {
                if (!IsOwned(item, out var error))
                    throw new BusinessException($"{fieldName}：{error}", ErrorCodes.InvalidArgument);

                var normalized = Normalize(item);
                if (normalized.Length > 0)
                    result.Add(normalized);
            }

            return JsonSerializer.Serialize(result);
        }

        /// <summary>写入前的列表解析：空 → 空列表；裸地址 → 单元素；JSON 数组 → 逐项；格式错误 → 抛 4001</summary>
        private static List<string?> ParseListForWrite(string? json, string fieldName)
        {
            var raw = json?.Trim();
            if (string.IsNullOrEmpty(raw))
                return [];

            if (raw[0] != '[')
                return [raw];

            try
            {
                return JsonSerializer.Deserialize<List<string?>>(raw)?
                    .Select(s => (string?)s)
                    .ToList() ?? [];
            }
            catch (JsonException)
            {
                throw new BusinessException($"{fieldName}：配置不是合法的 JSON 数组", ErrorCodes.InvalidArgument);
            }
        }

        private static bool IsAllowedPathChar(char c) =>
            c is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '/'
                or '.'
                or '-'
                or '_';
    }
}
