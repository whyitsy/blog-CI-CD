# tools/load —— 造数据 + 压测物料（本地性能基线）

> **为什么这些东西必须在仓库里**：`learn/03` §16 坑 6 记着本项目的前车之鉴 ——
> CDP 脚本曾放在仓库外，无版本控制、换机即丢。压测脚本是**项目资产**，
> 它要能在 CI 里跑、能被 diff、能跟着接口契约一起演进。
>
> 方法学在 [learn/03-性能与运行时诊断.md](../../learn/03-性能与运行时诊断.md)，
> 执行计划与 SLO 草案在 [docs/06-技术债与待办.md](../../docs/06-技术债与待办.md) §3.1。
> 本目录只放**怎么跑**。

---

## 0. 目录结构

```
tools/load/
├── seed-posts.mjs            # 造数据：直接写库灌 N 篇带长正文的中文文章（幂等、可清理）
├── cold-cache-probe.mjs      # 冷缓存探针：每轮 FLUSHDB 后单次请求
├── run-baseline.sh           # 一键跑完 B0 冒烟 + 六接口基线 + 冷缓存 + 汇总
├── summarize-baseline.mjs    # 把 k6 摘要 JSON 汇总成 markdown 表格
├── docker-compose.loadtest.yml  # 压测专用覆盖：**关掉限流**（不改仓库里任何 appsettings）
├── k6/
│   ├── lib/common.js         # BASE_URL / SLO 阈值 / 统一响应体校验 / 结果摘要
│   ├── 00-smoke.js           # B0 冒烟：10 RPS × 30s（环境门禁）
│   ├── 01-posts-list.js      # GET /api/posts（首页列表）
│   ├── 02-post-detail.js     # GET /api/posts/{id}（详情，触发浏览量写入）
│   ├── 03-posts-search.js    # GET /api/posts/search（中文全文检索）
│   ├── 04-posts-archives.js  # GET /api/posts/archives（归档，全量不分页）
│   ├── 05-site-config.js     # GET /api/site/config（站点配置，5 个聚合查询 + Redis）
│   └── 06-posts-create.js    # POST /api/posts（写路径，低并发）
└── results/                  # 历次基线的原始日志与汇总（**实测证据，一并入库**）
```

---

## 1. 前置条件

| # | 前置 | 怎么确认 |
|---|---|---|
| 1 | 整栈在跑 | `docker compose ps` 四个容器 healthy；`curl -s http://localhost:8080/health` 返回 `Healthy` |
| 2 | 有足够的数据量 | 至少 1,000 篇（不足时索引不生效、查询计划退化，数字会**给人虚假的安全感**，见 `learn/03` §7.1） |
| 3 | **限流已关闭** | 见 §4。没关的话你测的是限流器，不是应用 |
| 4 | 压测工具与被压服务在**同一侧** | 本项目开发方式是"WSL 发命令、Windows 跑程序"，**跨 WSL/Windows 边界的转发本身就是瓶颈**（`learn/03` §16 坑 8）。本目录的脚本都打 `127.0.0.1:8080`（WSL 侧直连 nginx），符合要求（`docs/05` §2.1） |
| 5 | 机器相对空闲 | 关掉 IDE / 浏览器 / 下载任务。既是压测机又是被测机时，抢 CPU 的数字没有意义（坑 9） |

### 安装 k6

k6 是单个 Go 二进制，三种装法任选：

```bash
# ① 官方仓库（需要 sudo）
sudo gpg -k && sudo gpg --no-default-keyring --keyring /usr/share/keyrings/k6-archive-keyring.gpg \
  --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys C5AD17C747E3415A3642D57D77C6C491D6AC1D69
echo "deb [signed-by=/usr/share/keyrings/k6-archive-keyring.gpg] https://dl.k6.io/deb stable main" \
  | sudo tee /etc/apt/sources.list.d/k6.list && sudo apt-get update && sudo apt-get install k6

# ② 直接下 release 二进制（**不需要 sudo**，本仓库实测走的就是这条）
mkdir -p ~/.local/bin && cd /tmp
curl -sL -o k6.tar.gz https://github.com/grafana/k6/releases/download/v1.4.1/k6-v1.4.1-linux-amd64.tar.gz
tar xzf k6.tar.gz && cp k6-v1.4.1-linux-amd64/k6 ~/.local/bin/ && export PATH="$HOME/.local/bin:$PATH"
k6 version     # 实测：k6 v1.4.1-release (go1.25.4, linux/amd64)

# ③ Docker（本项目的 zsh/bash 里 docker 一定可用，等价于 ②）
docker run --rm -i --network=host grafana/k6 run - < tools/load/k6/01-posts-list.js
```

