using System.Security.Cryptography;
using Blog.Application.Interfaces;

namespace Blog.Infrastructure.Security
{
    /// <summary>
    /// PBKDF2 密码哈希（T3 决策：用框架内置实现，不引入三方包）。
    ///
    /// 存储格式（自描述，便于将来提升迭代次数后仍能校验旧密码）：
    ///     pbkdf2-sha512${iterations}${base64(salt)}${base64(hash)}
    ///
    /// 安全要点（见 docs/08-开发知识点.md §16.4）：
    ///   - 每用户独立随机盐（16 字节）
    ///   - 迭代次数可调；登录成功时若发现参数过旧可静默升级（NeedsRehash）
    ///   - 校验用 FixedTimeEquals，防时序攻击
    ///   - 绝不使用 MD5/SHA1/SHA256 直接哈希密码
    /// </summary>
    public sealed class Pbkdf2PasswordHasher : IPasswordHasher
    {
        private const string Prefix = "pbkdf2-sha512";
        private const int DefaultIterations = 210_000; // OWASP 对 PBKDF2-HMAC-SHA512 的建议量级
        private const int SaltBytes = 16;
        private const int HashBytes = 32;

        private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA512;

        private readonly int _iterations;

        public Pbkdf2PasswordHasher(int iterations = DefaultIterations)
        {
            if (iterations < 1)
                throw new ArgumentOutOfRangeException(nameof(iterations), "迭代次数必须 >= 1");
            _iterations = iterations;
        }

        public string Hash(string password)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(password);

            var salt = RandomNumberGenerator.GetBytes(SaltBytes);
            var hash = Derive(password, salt, _iterations);
            return $"{Prefix}${_iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public bool Verify(string password, string storedHash)
        {
            if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(storedHash))
                return false;

            // 解析失败一律返回 false（不抛异常），避免把格式细节暴露成可用信号
            var parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != Prefix) return false;
            if (!int.TryParse(parts[1], out var iterations) || iterations < 1) return false;

            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expected = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            // 按存储里记录的迭代次数重算，因此提高默认迭代次数不会让旧密码失效
            var actual = Derive(password, salt, iterations, expected.Length);

            // 恒定时间比较，防时序攻击
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        public bool NeedsRehash(string storedHash)
        {
            if (string.IsNullOrWhiteSpace(storedHash)) return true;

            var parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != Prefix) return true;
            if (!int.TryParse(parts[1], out var iterations)) return true;

            return iterations < _iterations;
        }

        private static byte[] Derive(string password, byte[] salt, int iterations, int outputBytes = HashBytes) =>
            Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, Algorithm, outputBytes);
    }
}
