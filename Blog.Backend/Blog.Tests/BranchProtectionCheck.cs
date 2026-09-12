namespace Blog.Tests;

/// <summary>
/// ⚠️ 这是一个**故意失败**的测试，唯一用途是验证「分支保护」是否真的生效。
///
/// 它属于分支 <c>test/branch-protection-verify</c>，**不要合并到 main**。
/// 验证流程：
///   1. 在 GitHub 上开启分支保护（Require a pull request + Require status checks）
///   2. 用本分支开一个 PR
///   3. 等 CI 跑完 —— 它必须是**红色**的
///   4. 确认「Merge」按钮**被禁用**，并提示 required status checks 未通过
///   5. 关闭该 PR 并删除本分支
///
/// 如果第 4 步的按钮**仍然可以点**，说明分支保护没有真正生效。
/// 设计依据见 docs/11-CI-CD与自动化交付.md §3.2 铁律二、§9.5 第 6 条。
/// </summary>
public class BranchProtectionCheck
{
    [Fact]
    public void 这是一个故意的失败_用于验证分支保护是否生效()
    {
        Assert.True(false,
            "这是分支保护验证用的**故意失败**。看到这条消息说明 CI 正确地变红了 —— " +
            "接下来请确认 PR 的合并按钮是否被禁用。不要合并这个 PR。");
    }
}
