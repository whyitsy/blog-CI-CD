# 多作者技术博客平台

> 前后端分离的多作者博客系统：**Vue 3 + TypeScript** 前端 ｜ **ASP.NET Core 10** 后端 ｜ **PostgreSQL 18** ｜ **Redis 7**。
> 从架构设计、编码、自动化测试，到 CI/CD、容器化部署与 HTTPS 上线，独立完成全流程。

**🌐 线上站点**：<https://www.kkynet.site>
**✅ CI 状态**：见 [GitHub Actions](https://github.com/whyitsy/kky-blog/actions) —— 格式卫生 / 前端类型检查与构建 / 后端编译测试覆盖率 / 依赖漏洞扫描，四个作业

---

## 这是什么

一个**多作者**技术博客平台。它刻意不是「单人博客」——这个定位带来了一连串连锁需求：
文章归属需要越权校验、草稿需要权限保护、作者需要可增删改、内容需要专栏来组织。

| 能力 | 说明 |
|---|---|
| 内容载体 | Markdown 文章，前端渲染 + DOMPurify 消毒 |
| 内容组织 | 分类（单值）+ 标签（多值）+ **专栏**（多对多系列，带专栏内人为排序） |
| 账号体系 | `User`（登录账号）与 `Author`（内容署名）**分离**，支持没有账号的挂名作者 |
| 检索 | **中文全文检索**：zhparser 分词 + `tsvector` 生成列 + GIN 索引 + `ts_rank` 排序 |
| 图片 | 上传自动转 WebP 并压缩，带像素上限（防解压炸弹） |
| 权限 | 三级：匿名访客 / Author（作者）/ Admin（管理员），两个授权策略 `ContentWriter`、`AdminOnly` |
| 评论 | 外挂 giscus（GitHub Discussions），**本站不存评论数据** |

### 明确不做的

单人博客的简化前提（已被多作者定位作废）、细粒度 RBAC、
API 版本管理、站内评论系统。原因见 [`docs/06`](./docs/06-技术债与待办.md) 与 [`archive/决策记录.md`](./archive/决策记录.md)。

---

## 技术栈

| 层 | 技术 |
|---|---|
| 后端 | .NET 10 · ASP.NET Core · EF Core 10 · Npgsql · Serilog · ImageSharp · JWT |
| 数据库 | PostgreSQL 18（+ zhparser 中文分词扩展，自建镜像） |
| 缓存 / 限流 | Redis 7（`StackExchange.Redis`，令牌桶用 **Lua 脚本**保证原子性），无 Redis 时降级为内存实现 |
| 前端 | Vue 3 · TypeScript · Vite · Pinia · Vue Router · marked · DOMPurify · Sass |
| 测试 | xUnit · Moq · `WebApplicationFactory` 集成测试 · coverlet 覆盖率 |
| 交付 | Docker Compose · Nginx · Caddy（宿主机终结 TLS）· GitHub Actions · Dependabot |

---

## 架构与分层

```
Blog.Backend/
├─ Blog.Domain/          实体 + 仓储接口（零框架依赖）
├─ Blog.Application/     用例编排 + DTO + 横切接口
├─ Blog.Infrastructure/  EF Core / 缓存 / 限流 / 文件 / 安全 的实现
├─ Blog.WebApi/          HTTP 入口：Controllers · Middleware · 组合根
└─ Blog.Tests/           xUnit 单元测试 + 集成测试
Blog.FrontEnd/           Vue 3 SPA
deploy/                  Dockerfile · nginx.conf · 服务器初始化脚本
tools/                   e2e 检查 · k6 压测物料 · 备份还原
docs/ learn/ archive/    参考手册 / 教材 / 过程留档
```

**依赖方向单向，不可违反**：

```
Blog.WebApi ──► Blog.Application ──► Blog.Domain
     │                 ▲                  ▲
     └──► Blog.Infrastructure ────────────┘
```

`Blog.Domain` 的「零框架依赖」是最容易被破坏、也最值得守住的一条约束——
连 Npgsql 的类型都不允许出现在领域实体上（全文检索向量因此声明为 EF 的影子属性）。

完整说明见 [`docs/02-架构与数据模型.md`](./docs/02-架构与数据模型.md)。

---

## 快速开始

```bash
# 1. 准备环境变量（JWT 签名密钥与数据库密码不入库，必须注入）
cp .env.example .env
openssl rand -base64 48      # 填进 .env 的 JWT_SIGNING_KEY

# 2. 起整栈（nginx / webapi / pgsql / redis 四个服务）
docker compose up -d --build
docker compose ps            # 期望四个都 healthy
curl -i http://localhost:8080/health
```

⚠️ **首次启动后请立刻改掉种子管理员密码**（`admin@example.com` 的初始口令写在
`BlogDbContext.cs` 的种子注释里，是公开值）。生产配置要求见
[`docs/05` §8.3](./docs/05-运维与部署手册.md)。

---

## 文档导航

本项目把文档按**用途**分成三区，放错区就是未来的矛盾源：

| 区 | 用途 | 从这里开始 |
|---|---|---|
| [`docs/`](./docs/README.md) | **参考手册**：描述当前代码事实（不符合即为缺陷） | [快速开始](./docs/01-快速开始.md) · [架构](./docs/02-架构与数据模型.md) · [API](./docs/03-API参考.md) · [前端](./docs/04-前端设计.md) · [运维与部署](./docs/05-运维与部署手册.md) · [技术债](./docs/06-技术债与待办.md) |
| [`learn/`](./learn/README.md) | **教材**：讲原理与为什么、学习路线图 | [后端知识地图](./learn/01-后端知识地图.md) · [CI/CD 实践](./learn/02-CI-CD实践.md) · [性能与运行时诊断](./learn/03-性能与运行时诊断.md) |
| [`archive/`](./archive/) | **过程留档**：决策当时的理由、踩坑的完整定位过程 | [决策记录](./archive/决策记录.md) · [问题排查记录](./archive/问题排查记录.md) |

写作规范（两条铁律 + 三区硬规则）见 [`docs/00-文档规范.md`](./docs/00-文档规范.md)。

---

## 几条值得一提的工程实践

> 这些不是「设计得很漂亮」，而是**真的踩到、并且留下了证据**的问题。

- **限流被完全绕过**：部署后实测发现伪造 `X-Forwarded-For` 可让限流一次都不触发。
  根因是沿用了 Nginx 文档里的常见范例 `$proxy_add_x_forwarded_for`——它把**客户端自己发的头保留在最前面**，
  而应用取的是第一段。修复：改用 `$remote_addr` **覆盖**。（[`docs/05` §8.6](./docs/05-运维与部署手册.md)）
- **同一个缺陷换了一条路径回来**：上 HTTPS 时在宿主机加了一层 TLS 终结代理，
  上面那个「覆盖」写法于是把**所有访客写成同一个 IP**，按 IP 限流退化成全站共享一个令牌桶。
  改用 `realip` 按**对端网段**判断可信与否，并用四组对照实验验证——包括
  「绕过代理直连并伪造 XFF」这组，确认不可信对端的头会被忽略。（[`docs/05` §8.8.1](./docs/05-运维与部署手册.md)）
- **「检查看起来在工作」比「没有检查」更危险**：CI 的依赖安全扫描曾因镜像源未实现 audit 接口
  而**非 0 退出被 `continue-on-error` 吃掉**，于是「审计没跑成」和「审计跑了没发现漏洞」在页面上**长得一模一样**。
  同类问题共查出三处，全部补上**失效哨兵**（必须看到预期输出才算通过）。
  （[`archive/问题排查记录.md` §8](./archive/问题排查记录.md)）
- **防假绿**：CI 解析 trx 报告，**执行 0 个测试或存在失败一律令流水线变红**，
  并把测试数与覆盖率写进作业摘要——因为「测试没被发现」和「测试全过」都表现为绿色。
- **先说测量，再说优化**：压测物料（造数据脚本 + k6 脚本 + 本地基线）都在 [`tools/load/`](./tools/load/)，
  SLO 与结论沉淀在 [`learn/03`](./learn/03-性能与运行时诊断.md)。

---

## 开发约定

- **提交信息**：`类型(范围): 中文描述`，类型取 `feat` / `fix` / `docs` / `refactor` / `perf` / `test` / `chore`，`!` 表示破坏性变更
- **行尾统一 LF**（见 `.gitattributes`）；编辑时不要整文件覆盖写回 CRLF
- **密钥绝不入库**：`Jwt:SigningKey` 与数据库连接串必须由环境变量注入，缺失时应用**拒绝启动**
- **文档纪律**：`docs/` 与代码不符即为缺陷；变更历史属于 commit message，不属于文档
