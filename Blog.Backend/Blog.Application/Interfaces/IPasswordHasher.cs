namespace Blog.Application.Interfaces
{
    /// <summary>
    /// 密码哈希抽象。
    /// 实现必须是「慢哈希」（PBKDF2 / Argon2id / BCrypt），
    /// 绝不能用 MD5/SHA1/SHA256 直接哈希（见 learn/01-后端知识地图.md §9.2）。
    /// </summary>
    public interface IPasswordHasher
    {
        /// <summary>生成哈希（内含随机盐）。返回自描述格式，供 Verify 解析参数。</summary>
        string Hash(string password);

        /// <summary>
        /// 校验密码。必须使用恒定时间比较，防时序攻击。
        /// 格式非法或参数无法解析时返回 false（不抛异常，避免把内部细节暴露给调用方）。
        /// </summary>
        bool Verify(string password, string storedHash);

        /// <summary>
        /// 判断某个已存哈希是否需要用当前参数重新哈希
        /// （用于将来提高迭代次数后，在用户登录成功时悄悄升级）。
        /// </summary>
        bool NeedsRehash(string storedHash);
    }
}
