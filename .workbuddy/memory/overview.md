# 博客 Stage2 项目总览

## 项目状态
- 技术栈：Vue3 + TS + Vite + Pinia 前端；C#/.NET 10 + EF Core + PostgreSQL + Redis 后端
- 代码已覆盖：后端完整实现 + 前端 6 个页面 + 前后端联调
- 当前：后端运行中，前端 dev server 运行中，Docker 容器运行中

## 已完成内容

### 后端（全部完成）
1. **乐观锁**：`int Version` + SQL `WHERE Version=@v` 手动控制，跨数据库通用
2. **Application 服务**：Post/Category/Tag/Site/Author 服务 + 统一响应/异常/分页
3. **缓存**：Memory/Redis 可切换；防穿透/击穿/雪崩；Redis 故障降级
4. **WebApi**：6 个控制器 + 全局异常处理 + 文件接口 + 限流
5. **Serilog**：控制台 + 滚动日志 + 慢查询拦截
6. **限流**：Redis Lua 令牌桶 + 内存降级 + 配置化规则

### 前端（基础功能完成）
1. 首页 Hero、文章卡片列表、分页、Footer 统计
2. 标签墙 /tags、分类墙 /categories
3. 归档时间轴 /archive
4. 文章详情 /post/:id（Markdown 渲染、目录、阅读进度、评论占位）
5. 通用列表 /posts（支持 tag/category/keyword 筛选）
6. 搜索弹窗 + 导航栏

### 本轮新增（2026-09-09）
- 修复 `PostDetailDto` 缺少 `WordCount` 导致详情页阅读时间显示 `NaN`
- 启动 Docker / pgsql / redis / 后端 / 前端 dev server
- 通过 Chrome headless 完成 6 个页面渲染验证 + DOM/API 联调验证
- Git 提交：`29f5696 fix(backend): PostDetailDto 返回 WordCount...`

## 构建验证
- 后端：`dotnet build` 0 警告 0 错误
- 前端：`npm run build` 通过（vue-tsc + vite build）

## 环境备忘
- 后端启动：`cd Blog.Backend/Blog.WebApi/bin/Debug/net10.0 && ASPNETCORE_ENVIRONMENT=Development ./Blog.WebApi.exe --urls http://localhost:5080`
- 前端启动：`cd Blog.FrontEnd && npm run dev`
- 后端进程名是 `Blog.WebApi.exe`，杀进程用 `taskkill /F /IM Blog.WebApi.exe`（从 Git Bash 用 `/c/Windows/system32/taskkill.exe /F /IM`）
- 接口验证用 `curl --noproxy localhost,127.0.0.1`；PowerShell localhost 被 mock
- 沙箱 Bash `ProgramFiles` 为空，如 dotnet 构建报 `ArgumentNullException path1` 需用 `/d/tmpbuild/dn.sh` 包装

## 已知限制
- 搜索弹窗交互未在 headless 中点击验证，建议在浏览器中手动测试
- giscus 评论依赖 GitHub Discussions，本地网络无法加载属正常
- 当前测试数据 2 篇文章均只带 C# 标签、无分类，分类墙显示 0 篇
- 沙箱 dn.sh 包装脚本在当前环境下执行 `exec env ... dotnet` 时不产生输出，建议直接使用 `dotnet` 命令（本机环境变量已足够）