> ⚠️ **本仓库路径含中文，k6 会 panic**（实测：`unexpected k6 panic: stat /mnt/d/dotNET%E9%A1%B9%E7%9B%AE/...: no such file or directory`）。
> 根因是 k6 把脚本路径当 URL 转义后再 `stat`。**两种绕法**：
> ① 建一个纯 ASCII 软链，从软链目录里用相对路径跑（`run-baseline.sh` 自动做的就是这个）：
> ```bash
> ln -sfn "/mnt/d/dotNET项目/Stage2" /tmp/stage2 && cd /tmp/stage2 && k6 run tools/load/k6/00-smoke.js
> ```
> ② 用 ③ 的 Docker 方式（容器内路径是 `/` 挂载点，不含中文）。

---

## 2. 造数据

```bash
node tools/load/seed-posts.mjs              # 默认 1000 篇
node tools/load/seed-posts.mjs 10000        # 10,000 篇（docs/06 建议的更真实量级）
node tools/load/seed-posts.mjs --clean 1000 # 先清理历史压测数据再灌
node tools/load/seed-posts.mjs --only-clean # 只清理
node tools/load/seed-posts.mjs --dry-run 1000  # 只看 SQL 规模，不执行
```

**它做了什么**

| 项 | 说明 |
|---|---|
| 写法 | **不走 API，直接写库**（`docker compose exec -T pgsql psql`，SQL 从 stdin 灌）。走 `POST /api/posts` 1000 篇要撞 `write` 限流 2/秒，光限流就 500 秒 |
| 正文 | 每篇 **2.6~4.1 KB 中文 Markdown**（含标题层级、代码块、排查清单）。列表页不加载正文，但详情页加载完整 Content、搜索要过 zhparser 分词 —— 正文太短会把这两处开销严重低估 |
| 发布状态 | 约 80% 已发布、20% 草稿；发布时间散布在最近 30 个月（归档接口因此有 30+ 个分组） |
| 分类 / 标签 | 随机分配，1~4 个标签/篇；同时**复用库里已有的真实分类与标签**（只用自己的会让按分类过滤的计划与线上不同） |
| 唯一性 | 标题带序号（`[LOADTEST] #00001 · ...`），摘要带序号前缀，**标题与摘要都不重复** |
| 幂等 | ID 由「固定基准时间 + 序号」生成的 UUIDv7 决定、正文由固定种子的 PRNG 决定 ⇒ 重跑 `ON CONFLICT DO NOTHING`，不会翻倍 |
| 生成列 | **不写 `SearchVector`**（`GENERATED ALWAYS ... STORED`，写了直接报错）；`Version` 写 1、`IsDeleted` 写 false（NOT NULL 且无数据库默认） |
| 收尾 | 自动 `ANALYZE`（没有统计信息时规划器可能选全表扫描，测出来的就不是"有索引时的性能"） |

**清理**：所有压测数据的标题都以 `[LOADTEST]` 开头，分类/标签同理 —— 这就是清理依据。
`--only-clean` 只删这三类，**绝不碰真实文章**（关联的 `PostTag` 行由外键 `ON DELETE CASCADE` 自动清掉）。

> ⚠️ **直接写库不会让应用的缓存失效。** 灌完之后 Redis 里还是灌之前的旧值，
> 第一次跑接口会看到"数据明明有 1000 篇，归档却只有 3 篇"。
> **灌完必须 `docker compose exec -T redis redis-cli FLUSHDB`**（或等 TTL 过期）。
> `run-baseline.sh` 的预热步骤会顺带把缓存刷成新数据。

---

## 3. 压测：每个脚本回答什么问题

