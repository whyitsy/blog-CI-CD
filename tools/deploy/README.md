# 部署脚本

> 这一目录放**可执行的上线脚本**。方法论与背景在 [`../../docs/05-运维与部署手册.md`](../../docs/05-运维与部署手册.md) §8，
> 这里只讲"照着敲什么"。
>
> **状态**：脚本已就位，**尚未在真实服务器上跑过**（2026-09-14）。第一次上线时如果哪一步卡住，
> 把原始输出贴回来，我们把它变成这个目录里的下一条经验。

---

## 0. 先买服务器

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

| 方案 | 配置 | 参考价 | 判断 |
|---|---|---|---|
| **A（推荐）** | 阿里云香港轻量 2 核 2G / 60 GB / **30 Mbps** | ≈408 元/年 | 免备案；本地构建镜像传过去，2 GB 够跑 |
| B | 阿里云香港轻量 2 核 4G / 80 GB / 30 Mbps | ≈804 元/年 | 也可以直接在服务器上构建，最省事 |
| C | 腾讯云 2 核 2G **1 Mbps** | 199 元/年 | ❌ 1 Mbps ≈ 128 KB/s，带图片的博客不可用 |

**为什么优先香港节点**：内地节点用域名**必须 ICP 备案**（约 1~2 周）；香港免备案，
内地访问 70~120 ms，个人博客可接受。

> 价格来自公开测评文章（[阿里云香港轻量测评](https://developer.aliyun.com/article/1759576)、
> [2026 云服务器比价](https://cloud.tencent.com/developer/article/2659406)），**活动价随时变，以官网为准**。

**买的时候选**：Ubuntu 24.04 LTS（不要选带宝塔/WordPress 的应用镜像，那会占端口、加变量）。

---

## 1. 首次登录加固

```bash
# 本地：把公钥传上去（不要用密码登录跑线上）
ssh-copy-id <用户>@<服务器>

# 服务器：确认公钥能登录之后，再关掉密码登录
sudo sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
sudo systemctl restart ssh

# 防火墙：只放 22 / 80 / 443
sudo ufw allow 22/tcp && sudo ufw allow 80/tcp && sudo ufw allow 443/tcp
sudo ufw --force enable

# ⚠️ 云厂商控制台还有一层防火墙（安全组），两边都要放行
```

## 2. 装 Docker

```bash
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker "$USER" && newgrp docker
docker compose version    # 需要 v2（compose 插件），不是老的 docker-compose
```

## 3. 本地：构建并打包镜像

```bash
# 在仓库根目录
bash tools/deploy/pack-images.sh
# 产出 dist-images/blog-images-<时间戳>.tar.gz（约 500 MB）
```

## 4. 传到服务器并启动

```bash
scp dist-images/blog-images-*.tar.gz docker-compose.yml <用户>@<服务器>:/opt/blog/
# ⚠️ .env 单独传（它含真实密钥，绝不入库）；本地还没有就先在服务器上生成
ssh <用户>@<服务器>
cd /opt/blog && bash server-init.sh blog-images-<时间戳>.tar.gz
```

`server-init.sh` 会做七件事，每件都有验收输出：
装载镜像 → 生成 `.env`（`openssl rand` 现生成密钥）→ 起栈 → 等健康 →
验四容器 healthy → 验接口与**中文检索**（这条最能证明 PG 镜像搬对了）→ 打印上线后必做的两件事。

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

## 6. HTTPS 与定时备份

- **HTTPS**：`docs/05` §8.8 给了两个方案，推荐**宿主机再放一层 Nginx 终结 TLS**，
  容器栈的 `8080` 只监听 `127.0.0.1`（`server-init.sh` 生成的 `.env` 已经是这样）。
- **定时备份**：`tools/backup/README.md` §5 有 crontab 示例。
  ⚠️ **备份文件不要和数据库放同一台机器**，并且**每季度做一次恢复演练**。

---

## 7. 脚本清单

| 脚本 | 在哪跑 | 做什么 |
|---|---|---|
| `pack-images.sh` | 本地仓库根目录 | 构建三个镜像 + 打时间戳 tag + 打包成 tar.gz |
| `server-init.sh` | 服务器部署目录 | 装载镜像 + 生成 `.env` + 起栈 + 七步验收 |
| `../backup/backup.sh` | 任意（compose 栈所在机器） | `pg_dump` 备份 |
| `../backup/restore.sh` | 同上 | 还原到 `_restore` 库并核对 |
