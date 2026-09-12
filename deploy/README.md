# 部署相关产物

> 记录日期：2026-09-10 ｜ 相关说明见 [../docs/01-项目初始化与配置.md](../docs/01-项目初始化与配置.md) §6

本目录放**部署期**需要的产物（镜像定义、初始化脚本）。
它不属于任何 .NET 工程，因此不参与 `dotnet build`。

---

## 1. `postgres-zhparser.Dockerfile` —— 带中文分词的 PostgreSQL 镜像

### 为什么需要它

项目按 T9 决策使用 PostgreSQL **全文检索（FTS）** 做站内中文检索。
PostgreSQL 内置分词器只按空格/标点切分，对中文会把**整句当成一个词元**，
因此必须安装中文分词器 **`zhparser`**（依赖 **SCWS** 词法库）。

`zhparser` **不在官方 `postgres` 镜像中，必须自行编译**。
如果只在容器里手工编译，容器一重建就全丢，全文检索会直接报错：

```
ERROR:  text search configuration "chinese" does not exist
```

所以把编译步骤固化进镜像 —— 这是唯一可复现、可交接、CI 也能用的做法。

### 构建

```bash
# 在仓库根目录执行
docker build -f deploy/postgres-zhparser.Dockerfile -t blog-postgres-zhparser:18 .
```

构建产物：`blog-postgres-zhparser:18`（基于 `postgres:18.6`）。

### 镜像里有什么

| 内容 | 位置 |
|---|---|
| `zhparser.so` | `$(pg_config --pkglibdir)/zhparser.so` |
| `zhparser.control` + SQL 脚本 | `$(pg_config --sharedir)/extension/` |
| `libscws` | `/usr/local/lib/` |
| 初始化脚本 | `/docker-entrypoint-initdb.d/01-zhparser.sql` |

### 构建期踩到的坑（已在 Dockerfile 中处理）

| # | 坑 | 处理 |
|---|---|---|
| 1 | `libscws-dev` **不在 Debian 13 源**里 | SCWS 也必须从源码编译 |
| 2 | SCWS 仓库**没有 `autogen.sh`**，实际脚本名是 `acprep` | 用 `./acprep` |
| 3 | `acprep` 因 `Makefile.am` 里一行 **Tab 缩进的 `#` 注释**报 `'#' comment at start of rule is unportable` | `sed -i '/^[[:space:]]*#unison/d' Makefile.am` 先删掉 |
| 4 | `automake --warnings=no-portability` **无法抑制**上述错误 | 只能改源码 |
| 5 | `acprep` 需要 `autoconf automake libtool pkg-config` | 已装 |
| 6 | GitHub clone 偶发 TLS 中断 | 本文档记录；如需更稳可换 tarball 下载 |

---

## 2. `postgres-init/01-zhparser.sql` —— 全新环境的初始化

启用 `zhparser` 扩展，并创建中文检索配置 `chinese`（含权重所需的 token 类型映射）。

**运行时机**：postgres 官方镜像的 entrypoint 会在**数据目录为空**时执行它。
即「全新环境一次到位」，不需要开发者手工跑 SQL。

**幂等**：全部用 `CREATE EXTENSION IF NOT EXISTS` 与 `DO` 块判断，重复执行安全。

> ⚠️ **对已存在的数据库，这个脚本不会重跑。**
> 因此**扩展与检索配置的权威来源是 EF 迁移**（见 [../docs/06-数据库设计.md](../docs/06-数据库设计.md) §8.3）。
> 本脚本只是让全新环境开箱可用；迁移里必须也包含同样的语句，才能覆盖「已有库」的场景。

脚本末尾有一段自检，会在容器日志中打印分词结果，便于确认生效：

```
NOTICE:  zhparser 自检分词结果: 'core':3 'ef':2 '优化':6 '使用':1 '做':4 '全文检索':7 '数据库':5
```

> 日志里可能出现 `custom dict ... not loaded (missing or unreadable)` ——
> 这是 zhparser 在找**自定义词典**（用于加专业词汇），未提供时属正常，不影响分词。

---

## 3. 验证记录

已在一个**独立端口**的全新容器上验证镜像可用（未影响开发库）：

```bash
docker run -d --name pgsql-zh-test \
  -e POSTGRES_USER=kky -e POSTGRES_PASSWORD=123456 -e POSTGRES_DB=zh_test \
  -p 5433:5432 blog-postgres-zhparser:18
```

验证结果：

| 检查项 | 结果 |
|---|---|
| 初始化脚本自动执行 | ✅ 日志显示 `running /docker-entrypoint-initdb.d/01-zhparser.sql` |
| 扩展已安装 | ✅ `SELECT extname FROM pg_extension` 返回 `zhparser` |
| 检索配置已创建 | ✅ `SELECT cfgname FROM pg_ts_config` 返回 `chinese` |
| 中文分词正确 | ✅ `to_tsvector` 输出词级结果（非逐字、非整句） |
| 检索匹配正确 | ✅ `to_tsvector('chinese','数据库优化实践') @@ plainto_tsquery('chinese','数据库')` → `t` |

