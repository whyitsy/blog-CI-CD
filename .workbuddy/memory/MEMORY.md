# 博客项目（Stage2）长期记忆

## 项目结构
- 技术栈：Vue3 + TS + Vite 前端；C#/.NET 10 + ASP.NET Core + EF Core + PostgreSQL 后端（无 AutoMapper，清洁架构）
- 目录：`Blog.Backend/`（Domain/Application/Infrastructure/WebApi）、`Blog.FrontEnd/`、`docs/`
- **文档单一事实来源（2026-09-10 重构后）**，全部位于 `docs/`：
  - `tech.md` — 技术决策与知识点（部署三点详解、JWT/密码、缓存 key 规范、pg_trgm 搜索、SEO 路径、提交规范、待确认 T1–T10）
  - `business.md` — 业务文档（多作者定位、User/Author 分离、双登录入口、专栏、权限矩阵、生命周期、P0/P1/P2）
  - `backend.md` — 后端技术文档（版本号、架构、7+3 表、缓存、pg_trgm 搜索、JWT、27+ 端点、错误码、技术债）
  - `frontend.md` — 前端技术文档（.env 分环境、路由含作者工作区、骨架屏规划、分类样式待补、技术债）
  - `suggestion.md` — 待确认清单（T1–T10）+ 已决策索引（Q1–Q14 / E1–E16）
  - 旧文档 `docs/01`–`05` 与 `docs/diagram/*.svg` **已删除**（16 处与实现不一致），可从 git 历史取回
  - 原始需求 `补充具体说明.md` 在仓库根目录，保留
- **项目定位已改为「多作者博客」**（Q3）：`Author` 是内容属性（与 Post/Tag/Category 同层，无凭据）；
  `User` 是账号（Email/PasswordHash/Role）。两套登录入口：前台 `/login`（作者）、后台 `/admin/login`（管理员）
- 前端样式是**自研 CSS 组件体系**（`styles/tokens.css` + `global.css` + scoped style），**未使用 Naive UI**，后续保持一致
- 用户指示：不必严格按 `参考设计规范` 打磨视觉，**保证基础功能跑通优先**

## ⚠️ 操作纪律（血泪教训）
- **提交前必须 `git show --stat HEAD` 复核**：本项目历史上多次发生「无关文件被误删/误加并混入提交」：
  1. `3419a54` 混入 39 个 CRLF 行尾噪声文件（后经 `67e27a8` 重做修正，已加 `.gitattributes` 统一 LF）
  2. `d7ad2f6` 混入 `docs/diagram/*.svg` 与 `补充具体说明.md` 的删除（已 amend 为 `01ae2b5` 修正并恢复文件）
  3. 同一会话内 `补充具体说明.md` **被外部进程删除 3 次**（原因未定位，疑似 Windows 侧同步/清理）；
     已恢复。**建议频敏提交**，未跟踪文件丢失不可恢复
- 使用 `git add -A` / `git add .` 前先 `git status --short` 看清范围；**优先按路径精确 add**
- 改动前用 `git ls-files <path>` 确认文件是「已跟踪」还是「计划新增」，避免误判为删除
- 本仓库目录名含非 ASCII（`dotNET项目`），文档正文中**不要写仓库绝对路径**，只写仓库内相对路径


## 关键架构决策（勿改动）
1. **乐观锁 = int Version + SQL 条件手动控制**（用户明确要求，跨库通用）：
   - `BaseEntity.Version`(int,默认1)；仓储层 `ApplyOptimisticVersion` 生成 `UPDATE ... SET Version=@v+1 WHERE Id=@id AND Version=@v`；
     0 行 → `DbUpdateConcurrencyException` → 中间件转 409(code 4090)
   - 浏览量累计用 `ExecuteUpdate` 绕过乐观锁
   - 写入语义统一：**Key/实体不存在=新增（忽略 version）/ 已存在=乐观锁（version 必须 >=1，否则 4001）**
2. **缓存**：`Cache:Enabled` 开关 + `Provider: Memory/Redis`；穿透=空值哨兵短TTL、击穿=key级互斥重建、雪崩=TTL±20%抖动；Redis 故障自动降级
3. **限流**：令牌桶（Redis Lua + 内存降级），规则在 appsettings `RateLimit` 节配置化，触发 429 + Retry-After
4. **Serilog**：控制台 + `logs/blog-.log` 按天滚动；EF 慢查询>500ms 拦截
5. **前端布局 = 两个父路由**：公开站点挂 `layouts/DefaultLayout.vue`，管理端挂 `layouts/AdminLayout.vue`；
   `App.vue` 只有顶层 `RouterView`。管理端**不套**公开 NavBar/Footer（NavBar 是 fixed，混用会遮挡页面内容）

