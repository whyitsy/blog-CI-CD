#!/usr/bin/env bash
# 服务器：把栈更新到某个提交对应的镜像（从 GHCR 拉，**不传 tar.gz**）
#
# 和 pack-images.sh 的分工：
#   首次部署 / PG 变了  → 本地 pack-images.sh --all + scp（PG 439 MB，值得传一次）
#   日常更新应用        → 本脚本，只下 Docker 层（通常几 MB）
#
# 前提（一次性）：
#   ① 服务器上要有 docker-compose.prod.yml（从仓库 scp 过来）
#   ② .env 里要有一行 IMAGE_PREFIX，例如
#        IMAGE_PREFIX=ghcr.io/whyitsy/kky-blog
#      （镜像名 = ${IMAGE_PREFIX}-webapi / ${IMAGE_PREFIX}-nginx，tag 是提交 sha）
#   ③ GHCR 上的 package 已经设为 public（否则要 docker login ghcr.io）
#
# 用法（在 /opt/blog）：
#   bash tools/deploy/server-update.sh <提交 sha>
#   bash tools/deploy/server-update.sh <sha> --check     # 只拉不切，先看能不能拉到
#
# ⚠️ 不要用 `latest`：那是个会移动的标签，出事时说不清线上跑的是哪一版。
set -euo pipefail

SHA="${1:?用法: bash server-update.sh <提交 sha>   （sha 从 CI 的作业摘要里抄）}"
MODE="${2:-apply}"

step() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }
ok()   { printf '  ✅ %s\n' "$1"; }
die()  { printf '  ❌ %s\n' "$1" >&2; exit 1; }

[ -f .env ] || die "当前目录没有 .env"
[ -f docker-compose.yml ] || die "当前目录没有 docker-compose.yml"
[ -f docker-compose.prod.yml ] || die "缺少 docker-compose.prod.yml —— 从仓库 scp 一份过来"

# ⚠️ 不 source .env：那是可执行的 shell，而 .env 是**数据**。
#    用 grep 取单行，避免值里的特殊字符被当成命令执行。
PREFIX="$(grep -E '^IMAGE_PREFIX=' .env | tail -1 | cut -d= -f2- | tr -d '"'"'"' ' || true)"
[ -n "$PREFIX" ] || die ".env 里没有 IMAGE_PREFIX。加一行，例如：
       IMAGE_PREFIX=ghcr.io/<owner>/<repo>
     （owner/repo 就是 GitHub 上那个仓库的路径）"

COMPOSE=(docker compose -f docker-compose.yml -f docker-compose.prod.yml)
WEBAPI_IMAGE="${PREFIX}-webapi:${SHA}"
NGINX_IMAGE="${PREFIX}-nginx:${SHA}"

step "目标版本 ${SHA}"
printf '  %s\n  %s\n' "$WEBAPI_IMAGE" "$NGINX_IMAGE"

# 先拉，再改 .env —— 顺序不能反：
# 拉取失败（tag 不存在 / 没设 public / 网络不通）时 .env 还是旧值，栈不受影响。
step "拉取镜像"
if ! "${COMPOSE[@]}" pull webapi nginx; then
  die "拉取失败。三个最常见的原因：
       ① 这个 sha 的镜像还没推上去 —— 确认 CI 的 publish 作业跑绿了
       ② GHCR 上的 package 还是 private —— GitHub → Packages → Settings → 改成 public
          （或者在本机 docker login ghcr.io 之后再试）
       ③ 网络不通（服务器访问 ghcr.io）"
fi
ok "镜像已拉到本地"

if [ "$MODE" = "--check" ]; then
  ok "--check 模式：只拉不切，.env 未改动"
  exit 0
fi

# 备份 .env：回滚就是把备份覆盖回去
BACKUP=".env.bak.$(date +%Y%m%d-%H%M%S)"
cp .env "$BACKUP"

set_env() {
  if grep -qE "^$1=" .env; then
    sed -i "s|^$1=.*|$1=$2|" .env
  else
    printf '%s=%s\n' "$1" "$2" >> .env
  fi
}
set_env WEBAPI_IMAGE "$WEBAPI_IMAGE"
set_env NGINX_IMAGE  "$NGINX_IMAGE"
ok ".env 已更新（旧值备份在 ${BACKUP}）"

step "重建容器"
"${COMPOSE[@]}" up -d webapi nginx
# ⚠️ nginx 必须重启：它在启动时解析并缓存了上游 webapi 的 IP，
#    webapi 被重建后容器 IP 可能变化，nginx 不会自动重新解析。
"${COMPOSE[@]}" restart nginx
ok "容器已更新"

step "验收（不验不算完）"
"${COMPOSE[@]}" ps
printf '\n  健康检查：'
if curl -fsS http://127.0.0.1:8080/health >/dev/null; then
  ok "后端健康"
else
  die "后端不健康 —— 看日志：docker compose logs --tail=50 webapi"
fi

echo
echo "回滚到上一个版本："
echo "  cp ${BACKUP} .env && ${COMPOSE[*]} up -d webapi nginx && ${COMPOSE[*]} restart nginx"
echo
echo "⚠️ 回滚镜像**不会**回滚数据库结构。如果这次更新带了迁移，"
echo "   而迁移是破坏性的（删列 / 改类型），先看 docs/05 §8.9。"