验证后已删除测试容器：`docker rm -f pgsql-zh-test`。

---

## 4. 切换开发环境的 PostgreSQL 容器（✅ 已执行）

> **状态：已完成。** 开发容器 `pgsql` 现运行 `blog-postgres-zhparser:18`，
> 数据卷沿用原卷，业务数据与扩展配置均已验证（见 §5）。
> 下方步骤保留作为**操作手册与灾难恢复参考**。

数据在**命名卷**里（而不是容器内），因此**替换容器不会丢数据**：

```bash
# 1) 先确认数据卷名（下面这条会打印卷名，形如 3aaacea5...）
docker inspect pgsql --format '{{range .Mounts}}{{.Name}}{{end}}'

# 2) 停掉旧容器但保留它（万一要回退）
docker stop pgsql && docker rename pgsql pgsql-old

# 3) 用自定义镜像启动新容器，挂同一个数据卷
docker run -d --name pgsql \
  -e POSTGRES_USER=kky -e POSTGRES_PASSWORD=123456 -e POSTGRES_DB=blog \
  -p 5432:5432 \
  -v <上一步打印的卷名>:/var/lib/postgresql \
  blog-postgres-zhparser:18

# 4) 验证既有库仍在，并确认扩展与检索配置
docker exec pgsql psql -U kky -d blog_stage2 -tAc \
  "SELECT extname FROM pg_extension WHERE extname='zhparser';"
docker exec pgsql psql -U kky -d blog_stage2 -tAc \
  "SELECT to_tsvector('chinese','使用 EF Core 做数据库优化与全文检索');"

# 5) 确认无误后删除旧容器
docker rm pgsql-old
```

> **注意**：`-v <卷名>:/var/lib/postgresql` 的挂载点是 `/var/lib/postgresql`，
> 与官方镜像一致（不是 `/var/lib/postgresql/data`）。PG18 的 `PGDATA` 实际是
> `/var/lib/postgresql/18/docker`，所以卷挂在这一层才能覆盖到数据目录。
> 写错会导致容器以为数据目录为空而重新初始化，表现为「数据看起来丢了」（实际还在卷里）。

---

## 5. 切换执行记录与验证（2026-09-11）

### 执行

```bash
# 1) 先做安全备份（不依赖卷是否完好）
docker exec pgsql pg_dump -U kky -d blog_stage2 --no-owner --no-acl -f /tmp/b.sql
docker cp pgsql:/tmp/b.sql D:/tmpbuild/pgbackup/

# 2) 换容器（数据卷沿用）
docker stop pgsql && docker rename pgsql pgsql-old
docker run -d --name pgsql \
  -e POSTGRES_USER=kky -e POSTGRES_PASSWORD=123456 -e POSTGRES_DB=blog \
  -p 5432:5432 \
  -v 3aaacea5a25e5a7f7b0982fec9347baa1eeeede50c4e0541dbf704d818d77ce6:/var/lib/postgresql \
  blog-postgres-zhparser:18
```

### 验证结果

| 检查项 | 结果 |
|---|---|
| 数据库全部保留 | ✅ `blog` / `blog_dev` / `blog_stage2` / `hangfire_dev` |
| 业务数据未丢 | ✅ Posts 9 / Users 7 / Authors 4 / SiteConfigs 4 / SocialLinks 3 / Collections 2 |
| 迁移记录完整 | ✅ 2 个迁移均在（`InitCreate`、`AddAuthCollectionsAndFts`） |
| GIN 索引仍在 | ✅ `ix_posts_search` |
| `chinese` 检索配置 | ✅ parser = `zhparser`，token 映射 `a,e,i,j,l,n,q,v` |
| 中文分词为词级 | ✅ `'core':3 'ef':2 '优化':6 '使用':1 '做':4 '全文检索':7 '数据库':5` |
| 检索匹配 | ✅ `@@ plainto_tsquery('chinese','数据库')` → `t` |
| 应用连通 | ✅ 后端启动即迁移成功，`/api/posts/search?keyword=数据库` 返回 200 |

### 关键验证：容器重建后扩展是否还在

在**一次性容器 + 全新空数据卷**上模拟"容器重建"：

```bash
docker run -d --name pgsql-zh-verify -p 5433:5432 \
  -e POSTGRES_USER=kky -e POSTGRES_PASSWORD=123456 -e POSTGRES_DB=zh_verify \
  blog-postgres-zhparser:18
```

