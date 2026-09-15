# 部署脚本

> 这一目录放**可执行的上线脚本**。方法论与背景在 [`../../docs/05-运维与部署手册.md`](../../docs/05-运维与部署手册.md) §8，
> 这里只讲"照着敲什么"。
>
> **状态**：脚本已就位，**尚未在真实服务器上跑过**（2026-09-14）。第一次上线时如果哪一步卡住，
> 把原始输出贴回来，我们把它变成这个目录里的下一条经验。

---

## 0. 服务器：已选定（2026-09-14）

| 项 | 实际配置 |
|---|---|
| 厂商 / 地域 | 腾讯云 · **中国香港**（免备案，内地访问 70~120 ms） |
| 规格 | 入门型 Linux **2 核 2G** |
| 系统盘 | 40 GB SSD |
| 峰值带宽 | 20 Mbps |
| 月流量包 | 0.5 TB |
| 镜像 | Ubuntu 24.04 LTS |

实测依据（2026-09-14 在开发机上真跑 `pack-images.sh` 得到）：

| 项 | 实测值 | 怎么测的 |
|---|---|---|
| 运行态内存 | webapi **145 MB** + nginx 17 MB + PG 33 MB + redis 8 MB ≈ **200 MB** | `docker stats --no-stream` |
| **上线传输量** | **567 MB**（一个 tar.gz，含三个镜像） | `ls -lh dist-images/`，**这是决定传输时间与磁盘占用的数** |
| 其中 PG 镜像是大头 | 它含从源码编译的 zhparser + SCWS | — |
| 结论 | **运行很轻，重的是构建与首次传输**——所以镜像在本地构建、传上去；PG 镜像只在第一次传 | — |

> ⚠️ **别看 `docker images` 的 SIZE 列**：它在当前 Docker（containerd 镜像存储）下报的是"虚拟大小"
> （本项目实测 PG 显示 **1.98 GB**），而真实的传输产物是 **567 MB**。
> 两个数差 3~4 倍，按前者估容量会白白多买磁盘。**以 `pack-images.sh` 的产出为准。**

### 0.1 为什么 2 核 2G 够用 —— 但**必须**满足三个前提

2 GB 装得下（预算见下表，实际占用约 450~700 MB，余量 1.3 GB），
但**默认状态下它会不稳**，三个前提缺一不可：

| 前提 | 不做的后果 | 现在在哪 |
|---|---|---|
| **① 有 swap** | PG 做 VACUUM / 大排序时冲高 → 内核 OOM Kill 挑中 PG → 「博客突然 502，事后发现 pgsql 重启过」，而你会去查应用代码 | `server-init.sh` **第 1 步自动创建**（含写 `/etc/fstab`） |
| **② 给 webapi 设内存上限** | 不设时 .NET 按**宿主机 2 GB** 算 GC 堆硬上限 = 1.5 GB，一次大图上传就可能涨到那个量级，把 PG 挤死 | 已配在 `docker-compose.yml`（`memory: 768m`），并由 `server-init.sh` **第 6 步查内核实际值断言生效** |
| **③ 不在服务器上构建镜像** | `dotnet publish` + `vite build` 并行编译在 2 GB 上大概率 OOM，还要额外几 GB 磁盘 | 用 `pack-images.sh` 在本地构建（§3） |

**内存预算**（实测 + 估算）：

| 项 | 占用 |
|---|---|
| Ubuntu 24.04 + systemd + sshd + journald | 150~250 MB |
| dockerd + containerd | 80~120 MB |
| webapi（空载实测） | 145 MB |
| pgsql（空载 33 MB；带负载含 shared_buffers） | 150~250 MB |
| redis + nginx | 25 MB |
| **合计** | **≈ 450~700 MB**（2 GB 余量 ≈ 1.3 GB） |

> 💡 **2 GB 是教具，4 GB 只是台机器。** swap 什么时候开始抖、`.NET` 的 GC 上限怎么算、
> `OOMKilled` 长什么样 —— 这些在 4 GB 上**一个都遇不到**。

### 0.2 选型时真正该看的指标：带宽 > 核数

