// ============================================================================
// E2E 检查的配置：**全部来自环境变量**，没有任何硬编码的机器相关路径。
//
// 为什么要单独抽这个文件（对应 docs/06-技术债与待办.md §2 的 G2）：
//
//   搬进仓库之前的脚本里，这些东西是**写死**的：
//     · CDP 地址        http://127.0.0.1:9222
//     · 前端地址        http://localhost:5173
//     · 截图输出目录    D:/tmpbuild/shots/
//     · 账号密码        admin@example.com / Admin@12345
//     · 数据标题        「C# 进阶系列」「更新标题-标签同步验证」
//
//   写死的后果不是"不好看"，而是**搬进仓库等于没搬** ——
//   换一台机器、换一个端口、换一套种子数据，整套检查就全红，
//   然后你会得出"这些脚本没用"的结论，把它们删掉。
//
//   （这正是 docs/06-技术债与待办.md §2 那条教训的另一种形态：
//     **开发机上的文件不是项目资产** → **开发机上的常量也不是项目配置**。）
//
// 所有值都可以用环境变量覆盖，默认值只服务于「本机开发环境」这一种最常见的场景。
// ============================================================================

import { fileURLToPath } from 'node:url'

const env = process.env

/** 读取一个环境变量，空字符串视为"没设置" */
function str(name, fallback) {
  const v = env[name]
  return v === undefined || v === '' ? fallback : v
}

function int(name, fallback) {
  const v = env[name]
  if (v === undefined || v === '') return fallback
  const n = Number.parseInt(v, 10)
  if (Number.isNaN(n)) throw new Error(`环境变量 ${name} 不是合法整数：${v}`)
  return n
}

function bool(name, fallback) {
  const v = env[name]
  if (v === undefined || v === '') return fallback
  return !['0', 'false', 'no', 'off'].includes(v.toLowerCase())
}

export const config = {
  /** 前端地址。开发时是 Vite（5173）；跑容器整栈时应改成 http://localhost:8080 */
  baseUrl: str('E2E_BASE_URL', 'http://localhost:5173'),

  /** Chrome 的调试端口地址 */
  cdpUrl: str('E2E_CDP_URL', 'http://127.0.0.1:9222'),

  /**
   * 开发种子账号。
   * 默认值与 Blog.Infrastructure/Persistence/BlogDbContext.cs 里播种的账号一致
   * （该文件第 42 行也写明了「初始管理员密码（开发用）」）。
   * 换成别的环境（比如整栈容器）时用环境变量覆盖，不要去改代码。
   */
  admin: {
    email: str('E2E_ADMIN_EMAIL', 'admin@example.com'),
    password: str('E2E_ADMIN_PASSWORD', 'Admin@12345'),
  },
  author: {
    email: str('E2E_AUTHOR_EMAIL', 'writer@example.com'),
    password: str('E2E_AUTHOR_PASSWORD', 'Writer@12345'),
  },

  /**
   * ⚠️ **只有一个登录端点**，管理员和作者共用：
   *
   *     AuthController: [Route("api/auth")] + [HttpPost("login")]
   *     // 登录。不限定角色，Admin 与 Author 共用（登录入口不承担权限边界，
   *     // 权限由 AdminOnly / ContentWriter 授权策略在具体接口上强制）
   *
   * 搬进仓库前的旧脚本调的是 `/api/auth/admin/login` 与 `/api/auth/author/login` ——
   * **这两个地址早就不存在了**（返回 404，响应体为空，于是 `res.json()` 直接抛
   * "Unexpected end of JSON input"）。它们是更早的 API 版本留下的。
   *
   * 这就是"脚本放在仓库外"的代价之一：**没人会去改它们**，
   * 所以它们会安静地烂掉，而你还以为它们能跑。（archive/问题排查记录.md §9）
   */
  loginPath: str('E2E_LOGIN_PATH', '/api/auth/login'),

  /**
   * 前端在 localStorage 里用的键名。
   *
   * 这三个名字原本散落在每个脚本的字符串里（`'blog-auth-token'` 出现了几十次）。
   * 一旦前端改了键名，那种写法会让你改几十处、而且漏一处只有运行时才发现。
   * 放在这里 = 单一来源。
   */
  storageKeys: {
    token: 'blog-auth-token',
    user: 'blog-auth-user',
    expires: 'blog-auth-expires',
  },

  /** 单步等待上限。原脚本用的是固定 sleep（2500~3500ms），
   * 这里改成「轮询直到条件成立，超时才失败」——
   * 既更快（条件早成立就早继续），也更不容易因为机器慢而假红。 */
  timeoutMs: int('E2E_TIMEOUT_MS', 15000),

  /**
   * 截图目录（相对于本目录）。已在 .gitignore 里，属于运行产物，不进版本控制。
   *
   * ⚠️ 必须用 fileURLToPath 而不是 URL.pathname：
   *    仓库路径里有中文（`dotNET项目`），pathname 会给出百分号转义后的
   *    `/mnt/d/dotNET%E9%A1%B9%E7%9B%AE/...`，mkdir 出来就是一个乱码目录名。
   */
  artifactsDir: str('E2E_ARTIFACTS_DIR', fileURLToPath(new URL('../artifacts/', import.meta.url))),

  /** 默认是否截图。截图很便宜，而且是"我确实跑过"的证据，所以默认开 */
  shots: bool('E2E_SHOTS', true),

  /** 视口尺寸。固定视口是为了让截图可比、CSS 断点稳定 */
  viewport: { width: int('E2E_VIEWPORT_WIDTH', 1440), height: int('E2E_VIEWPORT_HEIGHT', 1100) },
}

/** 检查 Node 版本：CDP 客户端依赖 Node 22+ 内置的全局 WebSocket，不需要任何 npm 依赖 */
export function assertNodeVersion() {
  const major = Number.parseInt(process.versions.node.split('.')[0], 10)
  if (major < 22) {
    throw new Error(
      `需要 Node 22 或更高（当前 ${process.versions.node}）：` +
        `脚本用的是内置全局 WebSocket，因此不需要 npm install。`,
    )
  }
}

/**
 * 生成一个「本次运行唯一」的后缀。
 *
 * ⚠️ 这是把脚本搬进仓库时**必须补上**的东西，原因见 tools/e2e/README.md 的「可重复运行」一节：
 * 原脚本里创建的账号/作者/文章标题是**写死的**（`uitest@example.com`、`UI 测试作者`），
 * 于是**第二次运行必然失败**（邮箱已被占用），而失败原因与代码质量无关。
 *
 * 一套"只能跑一次"的检查比没有检查更糟 —— 它会训练你忽略红色。
 */
export function runId() {
  const now = new Date()
  const pad = (n, w = 2) => String(n).padStart(w, '0')
  // 形如 0914-0157-23；同一天内多次运行也不会撞
  return `${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}-${pad(now.getSeconds())}`
}
