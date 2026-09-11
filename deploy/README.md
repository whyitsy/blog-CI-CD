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

## 4. 切换开发环境的 PostgreSQL 容器

**当前开发容器 `pgsql` 是官方镜像 + 手工编译的扩展**，数据在**命名卷**里。
因为数据在卷上（而不是容器内），**替换容器不会丢数据**：

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

# 4) 验证既有库仍在，并创建扩展与检索配置
docker exec pgsql psql -U kky -d blog_stage2 -c "CREATE EXTENSION IF NOT EXISTS zhparser;"
#    检索配置用 postgres-init/01-zhparser.sql 的 DO 块（对已有库需手工执行一次）

# 5) 确认无误后再删除旧容器
docker rm pgsql-old
```

> **注意**：`-v <卷名>:/var/lib/postgresql` 的挂载点是 `/var/lib/postgresql`，
> 与官方镜像一致（不是 `/var/lib/postgresql/data`）。写错会导致容器以为数据目录为空而重新初始化，
> 表现为「数据看起来丢了」（实际还在卷里）。
>
> **本步骤尚未执行** —— 需要你确认后再切换（见 [../docs/09-已知限制与技术债.md](../docs/09-已知限制与技术债.md) §2）。
> 在切换之前，当前容器里的 zhparser 是手工装的，**容器一重建就会失效**。
