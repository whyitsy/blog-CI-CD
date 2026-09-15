#!/usr/bin/env bash
# 算出「这次改动除了更新镜像，还需要在服务器上做什么」，输出一段 markdown。
#
# 为什么需要它：
#   GHCR 里**只有 `webapi` / `nginx` 两个镜像**。
#   `docker-compose.yml`、PG 镜像、`tools/` 下的脚本**都不在里面** ——
#   改了那些却只跑 `server-update.sh`，会出现「镜像换了、配置没换」，
#   表现为某个功能不生效而**不报错**。这正是本项目一直在防的那类静默失败。
#
# 两个地方都调用它，保证只有一个真相来源：
#   ① CI 的 publish 作业 → 结论直接写进**作业摘要**（你本来就去那儿抄 sha，
#      答案出现在同一个地方最不容易漏）
#   ② 你自己 → `bash tools/deploy/what-to-do.sh origin/main HEAD`
#
# 用法：
#   bash tools/deploy/what-to-do.sh <基准ref> <目标ref>
#   bash tools/deploy/what-to-do.sh origin/main HEAD
#   bash tools/deploy/what-to-do.sh <服务器上正跑的 sha> <新 sha>
#
# 退出码：永远是 0（这是**提示**，不是门禁 —— 判断不出来时它说"人工确认"，
#         而不是让 CI 变红）。
set -uo pipefail

BASE="${1:?用法: bash what-to-do.sh <基准ref> <目标ref>}"
TARGET="${2:?用法: bash what-to-do.sh <基准ref> <目标ref>}"

git rev-parse --verify --quiet "${BASE}^{commit}" >/dev/null \
  || { echo "（基准 '$BASE' 不是有效提交，跳过判断）"; exit 0; }
git rev-parse --verify --quiet "${TARGET}^{commit}" >/dev/null \
  || { echo "（目标 '$TARGET' 不是有效提交，跳过判断）"; exit 0; }

# ⚠️ 必须关掉 core.quotePath：本仓库有中文文件名，默认会被转义成 \350\277\220…
#    那样就匹配不上任何规则，全部落进"不认识"分支。
FILES="$(git -c core.quotePath=false diff --name-only "$BASE" "$TARGET" || true)"

NEED_COMPOSE=""; NEED_PG=""; NEED_REDIS=""; NEED_BACKUP=""; NEED_DEPLOY_SH=""; UNKNOWN=""

while IFS= read -r f; do
  [ -n "$f" ] || continue
  case "$f" in
    # ── 打进 webapi / nginx 镜像的：CI 已经推上去，无需额外动作 ──
    Blog.Backend/*|Blog.FrontEnd/*) ;;
    deploy/nginx.conf|deploy/nginx.Dockerfile|deploy/webapi.Dockerfile) ;;
    # ── 不在 GHCR 里的：需要人工动作 ──
    deploy/postgres-zhparser.Dockerfile|deploy/postgres-init/*) NEED_PG=1 ;;
    docker-compose.yml|docker-compose.prod.yml) NEED_COMPOSE=1 ;;
    tools/backup/*) NEED_BACKUP=1 ;;
    tools/deploy/*.sh) NEED_DEPLOY_SH=1 ;;
    # ── 服务器不需要的 ──
    tools/*) ;;                       # load / e2e 只在开发机跑
    docs/*|learn/*|archive/*|plan/*|.github/*) ;;
    *.md|LICENSE|.gitignore|.editorconfig|.dockerignore) ;;
    *) UNKNOWN="${UNKNOWN}${f}
" ;;
  esac
done <<< "$FILES"

# Redis 的版本写在 compose 的 image 行里：改了那一行就得把新镜像一起带上，
# 否则服务器会去 Docker Hub 拉一个你没测过的版本。
if git diff -U0 "$BASE" "$TARGET" -- docker-compose.yml 2>/dev/null \
     | grep -qE '^\+[^+].*image:[[:space:]]*redis'; then
  NEED_REDIS=1
fi

SHORT="$(git rev-parse --short "$TARGET" 2>/dev/null || echo "$TARGET")"

echo "### 服务器更新：本次还需要额外做什么"
echo

if [ -z "$NEED_COMPOSE$NEED_PG$NEED_REDIS$NEED_BACKUP$NEED_DEPLOY_SH$UNKNOWN" ]; then
  echo "✅ **什么都不用** —— 本次改动全在镜像里（CI 已推 GHCR）。一条命令即可："
  echo
  echo '```bash'
  echo "cd /opt/blog && bash tools/deploy/server-update.sh ${SHORT}"
  echo '```'
  exit 0
fi

echo "⚠️ **光跑 \`server-update.sh\` 不够**，本次改动还包含不在 GHCR 里的东西："
echo

if [ -n "$NEED_COMPOSE" ]; then
  cat <<'EOF'
- **`docker-compose.yml` / `docker-compose.prod.yml` 改了** —— 镜像里不含它。
  不传的话会出现「镜像换了、配置没换」，而且**不报错**。
  ```bash
  scp docker-compose.yml docker-compose.prod.yml <用户>@<服务器>:/opt/blog/
  ```
EOF
fi

if [ -n "$NEED_PG" ]; then
  cat <<'EOF'
- **PG 镜像（zhparser）改了** —— 它不在 GHCR 里（439 MB，一年变不了几次）。
  ```bash
  bash tools/deploy/pack-images.sh --only pgsql
  scp dist-images/blog-images-*.tar.gz <用户>@<服务器>:/opt/blog/
  # 服务器上：gunzip -c blog-images-*.tar.gz | docker load
  ```
EOF
fi

if [ -n "$NEED_REDIS" ]; then
  cat <<'EOF'
- **`docker-compose.yml` 里 Redis 的 `image:` 行改了** —— 新版本要一起带上，
  否则服务器会去 Docker Hub 拉一个**你没测过**的版本。
  ```bash
  bash tools/deploy/pack-images.sh --only redis
  scp dist-images/blog-images-*.tar.gz <用户>@<服务器>:/opt/blog/
  # 服务器上：gunzip -c blog-images-*.tar.gz | docker load
  ```
EOF
fi

if [ -n "$NEED_BACKUP" ]; then
  cat <<'EOF'
- **`tools/backup/` 改了** —— 服务器上那份也要更新。
  ```bash
  scp -r tools/backup <用户>@<服务器>:/opt/blog/tools/
  ```
EOF
fi

if [ -n "$NEED_DEPLOY_SH" ]; then
  cat <<'EOF'
- **`tools/deploy/*.sh` 改了** —— 服务器上那份也要更新。
  ```bash
  scp tools/deploy/*.sh <用户>@<服务器>:/opt/blog/tools/deploy/
  ```
EOF
fi

if [ -n "$UNKNOWN" ]; then
  echo "- ⚠️ **有不认识的改动路径，请人工确认服务器上需不需要对应动作：**"
  echo
  echo '  ```'
  printf '%s' "$UNKNOWN" | sed 's/^/  /'
  echo '  ```'
  echo
fi

echo "**然后照常更新镜像**："
echo
echo '```bash'
echo "cd /opt/blog && bash tools/deploy/server-update.sh ${SHORT}"
echo '```'