```bash
export PATH="$HOME/.local/bin:$PATH"
k6 run tools/load/k6/00-smoke.js                      # B0 冒烟（先跑这个）
k6 run tools/load/k6/01-posts-list.js                 # 首页列表
k6 run -e DEEP_PAGES=20 tools/load/k6/01-posts-list.js # 追加：深分页（第 20 页）
k6 run tools/load/k6/02-post-detail.js                # 详情
k6 run -e SAME_POST=1 tools/load/k6/02-post-detail.js # 追加：只打同一篇（行锁对照）
k6 run tools/load/k6/03-posts-search.js               # 全文检索
k6 run tools/load/k6/04-posts-archives.js             # 归档
k6 run tools/load/k6/05-site-config.js                # 站点配置
k6 run tools/load/k6/06-posts-create.js               # 写文章（0.5 RPS，会真的新增文章）
```

| 脚本 | 回答什么问题 | 关键观察点 |
|---|---|---|
| `00-smoke` | 这套环境能不能开始压？ | `/health` 就绪、无 429、无 5xx。**冒烟不过，后面的数字全部作废** |
| `01-posts-list` | 最热读路径在这种数据量下多快？深分页是否更慢？ | p95 与 `dropped_iterations`；`DEEP_PAGES` 组的 p95 对比 |
| `02-post-detail` | 详情（含浏览量自增写）多快？**同一行的 UPDATE 会不会串行化**？ | `SAME_POST=1` 与默认（100 篇分散）两组的 **p99 比值** —— 这是 `docs/06` §3.2 实验 C 的判据 |
| `03-posts-search` | 中文全文检索（GIN + `ts_rank` + CTE）多快？ | 关键词按迭代轮换（宽泛词/生僻词混合），p99 才代表搜到宽泛词时的体验 |
| `04-posts-archives` | 无分页、全量内存分组的接口，响应体和延迟多大？ | 额外记录 **响应体 KiB** —— 它的风险在传输量，不在服务端算得快不快 |
| `05-site-config` | 5 个聚合查询 + Redis 缓存的接口，热/冷差多少？ | 热缓存看本脚本；**冷缓存看 `cold-cache-probe.mjs`** |
| `06-posts-create` | 写路径的单次成本、以及写后缓存失效的连带代价 | 低 RPS 是刻意的：**用写 RPS 衡量容量是误用**。它还会记录"写后立刻读列表"的延迟 |

> ⚠️ **写路径不能用读路径的载荷。** `run-baseline.sh` 对 `06-posts-create.js` 用**独立的
> `--write-rate`（默认 0.5 RPS）**，不跟随 `--rate`。第一版脚本忘了这一点，用 10 RPS 压写接口，
> 30 秒里顺手造了 **301 篇**垃圾文章 —— 那不是"写容量"，那是配置事故。

**所有脚本共有的三件事**（`learn/03` §8 的硬要求）：
① `thresholds` 里写 SLO（跑完自动判定，不靠人读报告）；
② 解析响应体的 **`code === 0`**（HTTP 200 也可能是业务失败）；
③ `BASE_URL` 可配置（`-e BASE_URL=https://blog.example.com`）。

常用覆盖参数：`-e RATE=10 -e DURATION=30s -e PRE_VUS=10 -e MAX_VUS=100`，
以及 `-e SLO_P95_LIST=300` 这类阈值覆盖（用来对比"如果 SLO 是别的值会怎样"）。

---

## 4. 怎么关限流（**压测的必要前提**）

`docs/06` §3.1 前置条件 5 / `learn/03` §16 坑 1：限流是按 IP 的令牌桶，
单 IP 压测走完突发容量后会被限到 **20 RPS（默认）/ 2 RPS（search、write）**，
超了返回 429。**不关限流，你测的是限流器的设定值，与应用容量毫无关系。**