| 检查项 | 结果 |
|---|---|
| entrypoint 执行初始化脚本 | ✅ 日志出现 `running /docker-entrypoint-initdb.d/01-zhparser.sql` |
| 扩展自动创建 | ✅ `ext=zhparser` |
| 检索配置自动创建 | ✅ `cfg=chinese` |
| 分词可用 | ✅ `'中文':1 '全文检索':2 '测试':3` |

验证后已删除该容器。**结论：容器重建不再影响全文检索。**

### 回退点

旧容器保留为 `pgsql-old`（`postgres:18.6`，已停止状态）。
确认新容器稳定后可删除：

```bash
docker rm pgsql-old
```

备份文件位于 `D:/tmpbuild/pgbackup/blog_stage2_<时间戳>.sql`。

---

## 6. 应用镜像与本地整栈编排（`docker-compose.yml`）

> 记录日期：2026-09-12 ｜ 相关说明见 [../docs/07-开发与运维手册.md](../docs/07-开发与运维手册.md) §8.6

### 6.1 本目录新增的产物

| 文件 | 职责 |
|---|---|
| `webapi.Dockerfile` | 后端镜像（多阶段：SDK 构建 → ASP.NET 运行时） |
| `nginx.Dockerfile` | 前端构建（Node）→ Nginx 网关镜像 |
| `nginx.conf` | Nginx 站点配置：静态文件 + `/api` 反代 + SPA fallback |
| `../docker-compose.yml` | 四服务编排：`nginx` / `webapi` / `pgsql` / `redis` |
| `../.env.example` | 环境变量模板（**含密钥的真实 `.env` 不入库**） |
| `../.dockerignore` | 构建上下文排除规则（**必需**，见 §6.4） |

### 6.2 为什么要在本地跑「生产形态」

`docs/07` §8.2 预告过三个**只在部署后才暴露**的坑。用这套 compose，
它们**全部可以在本地复现**，从而在买服务器之前就踩完：

| 坑 | 本地复现方式 |
|---|---|
| SPA history fallback | `curl -i http://localhost:8080/post/<id>` —— 配错立刻 404 |
| `X-Forwarded-For` | 对比带/不带伪造头的限流行为（§6.5 有实测脚本） |
| 工作目录 | 容器内 `WORKDIR /app` 决定 `logs/` 与 `media/` 落在哪 |

**收益**：远程部署时你只需要面对「服务器环境」这一个变量，而不是两个。

### 6.3 用法

```bash
# 在仓库根目录
cp .env.example .env                    # 填好 JWT_SIGNING_KEY 与 POSTGRES_PASSWORD
docker compose up -d --build            # 首次构建较慢（拉 SDK/Node 镜像）
docker compose ps                       # 四个服务都应为 healthy
curl -i http://localhost:8080/health    # 期望 200 + Healthy
docker compose logs -f webapi           # 看应用日志
docker compose down                     # 停止（数据在命名卷里，不会丢）
docker compose down -v                  # ⚠️ 连数据卷一起删
```

> 端口只发布 `nginx` 的 `8080`。`pgsql` 与 `redis` **刻意不映射到宿主机**：
> ① 避免与开发用的独立容器 `pgsql`/`redis` 抢占 5432/6379；
> ② 更接近生产——数据库不该直接对外。
> 需要直连时用 `docker compose exec pgsql psql -U kky -d blog_stage2`。

### 6.4 ⚠️ 构建踩到的坑（两个都是真实发生过的）

**坑 A：缺少 `.dockerignore` 会让 Windows 的 `obj/` 污染 Linux 构建**

`COPY Blog.Backend/ ./` 会把宿主机上 **Windows 生成的 `obj/`** 一起复制进镜像，
其中的 `project.assets.json` 记录着 Windows 路径。Linux 容器里 MSBuild 读它直接报错：

```
error MSB4018: The "ResolvePackageAssets" task failed unexpectedly.
NuGet.Packaging.Core.PackagingException:
  Unable to find fallback package folder
  'C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages'
```

**注意它的顺序**：`dotnet restore` 先在容器里跑成功了，是**随后的 `COPY` 把正确结果覆盖掉了**。
这类"文件顺序导致"的失败最难从报错本身看出来。

**解法**：仓库根的 `.dockerignore` 里排除 `**/bin` 与 `**/obj`（已落地）。

**坑 B：Docker Hub 在部分网络下不可达**

`docker compose up` 报 `failed to resolve reference "docker.io/library/redis:7-alpine"`。
`mcr.microsoft.com`（.NET 官方镜像）通常可达，但 Docker Hub 不一定。

**解法**：给 Docker Desktop 配置 registry mirror（Settings → Docker Engine）：

```json
{
  "registry-mirrors": ["https://docker.m.daocloud.io"]
}
```

配置后需重启 Docker Desktop。**镜像源地址会随时间失效，本文记录的只是一个 2026-09 仍可用的。**

