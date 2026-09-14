#!/usr/bin/env bash
# 服务器：首次上线。装载镜像 → 生成 .env → 起栈 → 逐项验收。
#
# 前提（缺一不可，先自己确认）：
#   ① 仓库的 docker-compose.yml、deploy/nginx.conf 已传到本目录
#   ② pack-images.sh 产出的 tar.gz 已传到本目录
#   ③ 已装好 docker 与 compose 插件
#
# 用法（在服务器上的部署目录，如 /opt/blog）：
#   bash server-init.sh blog-images-20260914-120000.tar.gz
#
# 第 1 步是环境预检：2 GB 机器上「没有 swap」是 PG 被 OOM Kill 的头号原因，
# 所以这台机器上 swap 不是可选项。预检会自己建好它。
set -euo pipefail

TAR="${1:?用法: bash server-init.sh <镜像包.tar.gz>}"
[ -f "$TAR" ] || { echo "❌ 找不到镜像包：$TAR" >&2; exit 1; }
[ -f docker-compose.yml ] || { echo "❌ 当前目录没有 docker-compose.yml" >&2; exit 1; }

step() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }
ok()   { printf '  ✅ %s\n' "$1"; }
bad()  { printf '  ❌ %s\n' "$1" >&2; exit 1; }

step "1/8 环境预检（compose / 磁盘 / swap）"

docker compose version >/dev/null 2>&1 \
  || bad "docker compose 不可用 —— 需要 v2 插件（不是老的 docker-compose）"
ok "Docker Compose $(docker compose version --short 2>/dev/null || echo v2)"

FREE_GB=$(df -BG --output=avail . | tail -1 | tr -dc '0-9')
if [ "$FREE_GB" -lt 15 ]; then
  bad "部署目录可用磁盘只有 ${FREE_GB} GB（需要 ≥15 GB）：镜像解包约 2 GB，加上 pgdata / 日志 / 上传图片会持续增长"
fi
ok "可用磁盘 ${FREE_GB} GB"

# ── swap：2 GB 机器上这是**必需项**，不是优化 ──────────────────────────────
# PG 做 VACUUM、大排序或建索引时会短暂冲高；没有 swap 时内核只能 OOM Kill，
# 而被挑中的往往正是 PG。症状是「博客突然 502，事后发现 pgsql 容器重启过」——
# 不知情的话很容易去查应用代码。
MEM_MB=$(awk '/^MemTotal:/{printf "%d", $2/1024}' /proc/meminfo)
SWAP_MB=$(awk '/^SwapTotal:/{printf "%d", $2/1024}' /proc/meminfo)
printf '  内存 %s MB / swap %s MB\n' "$MEM_MB" "$SWAP_MB"

if [ "$MEM_MB" -le 3072 ] && [ "$SWAP_MB" -lt 1024 ]; then
  echo "  ⚠️  内存 ${MEM_MB} MB 且 swap 不足 1 GB —— 正在创建 2 GB swap"
  SUDO=""
  [ "$(id -u)" -ne 0 ] && SUDO="sudo"

  if [ ! -f /swapfile ]; then
    # fallocate 在部分文件系统上不支持，退回 dd
    $SUDO fallocate -l 2G /swapfile 2>/dev/null || $SUDO dd if=/dev/zero of=/swapfile bs=1M count=2048 status=none
    $SUDO chmod 600 /swapfile
    $SUDO mkswap /swapfile >/dev/null
  fi
  $SUDO swapon /swapfile 2>/dev/null || true

  # ⚠️ 写进 fstab，否则**重启后就没了** —— 这是最容易漏的一步，
  #    漏了之后「上线当天好好的，过几天重启一次就开始莫名 502」。
  if ! grep -q '^/swapfile' /etc/fstab; then
    echo '/swapfile none swap sw 0 0' | $SUDO tee -a /etc/fstab >/dev/null
  fi

  # swappiness 默认 60 太激进：内存还有富余时就开始换出，PG 的延迟会抖。
  # 10 = 「只在真的快满了才用 swap」，这正好是我们要的兜底语义。
  if [ "$(cat /proc/sys/vm/swappiness)" != "10" ]; then
    echo 'vm.swappiness=10' | $SUDO tee /etc/sysctl.d/99-blog.conf >/dev/null
    $SUDO sysctl -p /etc/sysctl.d/99-blog.conf >/dev/null
  fi

  SWAP_MB=$(awk '/^SwapTotal:/{printf "%d", $2/1024}' /proc/meminfo)
  if [ "$SWAP_MB" -ge 1024 ]; then
    ok "swap 已就绪：${SWAP_MB} MB（已写入 /etc/fstab，重启后仍生效）"
  else
    bad "swap 创建失败（当前 ${SWAP_MB} MB）—— 手工处理办法见 tools/deploy/README.md §1.2，处理完重跑本脚本"
  fi
else
  ok "swap 充足（${SWAP_MB} MB），跳过"
fi