```bash
# ① 关限流（只重建 webapi，PG/Redis 不动）
docker compose -f docker-compose.yml -f tools/load/docker-compose.loadtest.yml up -d webapi

# ② ⚠️ 必须重启 nginx —— 否则可能 502
#    原因：nginx 在启动时解析了上游主机名 webapi 并缓存了它的 IP；
#    webapi 容器被重建后 IP 可能变化，nginx 不会自动重新解析。
docker compose restart nginx

# ③ 验证（search 规则容量是 20，连打 25 次不该出现 429）
for i in $(seq 1 25); do curl -s -o /dev/null -w '%{http_code}\n' \
  "http://127.0.0.1:8080/api/posts/search?keyword=%E7%BC%93%E5%AD%98"; done | sort | uniq -c
#   期望：25 个 200（若出现 429 ⇒ 限流还开着，别继续压）

# ④ 压测结束后恢复
docker compose up -d webapi && docker compose restart nginx
```

实现用的是 `RateLimit__Enabled=false` 这个 **.NET 标准环境变量**（`__` = 层级分隔符），
写在 `docker-compose.loadtest.yml` 里 —— **不改仓库里任何 appsettings 文件**，
因此不存在"压测配置被误提交"的风险。

---

## 5. 一键基线

```bash
export PATH="$HOME/.local/bin:$PATH"
./tools/load/run-baseline.sh                          # 10 RPS × 30s（写接口 0.5 RPS）
./tools/load/run-baseline.sh --rate 20 --duration 60s # 改读接口载荷
./tools/load/run-baseline.sh --write-rate 2           # 改写接口载荷（默认 0.5，刻意压低）
./tools/load/run-baseline.sh --skip-cold              # 跳过冷缓存探针
```

它会依次：记录环境规格 → 等 `/health` → **校验限流已关（没关直接退出）** → 预热缓存 →
B0 冒烟 → 六接口基线 → 冷缓存探针 → 汇总成
`tools/load/results/baseline-<时间戳>.md`（含环境规格 + p50/p95/p99 表）。

> 单个脚本未达标（threshold 失败）**不会中断整轮**：冷缓存与汇总照常产出，
> 最后以非 0 退出提醒你。否则一次 search 未达标会让你连其它五个接口的数字都拿不到。
>
> 原始输出一律写 `.txt` 而不是 `.log` —— 仓库根的 `.gitignore` 忽略了 `*.log`，
> 用 `.log` 会让**实测证据进不了仓库**（这条是踩出来的）。

> 环境规格是**必填项**，不是装饰：没有 CPU 核数、内存、数据量、冷热状态，
> 数字换台机器就失效、也无法与上一次比较（`learn/03` §11.1 纪律 3）。

---

## 6. 怎么读结果

| 看什么 | 怎么理解 | 危险信号 |
|---|---|---|
| **p50 / p95 / p99** | p50 决定"用户觉得快不快"，p99 决定"用户会不会骂人" | **不要看平均值**（99 个 10ms + 1 个 9910ms，平均只有 109ms） |
| `business_errors` | `code !== 0` 的比例 —— 统一响应体下的**真实**错误率 | 与 `http_req_failed` 一起看，只看后者会系统性低估 |
| `http_req_failed` | HTTP 层失败率（4xx/5xx/网络） | 出现 429 ⇒ 限流没关，数字作废 |
| `dropped_iterations` | k6 想发但**发不出去**的请求数（VU 不够） | > 0 说明系统已经慢到排不出请求 —— 这本身就是结论 |
| `checks` | 响应结构与 `code === 0` 的断言通过率 | 必须 100% |
| 冷 / 热缓存 | 热缓存是稳态，冷缓存是**容量下限** | 只测热缓存会得出严重乐观的结论（TTL 集中到期/重启/扩容时才是危险时刻） |

冷缓存的正确测法是**每轮清 Redis 再请求一次**（`cold-cache-probe.mjs`）：
恒定载荷下只有第一个请求是冷的，直接用 k6 跑 30 秒，p95 会被热缓存淹没。

---

## 7. 这个目录踩过的坑（省下你重复踩的时间）

