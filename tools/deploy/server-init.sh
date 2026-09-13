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
set -euo pipefail

TAR="${1:?用法: bash server-init.sh <镜像包.tar.gz>}"
[ -f "$TAR" ] || { echo "❌ 找不到镜像包：$TAR" >&2; exit 1; }
[ -f docker-compose.yml ] || { echo "❌ 当前目录没有 docker-compose.yml" >&2; exit 1; }

step() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }
ok()   { printf '  ✅ %s\n' "$1"; }
bad()  { printf '  ❌ %s\n' "$1" >&2; exit 1; }

step "1/7 装载镜像"
gunzip -c "$TAR" | docker load
ok "镜像已装载"

step "2/7 生成 .env（密钥现生成，绝不复用开发机的）"
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

step "3/7 启动容器栈"
docker compose up -d
sleep 5
docker compose ps

step "4/7 等待后端健康（首次启动会跑数据库迁移，可能要 40 秒以上）"
for i in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:8080/health >/dev/null 2>&1; then
    ok "后端健康（第 ${i} 次探测）"
    break
  fi
  [ "$i" -eq 30 ] && { echo "--- 后端日志末尾 ---"; docker compose logs --tail=40 webapi; bad "后端 150 秒内未健康"; }
  sleep 5
done

step "5/7 验收：四个容器都 healthy"
UNHEALTHY=$(docker compose ps --format '{{.Service}} {{.Status}}' | grep -v 'healthy' || true)
[ -z "$UNHEALTHY" ] && ok "四个服务全部 healthy" || { echo "$UNHEALTHY"; bad "有服务不是 healthy"; }

step "6/7 验收：接口与中文检索"
curl -fsS "http://127.0.0.1:8080/api/site/config" | head -c 200; echo
# 中文全文检索依赖 zhparser —— 这条最能证明"镜像搬对了"
curl -fsS "http://127.0.0.1:8080/api/posts/search?keyword=%E5%8D%9A%E5%AE%A2&page=1&pageSize=1" >/dev/null \
  && ok "全文检索可用（zhparser 生效）" \
  || bad "全文检索失败 —— 多半是 PG 镜像不是 blog-postgres-zhparser:18"
# 统一响应体：缺必填参数应返回 4001 而不是 ProblemDetails
curl -sS "http://127.0.0.1:8080/api/posts/search" | grep -q '"code":4001' \
  && ok "统一响应体生效" \
  || bad "缺少必填参数时未返回统一响应体"

step "7/7 ⚠️ 上线后必须立刻做的两件事"
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
