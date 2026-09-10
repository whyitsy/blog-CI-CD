# 博客 Stage2 项目总览

## 项目状态
- 技术栈：Vue3 + TS + Vite + Pinia 前端；C#/.NET 10 + EF Core + PostgreSQL + Redis 后端
- 当前：后端运行中（http://localhost:5131），前端 dev server 运行中（http://localhost:5173），Docker 容器运行中
- 代码已覆盖：后端完整实现 + 前端公开站点 6 个页面 + 管理端 7 个路由（独立 AdminLayout）

## 已完成内容

### 后端
1. **乐观锁**：`int Version` + SQL `WHERE Version=@v` 手动控制，跨数据库通用
2. **Application 服务**：Post/Category/Tag/Site/Author 服务 + 统一响应/异常/分页
3. **缓存**：Memory/Redis 可切换；防穿透/击穿/雪崩；Redis 故障降级
4. **WebApi**：6 个控制器 + 全局异常处理 + 文件接口 + 限流
5. **Serilog**：控制台 + 滚动日志 + 慢查询拦截
6. **限流**：Redis Lua 令牌桶 + 内存降级 + 配置化规则

### 前端（公开站点）
1. 首页 Hero、文章卡片列表、分页、Footer 统计
2. 标签墙 /tags、分类墙 /categories
3. 归档时间轴 /archive
4. 文章详情 /post/:id（Markdown 渲染、目录、阅读进度、giscus 评论）
5. 通用列表 /posts（支持 tag/category/keyword 筛选）
6. 搜索弹窗 + 导航栏 + 明暗主题切换

### 前端（管理端，2026-09-10 新增）
1. 独立 `AdminLayout`：左侧侧边栏 + 顶栏模块名，≤880px 折叠为横向 tab，**不套公开 NavBar/Footer**
2. 文章管理（含骨架屏、分页、窄屏卡片式折叠、发布/下架/软删除）
3. 新建 / 编辑文章（`PostEditor`，含内联新建分类与标签）
4. 分类管理 / 标签管理（`TaxonomyManager` 共用，含行内改名、乐观锁、软删除）
5. 用户资料：编辑 name/email/avatar/bio，头像支持上传，保存走 `PUT /api/authors/{id}`
6. 网站配置：基本配置（站点名/首屏副标题/背景图/建站日期，逐项乐观锁）+ 社交链接增删改与显示隐藏

## 构建验证
- 后端：`dotnet build` 0 警告 0 错误
- 前端：`npx vue-tsc -b --force` 0 错误 + `npx vite build` 通过
- UI：Chrome headless 逐页截图（1440px / 800px 两档）核对布局

## 环境备忘
- 后端：`cd Blog.Backend/Blog.WebApi/bin/Debug/net10.0 && ./Blog.WebApi.exe --urls http://localhost:5131 --environment Development`
  （WSL 下 `ASPNETCORE_ENVIRONMENT` 环境变量不生效，必须用 `--environment` 参数）
- 前端：`"/mnt/c/Program Files/nodejs/node.exe" ./node_modules/vite/bin/vite.js --host 127.0.0.1 --port 5173`
  （WSL 的 node 缺 rolldown 原生绑定，必须用 Windows node）
- 后端进程名是 `Blog.WebApi.exe`，杀进程用 `/c/Windows/System32/taskkill.exe /F /IM Blog.WebApi.exe`
- WSL 无法直连 localhost，接口验证/截图走 Windows 工具：
  `/mnt/c/Windows/System32/curl.exe -s --noproxy '*' http://localhost:5131/api/...`
- dotnet 用 `/mnt/c/Program Files/dotnet/dotnet.exe`

## 已知限制
- **无认证**：管理端写接口全部按 anonymous 放行，后续需补 JWT + 路由守卫
- 前端表单「点击保存」链路未做浏览器自动化验证（后端语义已用 curl 逐条验证）
- giscus 评论依赖 GitHub Discussions，本地网络无法加载属正常
- 种子作者头像 `/media/avatar-default.png` 已不由后端提供（文件接口为 `/api/files/**`），
  前端已用 `@error` 回退到首字母占位，如需真实默认头像应更新种子数据或通过用户资料页上传
- 搜索弹窗交互仍未在 headless 中点击验证，建议手动测试
