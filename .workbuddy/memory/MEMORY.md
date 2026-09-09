# 博客项目（Stage2）长期记忆

## 项目结构
- 技术栈：Vue3 + TS + Vite 前端；C#/.NET 10 + ASP.NET Core + EF Core + PostgreSQL 后端（无 AutoMapper，清洁架构）
- 目录：`Blog.Backend/`（Domain/Application/Infrastructure/WebApi）、`Blog.FrontEnd/`、`docs/`（5 份文档 + diagram）

## 关键架构决策（勿改动）
1. **乐观锁 = int Version + SQL 条件手动控制**（用户明确要求，跨库通用）：
   - `BaseEntity.Version`(int,默认1)；仓储层 `ApplyOptimisticVersion` 生成 `UPDATE ... SET Version=@v+1 WHERE Id=@id AND Version=@v`；0 行 → `DbUpdateConcurrencyException` → 中间件转 409(code 4090)
   - 浏览量累计用 `ExecuteUpdate` 绕过乐观锁
2. **缓存**：`Cache:Enabled` 开关 + `Provider: Memory/Redis`；穿透=空值哨兵短TTL、击穿=key级互斥重建、雪崩=TTL±20%抖动；Redis 故障自动降级
3. **限流**：令牌桶（Redis Lua + 内存降级），规则在 appsettings `RateLimit` 节配置化，触发 429 + Retry-After
4. **Serilog**：控制台 + `logs/blog-.log` 按天滚动；EF 慢查询>500ms 拦截

## 环境备忘（重要）
- **沙箱 Bash 缺 Windows 环境变量** → dotnet 构建必须用 `/d/tmpbuild/dn.sh` 包装（否则 `ArgumentNullException path1`）
- **数据库**：Docker 容器 `pgsql`（用户 kky / 密码 123456），本阶段库 `blog_stage2`（旧库 blog 勿动）；另有 redis 容器
- **接口验证**：只能用 Git Bash `curl --noproxy localhost,127.0.0.1`；PowerShell 的 localhost 会被 mock 层拦截返回假数据
- **后端进程名是 `Blog.WebApi`**（非 dotnet），杀进程用 `Get-Process -Name Blog.WebApi`
- 后端地址 http://localhost:5080；前端 dev http://localhost:5173（vite proxy `/api`→5080）

## 启动命令
- 后端持久运行（推荐）：在 Bash 工具里用 `run_in_background=true` 执行：
  ```bash
  cd /d/dotNET项目/Stage2/Blog.Backend/Blog.WebApi/bin/Debug/net10.0 && env -u HTTP_PROXY -u http_proxy -u HTTPS_PROXY -u https_proxy ASPNETCORE_ENVIRONMENT=Development ./Blog.WebApi.exe --urls http://localhost:5080
  ```
  注意：不要加 `&` 或 `nohup`，让 Bash 工具的 `run_in_background` 保持进程；带 `&` 的进程会在 Bash 会话结束后被系统杀掉，导致 Vite 代理 `ECONNREFUSED`。
- 前端：`cd Blog.FrontEnd && npm run dev`（先 `npm run build` 用 vue-tsc 校验）
- 先确保 Docker Desktop 已启动且 pgsql/redis 容器 running，再启动后端
- 杀后端进程：`/c/Windows/system32/taskkill.exe /F /IM Blog.WebApi.exe`（Git Bash 的 `taskkill` 语法可能与 Windows 不同，用完整路径更稳）

## 当前进度（截至 2026-09-09）
- 后端：**已全部完成并冒烟测试通过**（6 次 git commit），20+ 接口全过
- 前端：**页面已全部创建并 build 通过**（1 次 commit，branch main，作者 kky/kky@example.com）
- 前端组件：Hero/NavBar/Footer/PostCard/PostCardList/PaginationBar/SearchModal/GiscusComments/SocialIcon + 6 个 view
- **待办**：完成前后端联调验证（此前 agent-browser 装 Chromium 触发 429 被阻断）
- 用户最新指示：不再严格按 `参考设计规范` 打磨视觉，**只保证基础功能跑通即可**

## 前端关键文件
- `src/api/http.ts`：fetch 封装，解包 ApiResponse，code≠0 throw ApiError；BASE = `VITE_API_BASE ?? ''`（走 vite proxy）
- `src/types/index.ts`：与后端 DTO 对齐的 TS 类型
- `src/stores/site.ts` / `app.ts`；`src/router/index.ts`（createWebHistory）
