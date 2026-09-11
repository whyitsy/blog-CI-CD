// 测试工程的显式入口点。
//
// 为什么需要它：xUnit v3 的测试工程需要一个 Main（它的运行器会用到），
// 但 SDK 自动生成的那个 Program 会**与 Blog.WebApi 顶层语句生成的 Program 同名**，
// 导致 WebApplicationFactory<Program> 解析到错误的类型
// （报 "The server has not been started or no web application was configured"）。
// 因此这里提供自己的入口点，并在 csproj 里设置 <GenerateProgramFile>false</GenerateProgramFile>
// 关掉自动生成，避免任何重名。
//
// 入口点本身不做任何事——测试由 xUnit 运行器执行。
internal static class TestEntryPoint
{
    public static void Main() { }
}