step "2/8 装载镜像"
gunzip -c "$TAR" | docker load
ok "镜像已装载"

step "3/8 生成 .env（密钥现生成，绝不复用开发机的）"
if [ -f .env ]; then
  ok ".env 已存在，跳过（如需重建请先手工备份并删除）"
else
  # umask 077：密钥文件不给同组/其他用户读
  ( umask 077; cat > .env <<EOF
# 由 tools/deploy/server-init.sh 生成于 $(date '+%F %T')
# ⚠️ 本文件含真实密钥：不要入库、不要贴进聊天、不要放进截图
JWT_SIGNING_KEY=$(openssl rand -base64 48 | tr -d '\n')
POSTGRES_PASSWORD=$(openssl rand -base64 24 | tr -d '\n' | tr '/+' '_-')
POSTGRES_USER=kky
POSTGRES_DB=blog_stage2
JWT_ISSUER=blog-api
JWT_AUDIENCE=blog-frontend
# ⚠️ 只监听回环：由宿主机上的 Nginx/Caddy 终结 TLS 再转发（见 docs/05 §8.7）
HTTP_PORT=127.0.0.1:8080
EOF
  )
  ok ".env 已生成（权限 $(stat -c%a .env 2>/dev/null || stat -f%Lp .env)）"
fi

step "4/8 启动容器栈"
docker compose up -d
sleep 5
docker compose ps

step "5/8 等待后端健康（首次启动会跑数据库迁移，可能要 40 秒以上）"
for i in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:8080/health >/dev/null 2>&1; then
    ok "后端健康（第 ${i} 次探测）"
    break
  fi
  [ "$i" -eq 30 ] && { echo "--- 后端日志末尾 ---"; docker compose logs --tail=40 webapi; bad "后端 150 秒内未健康"; }
  sleep 5
done

step "6/8 验收：四个容器都 healthy，且内存上限真的生效"
UNHEALTHY=$(docker compose ps --format '{{.Service}} {{.Status}}' | grep -v 'healthy' || true)
[ -z "$UNHEALTHY" ] && ok "四个服务全部 healthy" || { echo "$UNHEALTHY"; bad "有服务不是 healthy"; }

# ⚠️ 「配置里写了」和「内核真的限制了」是两件事。
# deploy.resources.limits 在 swarm 之外是否生效，各版本 Compose 行为并不一致
# （已知 reservations 在非 swarm 下会被忽略，见 docker/compose#10046）。
# 而没有上限时 .NET 按**宿主机总内存**算 GC 堆硬上限（默认 75%）——
# 2 GB 机器上等于给应用发了 1.5 GB 的许可，PG 随时可能被 OOM Kill。
# 所以这里查的是内核看到的实际值，不是 compose 文件里写了什么。
WEBAPI_ID=$(docker compose ps -q webapi)
LIMIT=$(docker inspect -f '{{.HostConfig.Memory}}' "$WEBAPI_ID" 2>/dev/null || echo 0)
if [ "${LIMIT:-0}" -eq 0 ]; then
  bad "webapi 的内存上限没生效（HostConfig.Memory=0）。
      当前 Compose 版本忽略了 deploy.resources.limits。
      请在 docker-compose.yml 的 webapi 服务下补一行同样数值的 mem_limit: 768m，再重跑本脚本。
      （两个都留着没关系：compose-spec 要求二者一致，不冲突。）"
fi
ok "webapi 内存上限已生效：$((LIMIT / 1024 / 1024)) MB（内核实际值）"

step "7/8 验收：接口与中文检索"
curl -fsS "http://127.0.0.1:8080/api/site/config" | head -c 200; echo
# 中文全文检索依赖 zhparser —— 这条最能证明"镜像搬对了"
curl -fsS "http://127.0.0.1:8080/api/posts/search?keyword=%E5%8D%9A%E5%AE%A2&page=1&pageSize=1" >/dev/null \
  && ok "全文检索可用（zhparser 生效）" \
  || bad "全文检索失败 —— 多半是 PG 镜像不是 blog-postgres-zhparser:18"
# 统一响应体：缺必填参数应返回 4001 而不是 ProblemDetails
curl -sS "http://127.0.0.1:8080/api/posts/search" | grep -q '"code":4001' \
  && ok "统一响应体生效" \
  || bad "缺少必填参数时未返回统一响应体"

step "8/8 ⚠️ 上线后必须立刻做的两件事"
cat <<'EOF'
  1) 改管理员密码
     仓库是 public，种子口令 admin@example.com / Admin@12345 是**公开已知**的。
     登录后台 → 账号管理 → 重置 admin 的密码，然后用旧密码再登录一次，
     必须失败才算改成功（见 docs/05 §8.3）。

  2) 配 HTTPS
     二选一，见 docs/05 §8.8：宿主机 Nginx/Caddy 终结 TLS（推荐），
     或把证书挂进 nginx 容器。

  之后：设好定时备份（tools/backup/README.md §5），并**做一次恢复演练**。
EOF
ok "首次上线流程结束"