**坑 C：健康检查写 `localhost` 会让容器永远 `unhealthy`，但服务其实完全正常**

最初 nginx 的健康检查写的是 `wget -qO- http://localhost/`，结果：

```
wget: can't connect to remote host: Connection refused
```

而**从宿主机 `curl http://localhost:8080/` 一切正常**。

**根因**：Alpine 里 `localhost` 优先解析成 IPv6 `[::1]`，
而 nginx 默认的 `listen 80;` **只绑 IPv4**。于是容器内探活失败、外部访问正常。

**解法**（两处都改了，缺一不可）：

| 位置 | 改动 |
|---|---|
| `nginx.Dockerfile` / `webapi.Dockerfile` 的 HEALTHCHECK | 地址写 `127.0.0.1`，不写 `localhost` |
| `nginx.conf` | 加 `listen [::]:80;`，同时监听 IPv6 |

> **这个坑的教训**：「服务能访问」和「探针说健康」是**两个独立的信号**。
> 只验证前者，你会带着一个永远 unhealthy 的容器上线 ——
> 而编排系统（Swarm / K8s / 甚至 `depends_on: service_healthy`）会因此拒绝启动下游服务。

### 6.5 验证记录（2026-09-12 实测）

| 检查项 | 结果 |
|---|---|
| 四个容器状态 | ✅ `pgsql` / `redis` / `webapi` / `nginx` **全部 `healthy`** |
| `/health` 经 Nginx 反代 | ✅ `200` + `Healthy` |
| `/api/site/config` 经 Nginx | ✅ `200`，返回真实种子数据 |
| 前端首页 | ✅ `200` / `text/html` / 904 字节 |
| 静态资源缓存头 | ✅ `/assets/*` 返回 `Cache-Control: max-age=31536000` + `public, immutable` |
| **坑 1** SPA fallback | ✅ `/post/<uuid>`、`/admin/posts/new`、`/collections/xxx` 均返回 index.html；`/assets/不存在.js` 正确 404 |
| **坑 2** `X-Forwarded-For` | ✅ 修复后：伪造 XFF 连发 28 次 → 26 次被限流（修复前 **0 次**，见 §6.6） |
| **坑 3** 工作目录与持久化 | ✅ 上传文件写入 `media` 卷（`app` 用户所有）；日志写入 `logs` 卷；`media-seed` 只读回退可用（`GET /api/files/avatar-default.webp` → `200 image/webp`） |
| 新增坑：上传体积 | ✅ 2 MiB 文件上传成功且在**后端日志中可见**（未设 `client_max_body_size` 时会被 nginx 413 拦掉，后端毫无记录） |
| 路径穿越防护 | ✅ `/api/files/....//....//etc/passwd` 到达应用后返回 `404 文件不存在` |

### 6.6 ⚠️ 发现并修复的安全缺陷：限流可被绕过

**这是本轮最有价值的发现，且只有把栈真正跑起来才能发现。**

| 测试 | 修复前 | 修复后 |
|---|---|---|
| A：正常请求 28 次 | 第 21 次起 `429` ✅ | 同左 ✅ |
| B：每次伪造不同的 `X-Forwarded-For` | **0 次 `429`**（完全绕过）❌ | **26 次 `429`** ✅ |

**根因是信任边界搞错了**：

1. `nginx.conf` 原先用 `proxy_add_x_forwarded_for`（nginx 文档里的常见范例），
   它会把**客户端自己发的 `X-Forwarded-For` 保留在前面**再追加真实 IP，
   头变成 `"<客户端伪造的IP>, <真实IP>"`。
2. 而 `RateLimitingMiddleware.GetClientIp()` 取的是 `Split(',')[0]` —— **第一段**，
   也就是**客户端完全可控的那一段**。

于是只要每次请求换一个伪造 IP，每个请求都会拿到一个全新的令牌桶。

**修复**：nginx 侧改用 `$remote_addr` **覆盖**整个头，把不可信输入丢掉：

```nginx
proxy_set_header X-Forwarded-For $remote_addr;
```

**前提**：nginx 是唯一入口（本项目拓扑正是如此，`webapi` 不对外发布端口）。
若将来前面再加 CDN / 云负载均衡，需要改用 `ngx_http_realip_module` 信任上游网段，
**而不能简单回到追加写法**。

> **更彻底的方案**（`[计划中]`，见 [../docs/09-已知限制与技术债.md](../docs/09-已知限制与技术债.md)）：
> 应用侧改用 ASP.NET Core 的 `ForwardedHeadersMiddleware`，
> 通过 `KnownProxies` / `KnownNetworks` 显式声明**只信任哪些代理**发来的转发头。
> 那才是把"信任边界"表达在代码里，而不是依赖部署配置。
> 在当前拓扑下 nginx 覆盖已经足够，故未立即实施。