同样写"2 核 2G"，不同套餐能差 30 倍带宽。**对博客的性价比排序是：
带宽 > 月流量包 > 磁盘 ≥ 内存 > 核数** —— 核数是这里**最不重要**的指标
（本地 10 RPS 压测 p95 只有 2~8 ms，瓶颈根本不在这）。

- 曾出现在 199 元/年档的**内地 1 Mbps** 套餐**直接排除**：1 Mbps ≈ 128 KB/s，一张 200 KB 的图要 1.6 秒。
- **香港节点免备案**；内地节点用域名必须 ICP 备案（约 1~2 周），备案前域名与 80/443 都不通。

> 价格来自公开测评与厂商公告（[阿里云香港轻量测评](https://developer.aliyun.com/article/1759576)、
> [腾讯云香港地域调价公告](https://cloud.tencent.com/announce/detail/2131)），**活动价随时变，以官网为准**。

**下次再买时**：镜像选 Ubuntu 24.04 LTS，**不要选带宝塔/WordPress 的应用镜像**（占端口、加变量）；
确认有**快照**功能（系统级回滚，与 `pg_dump` 互补）；确认流量包是**每月**还是**一次性**、
超出后是**限速**还是**计费**。

---

## 1. 首次登录加固

### 1.1 SSH 与防火墙

```bash
# 本地：把公钥传上去（不要用密码登录跑线上）
ssh-copy-id <用户>@<服务器>

# 服务器：确认公钥能登录之后，再关掉密码登录
sudo sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo systemctl restart ssh

# 防火墙：只放 22 / 80 / 443 # 直接在控制台添加规则即可
sudo ufw allow 22/tcp && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp
sudo ufw --force enable
```

### 1.2 swap（2 GB 机器必做）

`server-init.sh` 第 1 步会**自动**完成下面这些，正常情况下你不需要手工做。
这里写出来是为了：① 你能看懂它在干什么；② 它失败了你知道怎么补。

```bash
# 建 2 GB swap 文件
sudo fallocate -l 2G /swapfile || sudo dd if=/dev/zero of=/swapfile bs=1M count=2048
sudo chmod 600 /swapfile
sudo mkswap /swapfile
sudo swapon /swapfile

# ⚠️ 写进 fstab，否则重启后失效 —— 最容易漏的一步
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab

# swappiness 默认 60 太激进（内存还有富余就开始换出，PG 延迟会抖）
echo 'vm.swappiness=10' | sudo tee /etc/sysctl.d/99-blog.conf
sudo sysctl -p /etc/sysctl.d/99-blog.conf

# 验收
free -h && swapon --show
```

**为什么这是必需项而不是优化**：没有 swap 时，内核面对内存压力只有 OOM Kill 一条路，
而它挑中的往往正是 PG（RSS 大的进程优先）。

## 1.3 初始化建目录

```bash
sudo mkdir -p /opt/blog && sudo chown $USER /opt/blog
sudo mkdir -p /opt/blog/tools
```



---

## 2. 装 Docker

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker "$USER" && newgrp docker
docker compose version # 非 V1.x版本
```

## 3. 本地：构建并打包镜像

```bash
bash tools/deploy/pack-images.sh               # 应用镜像（webapi + nginx）—— 日常用这个
bash tools/deploy/pack-images.sh --all         # 四个都打 —— 首次部署
bash tools/deploy/pack-images.sh --only nginx  # 只改了前端 —— 只打 nginx
bash tools/deploy/pack-images.sh --dry-run     # 只预览会打哪些，不构建
```

实测体积（2026-09-15，gzip 后的 tar.gz）：

| 打包内容 | 体积 | 什么时候用 |
|---|---|---|
| 仅 `nginx` | **25 MB** | 只改了前端 / `deploy/nginx.conf` |
| 仅 `webapi` | **103 MB** | 只改了后端 |
| `webapi` + `nginx`（**默认**） | **128 MB** | 两边都改了 |
| 仅 `redis` | **15 MB** | `docker-compose.yml` 里改了 Redis 版本 |
| 仅 `pgsql` | **439 MB** | `deploy/postgres-zhparser.Dockerfile` 改了（罕见） |
| 四个全部 | **≈590 MB** | **只有首次部署** |

> ❌ **不要每次都传 PG 镜像。** 它单独就占 **439 MB**（含从源码编译的 zhparser + SCWS），
> 而它一个季度也未必变一次。旧版脚本每次都会把它打进包里 —— 那 439 MB 是纯浪费。

**关于 Redis**：它没有 `build:` 段，用的是**官方镜像**，脚本对它只 `pull` 不 `build`
（对比 PG：必须自编译 zhparser，所以非自建不可）。但它**仍然会被 `--all` 打进包里** ——
目的是让**服务器完全不需要连 Docker Hub**：那是个不受控的外部依赖
（`docs/05 §8.5` 坑 B 记录过 Docker Hub 不可达，大陆网络尤其常见）。

### 3.1 到底该打哪个？

**改了什么 → 影响哪个镜像**（这张表就是 `--changed` 的判断依据）：

| 改动位置 | 要重建 | 打哪个 |
|---|---|---|
| `Blog.Backend/**` | webapi | `--only webapi` |
| `Blog.FrontEnd/**` | nginx | `--only nginx` |
| `deploy/nginx.conf`、`deploy/nginx.Dockerfile` | nginx | `--only nginx` |
| `deploy/webapi.Dockerfile` | webapi | `--only webapi` |
| `deploy/postgres-zhparser.Dockerfile`、`deploy/postgres-init/**` | pgsql | `--only pgsql`（罕见） |
| `docker-compose.yml` 里 `redis:` 的 `image:` 行 | 无（改的是版本号） | `--only redis`（把新版本带上去） |
| 其它 `docker-compose.yml`、`tools/**`、`docs/**`、根 `README.md` | **不用重建** | —— 直接 scp |

不想记的话让它自己判断：

```bash
bash tools/deploy/pack-images.sh --changed --dry-run   # 先预览
bash tools/deploy/pack-images.sh --changed             # 相对「上次打包的提交」
bash tools/deploy/pack-images.sh --changed origin/main # 或指定基准
```

⚠️ **`--changed` 的基准是「上次打包」，不是「上次部署」。** 如果你打了包但部署失败了，
再跑 `--changed` 会漏掉已经打进那个包的内容 —— **拿不准就显式 `--only`，或直接 `--all`。**
不认识的路径一律按"可能影响全部"处理，因为少传一个镜像的失败方式是**静默的**。

## 4. 传到服务器并启动

```bash
# 在仓库根目录
ssh <用户>@<服务器> 'mkdir -p /opt/blog/tools'
scp dist-images/blog-images-*.tar.gz docker-compose.yml docker-compose.prod.yml \
    tools/deploy/server-init.sh tools/deploy/server-update.sh \
    <用户>@<服务器>:/opt/blog/

# 备份脚本也要上去：它靠 `docker compose exec` 工作，必须在 compose 目录下运行
scp -r tools/backup <用户>@<服务器>:/opt/blog/tools/

ssh <用户>@<服务器>
cd /opt/blog && bash server-init.sh blog-images-<时间戳>.tar.gz
```

> ⚠️ **不要传开发机的 `.env`。** 密钥应该在服务器上现生成（`server-init.sh` 第 3 步就是这么做的），
> 开发机的密钥一旦泄漏或复用，等于线上的 JWT 谁都能伪造。
>
> ⚠️ **`server-init.sh` 本身也要传上去**（上面第一组 `scp` 里的第二个文件）。
> 漏传的表现是：ssh 进去执行 `bash server-init.sh` 报 `No such file or directory`。
>
> 💡 `docker-compose.prod.yml` 与 `server-update.sh` 是**路径 A（§4.5.1）**用的，
> 首次部署可以一起传，也可以等要用时再传。
> 漏传的表现是：ssh 进去执行 `bash server-init.sh` 报 `No such file or directory`。

`server-init.sh` 会做八件事，每件都有验收输出：
**环境预检（compose / 磁盘 / swap）** → 装载镜像 → 生成 `.env`（`openssl rand` 现生成密钥）→ 起栈 →
等健康 → 验四容器 healthy → 验接口与**中文检索**（这条最能证明 PG 镜像搬对了）→ 打印上线后必做的两件事。

跑完之后**按它打印的做那两件事**（改管理员密码、配 HTTPS），别关掉终端就以为完事了。

> ⚠️ **刚跑完时，网站在公网上是访问不到的 —— 这是设计如此，不是部署失败。**
> `server-init.sh` 生成的 `.env` 里是 `HTTP_PORT=127.0.0.1:8080`，容器只监听**回环**，
> 所以它的第 7 步只能从服务器本机 `curl 127.0.0.1:8080` 验收。
> 想让外网访问，必须做 §6 的**宿主机 Nginx 终结 TLS**（这也正是绑回环的原因：
> 客户端不能绕过宿主机直连 8080，否则 §8.6 那个"限流信任边界"的前提就没了）。
>
> 如果**只是**想先冒烟验证一下外网可达（还没配域名和证书），可以临时：
> 把 `.env` 改成 `HTTP_PORT=8080` → `docker compose up -d nginx` →
> 在腾讯云控制台**安全组**放行 8080 → 用 `http://<服务器IP>:8080` 访问。
> 验证完请改回 `127.0.0.1:8080` 并把安全组的 8080 关掉。

---

## 4.5 日常更新一个新版本（改完代码之后走这个）

首次部署用 `server-init.sh`（§4）。**之后每次更新有两条路，日常走 A**：

| | 路径 | 传输量 | 什么时候用 |
|---|---|---|---|
| **A** | **CI 推 GHCR → 服务器 pull** | **几 MB**（Docker 按层拉） | **日常更新，推荐** |
| B | 本地打包 → scp → `docker load` | 25 / 103 / 128 MB | 首次部署、PG 变了、或 GHCR 不可用时 |

### 4.5.1 路径 A：从 GHCR 拉（推荐）

```
改代码 → commit → push → CI 绿（含 publish 作业推镜像）→ 合并
   ↓
服务器：bash tools/deploy/server-update.sh <提交 sha>
```

CI 合并进 main 后会跑 `publish` 作业，把 `webapi` 与 `nginx` 推到 GHCR，
tag 就是那次合并的**提交 sha**。服务器一条命令拉下来：

```bash
cd /opt/blog
bash tools/deploy/server-update.sh <sha>      # sha 从 CI 作业摘要里抄
bash tools/deploy/server-update.sh <sha> --check   # 只拉不切，先确认能拉到
```

**一次性准备**（只做一次）：

1. 服务器上要有 `docker-compose.prod.yml`
2. `.env` 里加一行 `IMAGE_PREFIX=ghcr.io/<owner>/<repo>`
3. 第一次推完镜像后，去 GitHub → **Packages** → 对应 package → Settings →
   把 visibility 改成 **public**（否则服务器要 `docker login ghcr.io`）
4. 手动跑一次 CI：Actions → CI → **Run workflow**（这个作业只能真跑验证）

> ⭐ **路径 A 顺带补上了一个完整性缺口。** 走路径 B 时，你部署的镜像是
> **你本地**在 CI 绿了之后构建的 —— 工作区脏的话，你会带着绿色徽章上线没测过的代码。
> 走 A 时服务器拉的就是 CI 从那个提交构建的镜像，**"线上跑的是哪个提交"有据可查**。
> 这正是 `docs/06` 的 **G4「没有发布流程与版本号」**。

> ⚠️ **PG 与 Redis 不走 GHCR**，仍然靠 `pack-images.sh --all` 首次传一次。
> 理由：PG 镜像 439 MB 且一年变不了几次，让 CI 每次重建重推不划算。

#### ⚠️ 光跑 `server-update.sh` 有时不够

**GHCR 里只有 `webapi` / `nginx` 两个镜像。** 改了别的东西却只跑这个脚本，
会出现「**镜像换了、配置没换**」—— 表现为某个功能不生效，而且**不报错**。

| 本次改了什么 | 光跑 `server-update.sh` 够吗 |
|---|---|
| `Blog.Backend/**`、`Blog.FrontEnd/**`、`deploy/nginx.conf`、`deploy/{webapi,nginx}.Dockerfile` | ✅ 够了 |
| `docker-compose.yml`、`docker-compose.prod.yml` | ⚠️ 还要 **scp** |
| `deploy/postgres-zhparser.Dockerfile`、`deploy/postgres-init/**` | ⚠️ 还要 `pack-images.sh --only pgsql` → scp → `docker load` |
| `docker-compose.yml` 里 redis 那行 `image:` | ⚠️ 还要 `pack-images.sh --only redis` → scp → `docker load` |
| `tools/backup/**`、`tools/deploy/*.sh` | ⚠️ 还要 scp |

**不用背这张表** —— CI 跑完会把结论**直接写进 `publish` 作业的摘要**里
（你本来就要去那儿抄 sha，答案出现在同一个地方最不容易漏）。
想提前看也可以：

```bash
bash tools/deploy/what-to-do.sh origin/main HEAD              # 这次合并需要额外做什么
bash tools/deploy/what-to-do.sh <服务器上正跑的 sha> <新 sha>
```

> 判断逻辑只有这一份（`what-to-do.sh`）—— CI 和本地调的是同一个脚本，
> 不会出现"文档里的表"和"CI 的判断"慢慢漂移。

### 4.5.2 路径 B：本地打包传输（首次部署与兜底）

```bash
# ① 本地
git status                                    # 必须干净
bash tools/deploy/pack-images.sh --changed --dry-run   # 预览：确认它只挑了该挑的
bash tools/deploy/pack-images.sh --changed             # 产出 dist-images/blog-images-<时间戳>.tar.gz

# ② 传上去
scp dist-images/blog-images-*.tar.gz <用户>@<服务器>:/opt/blog/

# ③ 服务器
cd /opt/blog
gunzip -c blog-images-*.tar.gz | docker load
docker compose up -d            # 只重建有变化的容器，其余不动

# ④ 验收（别只看"容器起来了"）
docker compose ps                              # 四个都 healthy
curl -sI https://<你的域名> | head -3           # 200
docker compose logs nginx --tail 20            # 真实访客 IP（realip 是否仍生效）
```

> 💡 **不想在本地和服务器各落一个中转文件**，可以一条流水线推过去
> （`pack-images.sh` 跑完会把这行命令打印出来）：
>
> ```bash
> docker save blog-nginx:local blog-nginx:<时间戳> | gzip | ssh <用户>@<服务器> 'gunzip | docker load'
> ```
>
> 只省磁盘、不省传输量 —— 真正的传输量由 §3.1 决定。

**回滚**见 §5；**改了表结构**要额外注意 —— 回滚镜像**不会**回滚数据库结构。

---

## 5. 回滚

镜像打了时间戳 tag（`blog-webapi:<时间戳>`），回滚就是把 compose 指回旧 tag：

```bash
# 看有哪些版本
docker images | grep blog-webapi

# 回滚（在 /opt/blog）
docker tag blog-webapi:<旧时间戳> blog-webapi:local
docker tag blog-nginx:<旧时间戳>  blog-nginx:local
docker compose up -d --force-recreate webapi nginx
```

> ⚠️ **数据库迁移是不可逆的那一半**：应用启动时会自动跑迁移（`Program.cs`），
> 而 EF 的 `Down()` 未必写得完整。所以：
> - **破坏性迁移（删列/改类型）上线前必须先备份**（`tools/backup/backup.sh`）
> - 回滚应用镜像**不会**回滚数据库结构——真要回退 schema，只能用备份还原
>
> 这条是"回滚预案"里最容易想当然的地方，见 `docs/05` §8.9。

## 6. HTTPS 与备份

### 6.1 宿主机放一层 Caddy 终结 TLS（推荐）

**概念与原理**见 `docs/05` §8.8。这里只讲怎么敲。选 Caddy 而不是 Nginx+certbot，
是因为证书的**申请、续期、HTTP→HTTPS 跳转全自动**，配置只有三行。

```bash
# ── ① 装 Caddy（官方源）──
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https curl
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
  | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
  | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update && sudo apt install -y caddy

# ── ② 写配置（把域名换成你自己的）──
sudo tee /etc/caddy/Caddyfile >/dev/null <<'EOF'
www.example.com {
	reverse_proxy 127.0.0.1:8080
}

example.com {
	redir https://www.example.com{uri} permanent
}
EOF

# ── ③ 放行端口（ufw 与云厂商安全组**两层都要**开）──
sudo ufw allow 80/tcp && sudo ufw allow 443/tcp

# ── ④ 启动并看证书申请过程 ──
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
journalctl -u caddy -f        # 成功会打印 "certificate obtained successfully"
```

> ⚠️ **Let's Encrypt 要回连验证**：配置里写的每个域名都必须已经解析到这台服务器。
> apex 域名（`example.com`）没解析就把那一段删掉，只留 `www`。

### 6.2 ⚠️ 顺序很重要：先让 `realip` 生效，再关 8080

宿主机这一层会让容器 nginx 看到的对端变成宿主机，**「每 IP 限流」会静默退化成
「全站共享一个桶」**（完整推导见 `docs/05` §8.8.1）。`deploy/nginx.conf` 里已经加了
4 行 `realip` 修复，但它**在镜像里**——所以要重建 nginx 镜像并传上去
（只有这一个镜像变了，不用重传 567 MB）：

```bash
# ── 本地仓库根目录 ──
docker compose build nginx
STAMP=$(date +%Y%m%d-%H%M%S)
docker tag blog-nginx:local "blog-nginx:$STAMP"
docker save blog-nginx:local "blog-nginx:$STAMP" | gzip > "dist-images/nginx-$STAMP.tar.gz"
scp "dist-images/nginx-$STAMP.tar.gz" <用户>@<服务器>:/opt/blog/

# ── 服务器 ──
cd /opt/blog
gunzip -c "nginx-$STAMP.tar.gz" | docker load
docker compose up -d --force-recreate nginx

# ⚠️ 验收：日志里必须是**真实访客 IP**，不是 127.0.0.1 / 172.x
docker compose logs nginx | tail -20
```

确认上面这一条通过之后，**再**收掉明文旁路：

```bash
# ① .env 里改回回环
HTTP_PORT=127.0.0.1:8080
# ② 重建容器让它重新读端口绑定
docker compose up -d nginx
# ③ 腾讯云控制台关掉安全组的 8080
```

**最终验收**：

```bash
curl -sI https://www.example.com | head -3                  # 期望 200
curl -sI http://www.example.com | grep -i '^location'       # 期望 301 → https
curl -s 'https://www.example.com/api/posts/search?keyword=博客&page=1&pageSize=1' | head -c 120
```

### 6.3 备份

⚠️ **本项目不做定时备份** —— 个人项目，已在 `docs/06` §4 登记为「明确不做」。
`tools/backup/` 里的脚本保留为**手工手段**：改动表结构之前跑一次，
否则一旦迁移写错就没有退路（回滚镜像**不会**回滚表结构）。

原理、恢复演练流程，以及「哪天改主意要开定时备份」的 crontab，
见 `docs/05` §8.9 与 `tools/backup/README.md`。

---

## 7. 脚本清单

| 脚本 | 在哪跑 | 做什么 |
|---|---|---|
| `pack-images.sh` | 本地仓库根目录 | 按服务构建镜像 + 打时间戳 tag + 打包成 tar.gz（默认只打应用两个） |
| `server-init.sh` | 服务器部署目录 | 环境预检（compose/磁盘/swap）+ 装载镜像 + 生成 `.env` + 起栈 + 八步验收 |
| `server-update.sh` | 服务器部署目录 | **日常更新**：从 GHCR 拉指定 sha 的镜像并切换（§4.5.1） |
| `what-to-do.sh` | 本地（CI 也调它） | 算出「这次改动除了更新镜像还需要做什么」（§4.5.1 那张表） |
| `../backup/backup.sh` | 任意（compose 栈所在机器） | `pg_dump` 备份 |
| `../backup/restore.sh` | 同上 | 还原到 `_restore` 库并核对 |