| # | 坑 | 现象 / 应对 |
|---|---|---|
| 1 | **k6 遇到非 ASCII 路径会 panic** | `stat /mnt/d/dotNET%E9%A1%B9%E7%9B%AE/...: no such file or directory` → 用 ASCII 软链或 Docker 跑（§1） |
| 2 | 忘了关限流 | search/write 大面积 429。`run-baseline.sh` 会在开压前**主动退出**并打印处置命令 |
| 3 | 重建 webapi 后 nginx 502 | nginx 缓存了上游 IP，`docker compose restart nginx` 即可（§4） |
| 4 | **直接写库后接口仍返回旧数据** | 应用缓存没失效 → `redis-cli FLUSHDB` |
| 5 | 数据量太少 | 规划器直接全表扫描，GIN 索引根本不参与，数字毫无意义（先用 `seed-posts.mjs` 灌 1000+） |
| 6 | 把 `POST /api/posts` 也按读接口的 RPS 压 | 写路径的 SLO 与容量是两回事；本脚本刻意用 0.5 RPS |
| 7 | 压测脚本写死 URL | 全部走 `-e BASE_URL=`，同一个脚本能指本地 / 测试 / 生产 |
| 8 | 只统计 HTTP 状态码 | `{code,message,data}` 里 `code !== 0` 也是失败，脚本必须解析 |
| 9 | k6 摘要里 `http_reqs` 显示为 0 | k6 **只为被 `thresholds` 引用的标签子指标**生成摘要条目 —— 脚本里已补 `http_reqs{endpoint:x}: ['count>0']` |
| 10 | `06-posts-create.js` 会真的写数据 | 标题统一带 `[LOADTEST]`，用 `node tools/load/seed-posts.mjs --only-clean` 清理 |
| 11 | **k6 的 `rate` 必须是正整数** | 想跑 0.5 RPS 不能写 `rate: 0.5`，会在解析 options 时直接失败：`cannot unmarshal number 0.5 into Go struct field Options.scenarios.rate of type int64`。正确写法是 **`rate: 1` + `timeUnit: '2s'`**（`lib/common.js` 里已自动换算） |
| 12 | 用 `.log` 存压测输出 | `.gitignore` 忽略 `*.log` ⇒ 证据进不了仓库。统一用 `.txt` |
| 13 | **写路径跟着读路径的 RPS 走** | 会造出几百篇垃圾文章，且测的不是写容量。`run-baseline.sh` 用独立的 `--write-rate`（默认 0.5 RPS） |

---

## 8. 已测基线（2026-09-14，本地 compose 整栈）

| 接口 | p50 | p95 | p99 |
|---|---|---|---|
| `GET /api/posts` 首页列表 | 1.64 ms | 2.21 ms | 2.37 ms |
| `GET /api/posts/{id}` 详情 | 4.06 ms | 6.91 ms | 8.35 ms |
| `GET /api/posts/search` 检索 | 1.67 ms | 2.41 ms | 7.28 ms |
| `GET /api/posts/archives` 归档 | 2.31 ms | 3.25 ms | 3.87 ms |
| `GET /api/site/config` 站点配置 | 1.55 ms | 2.05 ms | 2.48 ms |
| `POST /api/posts` 写文章 | 7.04 ms | 7.98 ms | 8.25 ms |

冷缓存（每轮 `FLUSHDB` 后单次请求）p95：检索 13.09 ms ＞ 归档 7.48 ms ＞ 详情 7.63 ms ＞
列表 5.77 ms ＞ 站点配置 5.27 ms。

完整环境规格与明细见 `results/baseline-20260914-052631.md`；
其中 **归档接口单次响应 103.4 KiB**（796 篇已发布）值得单独记住：
它的风险在传输量，不在服务端延迟。

> ⚠️ 这批数字来自 **20 核 / 15GiB 的 WSL2 + 全组件回环**，**不代表目标机器的容量**。
> 目标机器规格未定之前，不要用它做容量承诺。

---

## 9. 结果去哪儿

- 每次基线的汇总：`tools/load/results/baseline-<时间戳>.md`（环境规格已内嵌在其中）
- 造数据日志：`tools/load/results/seed-<篇数>.txt`
- k6 原始摘要 JSON、逐接口输出、爬坡与冷缓存明细：`tools/load/results/raw/`
- 结论与 SLO 定稿：`docs/06-技术债与待办.md` §3.1（做完即从这里删条目），
  「怎么用」留在本文件，「为什么」留在 `learn/03`