## 环境备忘（重要）
- **行尾统一 LF**：仓库根已加 `.gitattributes`（`* text=auto eol=lf` + 图片二进制）。
  编辑文件时请保持 LF，**不要整文件覆盖写回 CRLF**，否则会产生整文件 diff。
  检查：`git ls-files --eol | grep w/crlf`（正常应为空）。
- **WSL 直连 localhost 不通**（WSL2 localhost 转发未生效）：必须用 Windows curl 验证接口
  `/mnt/c/Windows/System32/curl.exe -s --noproxy '*' http://localhost:5131/api/...`（`--noproxy` 与 `*` 要分成两个参数）
- **dotnet 不在 PATH**：用 `/mnt/c/Program Files/dotnet/dotnet.exe`
- **数据库**：Docker 容器 `pgsql`（用户 kky / 密码 123456），本阶段库 `blog_stage2`（旧库 blog 勿动）；另有 redis 容器
- **端口**：后端 http://localhost:5131（launchSettings `http` profile）；前端 dev http://localhost:5173（vite proxy `/api`→5131）
- **构建前先杀后端**：`dotnet build` 会因 Blog.WebApi.exe 占用 dll 报 MSB3027/MSB3021，
  先 `/c/Windows/System32/taskkill.exe /F /IM Blog.WebApi.exe`

## 启动命令
- 后端持久运行：在 Bash 工具里用 `run_in_background=true` 执行（不要加 `&` / `nohup`，否则会话结束进程被杀导致 Vite 代理 ECONNREFUSED）：
  ```bash
  cd /d/dotNET项目/Stage2/Blog.Backend/Blog.WebApi/bin/Debug/net10.0 && \
  env -u HTTP_PROXY -u http_proxy -u HTTPS_PROXY -u https_proxy \
  ./Blog.WebApi.exe --urls http://localhost:5131 --environment Development
  ```
  **注意**：用 WSL 启动 Windows exe 时 `ASPNETCORE_ENVIRONMENT` 不生效（会因连接串为空启动失败），必须用 `--environment Development`。
- 前端 dev（**必须用 Windows 的 node**，WSL node 缺 rolldown 原生绑定 `Cannot find native binding`）：
  ```bash
  cd Blog.FrontEnd && "/mnt/c/Program Files/nodejs/node.exe" ./node_modules/vite/bin/vite.js --host 127.0.0.1 --port 5173
  ```
- 类型检查 / 构建：`cmd.exe /c "npx vue-tsc -b --force"`、`cmd.exe /c "npx vite build"`
- 先确保 Docker Desktop 已启动且 pgsql/redis 容器 running，再启动后端
- **Chrome headless 截图**（Windows 侧运行，路径用 `D:\...`；先 `taskkill /F /IM chrome.exe` 清残留，否则可能挂住）：
  ```bash
  chrome.exe --headless=new --disable-gpu --hide-scrollbars --window-size=1440,1000 \
    --screenshot="D:\tmpbuild\shots\x.png" --virtual-time-budget=8000 "http://localhost:5173/admin"
  ```
  Chrome 会缓存，核对改动后请加 `?cb=<timestamp>` 或使用独立 `--disk-cache-dir`

## 当前进度（截至 2026-09-10）
- 后端：全部完成。本轮新增：作者资料更新 `PUT /api/authors/{id}`、社交链接 `?includeHidden=true`、
  社交链接删除 `DELETE /api/site/social-links/{id}`、`SiteConfigDto.Versions`（下发各配置项版本号）
- 前端：公开站点 6 个页面 + **管理端 7 个路由**（AdminLayout 侧边栏：文章/新建/编辑/分类/标签/用户资料/网站配置）
- 已修复：文章管理页被固定导航栏遮挡（改为独立 AdminLayout）；`PagedResult.totalCount` 应为 `total`
  （此前列表页与管理页「共 N 篇」显示 undefined）
- **待办**：认证与授权（当前全部接口按 anonymous 放行，用户已确认后续再补）；
  管理端表单「点击保存」链路的浏览器自动化验证（本轮只做了接口契约核对 + curl 语义验证）

## 前端关键文件
- `src/api/http.ts`：fetch 封装，解包 ApiResponse，code≠0 throw ApiError；BASE = `VITE_API_BASE ?? ''`（走 vite proxy）；
  FormData 不设 Content-Type；`upload()` 用于文件上传（`api/files.ts`）
- `src/types/index.ts`：与后端 DTO 对齐；**`PagedResult` 字段是 `items/total/page/pageSize/totalPages`**
- `src/stores/site.ts`：站点配置/社交链接/统计缓存于 store，管理端保存后调 `refreshAll()` 刷新全站
- `src/layouts/AdminLayout.vue`：管理端侧边栏布局（≤880px 折叠为横向 tab，用最长前缀匹配决定高亮与标题）
- `src/components/admin/TaxonomyManager.vue`：分类/标签共用的管理 UI
- `src/router/index.ts`：两个父路由（`/` → DefaultLayout，`/admin` → AdminLayout），子路由 name 保持与原有一致
