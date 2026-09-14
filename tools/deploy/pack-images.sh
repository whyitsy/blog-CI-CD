#!/usr/bin/env bash
# 本地：构建镜像并打包成一个 tar.gz，供传到服务器。
#
# 为什么在本地构建而不是在服务器上构建：
#   ① 服务器只需要跑容器，不需要 .NET SDK 与 Node（省 2~3 GB 磁盘与 2 GB 内存）
#   ② 构建产物与开发机完全一致 —— 少一个"服务器上构建出来不一样"的变量
#   ③ 2 核 2G 的机器上跑 `dotnet publish` + `vite build` 有 OOM 风险
#
# ⚠️ **PG 镜像不要每次传。** 实测（2026-09-15，gzip 后的 tar.gz）：
#     全部四个（首次部署用 --all）      ≈590 MB
#     仅 nginx（前端改动）               25 MB   ← 23 倍差距
#     仅 webapi（后端改动）             103 MB
#     应用两个（webapi + nginx）        128 MB
#     PG 单独                          439 MB   ← 它一个季度也未必变一次
#     Redis 单独                        ≈18 MB   ← 官方镜像，不构建只 pull
#   所以默认只打应用镜像，PG / Redis 要用 --all 显式带。
set -euo pipefail

ALL_SERVICES=(webapi nginx pgsql redis)
DEFAULT_SERVICES=(webapi nginx)
PG_IMAGE="blog-postgres-zhparser:18"

step() { printf '\n\033[1m==> %s\033[0m\n' "$1"; }
ok()   { printf '  ✅ %s\n' "$1"; }
warn() { printf '  ⚠️  %s\n' "$1" >&2; }
die()  { printf '  ❌ %s\n' "$1" >&2; exit 1; }

usage() {
  cat <<'EOF'
用法（在仓库根目录）：
  bash tools/deploy/pack-images.sh                   # 应用镜像（webapi + nginx）—— 日常用这个
  bash tools/deploy/pack-images.sh --only nginx      # 只改了前端 → 只打 nginx（25 MB）
  bash tools/deploy/pack-images.sh --only webapi     # 只改了后端（103 MB）
  bash tools/deploy/pack-images.sh --all             # 四个都打（首次部署）
  bash tools/deploy/pack-images.sh --changed [<ref>] # 按 git 改动自动判断该打哪些
  bash tools/deploy/pack-images.sh --dry-run         # 只预览会打哪些，不构建
  bash tools/deploy/pack-images.sh --out <目录>      # 换输出目录（默认 ./dist-images）

改了什么 → 该打哪个（这张表就是 --changed 的依据，也建议你记住）：

  Blog.Backend/**                              → webapi
  Blog.FrontEnd/**                             → nginx    （前端产物打在 nginx 镜像里）
  deploy/nginx.conf  deploy/nginx.Dockerfile   → nginx
  deploy/webapi.Dockerfile                     → webapi
  deploy/postgres-zhparser.Dockerfile          → pgsql    （罕见）
  deploy/postgres-init/**                      → pgsql    （罕见）
  其它（tools/**、docs/**、根目录 README.md）   → 不用重建镜像
                                                 只是其中有些文件要 scp 过去（README §4）

  docker-compose.yml                           → 不用重建镜像
      ⚠️ **例外**：改了 `redis:` 下面那行 `image:` 时，
         要手动 `--only redis` 把新版本一起带上去 ——
         否则服务器会去 Docker Hub 拉，而那个版本你可能没测过。

  ⚠️ 不认识的路径会被当成"可能影响全部"，直接打三个（含 439 MB 的 PG）。
     这是刻意的：**少传一个镜像的失败方式是静默的** ——
     服务器上跑着旧镜像而你看不出来，正是本项目反复记录的那类问题。
EOF
}

# ── 参数解析 ────────────────────────────────────────────────────────────
MODE="default"
SERVICES=()
CHANGED_REF=""
OUT_DIR="./dist-images"
DRY_RUN=""

while [ $# -gt 0 ]; do
  case "$1" in
    -h|--help)  usage; exit 0 ;;
    --all)      MODE="all"; shift ;;
    --only)     MODE="only"; shift
                [ $# -gt 0 ] || die "--only 后面要跟服务名，例如 --only nginx,webapi"
                IFS=',' read -r -a SERVICES <<< "$1"; shift ;;
    --only=*)   MODE="only"; IFS=',' read -r -a SERVICES <<< "${1#--only=}"; shift ;;
    --changed)  MODE="changed"; shift
                if [ $# -gt 0 ] && [ "${1#-}" = "$1" ]; then CHANGED_REF="$1"; shift; fi ;;
    --dry-run)  DRY_RUN=1; shift ;;
    --out)      shift; OUT_DIR="${1:?--out 后面要跟目录}"; shift ;;
    -*)         die "未知参数：$1（用 --help 看用法）" ;;
    *)          MODE="only"; SERVICES+=("$1"); shift ;;
  esac
done

cd "$(dirname "$0")/../.."                     # 一律在仓库根目录干活
STAMP_FILE="${OUT_DIR}/.last-pack"

# ── --changed：按 git 改动判断该打哪些 ──────────────────────────────────
# ⚠️ 不认识路径时一律按"可能影响全部"处理：宁可多传 439 MB，不可少传一个镜像。
#    （"少传"的失败方式是**静默的**——服务器上跑着旧镜像而你看不出来，
#      正是本项目反复记录的那类问题。）
services_from_path() {
  case "$1" in
    Blog.Backend/*)                                       echo webapi ;;
    Blog.FrontEnd/*)                                      echo nginx ;;
    deploy/nginx.conf|deploy/nginx.Dockerfile)            echo nginx ;;
    deploy/webapi.Dockerfile)                             echo webapi ;;
    deploy/postgres-zhparser.Dockerfile|deploy/postgres-init/*) echo pgsql ;;
    docker-compose.yml|tools/*|docs/*|learn/*|archive/*|plan/*|.github/*) ;;  # 不影响镜像
    README.md|*.md|.gitignore|.editorconfig|LICENSE) ;;                       # 根目录杂项
    *)                                                    echo ALL ;;
  esac
}

if [ "$MODE" = "changed" ]; then
  if [ -z "$CHANGED_REF" ]; then
    if [ -f "$STAMP_FILE" ]; then
      CHANGED_REF="$(cat "$STAMP_FILE")"
      step "按改动判断（基准：上次打包的提交 $(git rev-parse --short "$CHANGED_REF" 2>/dev/null || echo "$CHANGED_REF")）"
    else
      step "按改动判断"
      warn "没有上次打包的记录 → 保守地打全部三个"
      SERVICES=("${ALL_SERVICES[@]}")
    fi
  else
    step "按改动判断（基准：$CHANGED_REF）"
  fi

  if [ -n "$CHANGED_REF" ]; then
    git rev-parse --verify --quiet "$CHANGED_REF^{commit}" >/dev/null \
      || die "基准 '$CHANGED_REF' 不是有效提交"

    # ⚠️ 必须关掉 core.quotePath：本仓库有中文文件名，默认会被转义成 \350\277\220…
    #    那样就匹配不上任何规则，全部落到"不认识"分支 → 每次都打全部。
    files="$(
      {
        git -c core.quotePath=false diff --name-only "$CHANGED_REF" HEAD
        git -c core.quotePath=false status --porcelain | cut -c4- | sed 's/.* -> //'
      } | sort -u | grep -v '^$' || true
    )"

    total=$(printf '%s\n' "$files" | grep -c . || true)
    if [ "$total" -eq 0 ]; then
      warn "相对 ${CHANGED_REF} 没有任何改动 → 退回默认的应用镜像（打空包等于什么都没做）"
      SERVICES=("${DEFAULT_SERVICES[@]}")
    else
      picked=""
      while IFS= read -r f; do
        [ -n "$f" ] || continue
        for s in $(services_from_path "$f"); do
          if [ "$s" = "ALL" ]; then
            warn "不认识的路径：$f → 保守地打全部三个"
            picked="${ALL_SERVICES[*]}"
            break 2
          fi
          picked="$picked $s"
        done
      done <<< "$files"

      # shellcheck disable=SC2206
      SERVICES=($(printf '%s\n' $picked | sort -u | grep -v '^$'))

      printf '    改动文件 %s 个，例如：\n' "$total"
      printf '%s\n' "$files" | head -8 | sed 's/^/      /'
      [ "$total" -gt 8 ] && echo "      …（共 ${total} 个）"

      if [ "${#SERVICES[@]}" -eq 0 ]; then
        ok "改动都不影响镜像（只改了文档 / 脚本 / compose）—— 没有镜像需要重建"
        echo
        echo "如果这些改动要上服务器，直接 scp 即可（见 tools/deploy/README.md §4）："
        echo "  scp docker-compose.yml tools/deploy/server-init.sh <用户>@<服务器>:/opt/blog/"
        exit 0
      fi
    fi
  fi
fi

[ "$MODE" = "all" ] && SERVICES=("${ALL_SERVICES[@]}")
[ "${#SERVICES[@]}" -eq 0 ] && SERVICES=("${DEFAULT_SERVICES[@]}")

for s in "${SERVICES[@]}"; do
  printf '%s\n' "${ALL_SERVICES[@]}" | grep -qx "$s" \
    || die "未知服务 '$s'（可选：${ALL_SERVICES[*]}）"
done

if [ -n "$DRY_RUN" ]; then
  step "只预览，不构建"
  ok "会构建并打包：${SERVICES[*]}"
  exit 0
fi

mkdir -p "$OUT_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"
TAR="${OUT_DIR}/blog-images-${STAMP}.tar.gz"

# ── 从 compose 读 redis 的 image，避免版本在两处写重（写重就会漂）──────────
redis_image() {
  local img
  img="$(awk '/^  redis:/{f=1;next} f&&/^  [a-z]/{f=0} f&&/^[[:space:]]+image:/{print $2;exit}' docker-compose.yml)"
  [ -n "$img" ] || die "没能从 docker-compose.yml 读出 redis 的 image"
  printf '%s' "$img"
}

# ── 构建（redis 是官方镜像，只 pull 不 build）────────────────────────────
BUILD_SERVICES=()
PULL_SERVICES=()
for s in "${SERVICES[@]}"; do
  if [ "$s" = "redis" ]; then PULL_SERVICES+=(redis); else BUILD_SERVICES+=( "$s" ); fi
done

if [ "${#BUILD_SERVICES[@]}" -gt 0 ]; then
  step "构建镜像：${BUILD_SERVICES[*]}"
  docker compose build "${BUILD_SERVICES[@]}"
fi
if [ "${#PULL_SERVICES[@]}" -gt 0 ]; then
  # Redis 没有 build 段 —— 它不需要本项目定制（对比 PG：必须自编译 zhparser）。
  # ⚠️ 但仍然要**打进包里**：否则服务器首次部署必须能连上 Docker Hub，
  #    那是个不受控的外部依赖（docs/05 §8.5 坑 B 记过 Docker Hub 不可达）。
  step "拉取官方镜像：${PULL_SERVICES[*]}（不构建，只 pull）"
  docker compose pull "${PULL_SERVICES[@]}"
fi

# ── 核对镜像存在 ────────────────────────────────────────────────────────
step "核对镜像"
SAVE_ARGS=()
for s in "${SERVICES[@]}"; do
  case "$s" in
    pgsql)  img="$PG_IMAGE" ;;
    webapi) img="blog-webapi:local" ;;
    nginx)  img="blog-nginx:local" ;;
    redis)  img="$(redis_image)" ;;
  esac
  docker image inspect "$img" >/dev/null 2>&1 || die "镜像不存在：$img"
  printf '  %-32s %s\n' "$img" \
    "$(docker image inspect -f '{{.Size}}' "$img" | awk '{printf "%.0f MB", $1/1024/1024}')"
  SAVE_ARGS+=("$img")
done

# ── 打时间戳 tag（回滚要用，见 README §5）───────────────────────────────
# 只给自建的两个镜像打：redis / pgsql 的版本写在 compose 里，回滚靠改那一行即可。
for s in "${SERVICES[@]}"; do
  case "$s" in
    webapi) docker tag blog-webapi:local "blog-webapi:${STAMP}"; SAVE_ARGS+=("blog-webapi:${STAMP}") ;;
    nginx)  docker tag blog-nginx:local  "blog-nginx:${STAMP}";  SAVE_ARGS+=("blog-nginx:${STAMP}") ;;
  esac
done

# ── 打包 ────────────────────────────────────────────────────────────────
step "打包到 ${TAR}"
docker save "${SAVE_ARGS[@]}" | gzip > "$TAR"

SIZE=$(stat -c%s "$TAR" 2>/dev/null || stat -f%z "$TAR")
[ "$SIZE" -ge 1048576 ] || die "打包文件只有 ${SIZE} 字节，判定为失败"
ok "完成：${TAR}（$(echo "$SIZE" | awk '{printf "%.0f MB", $1/1024/1024}')）"

git rev-parse HEAD > "$STAMP_FILE" 2>/dev/null || true

# ── 下一步 ──────────────────────────────────────────────────────────────
echo
echo "下一步："
printf '%s\n' "${SERVICES[@]}" | grep -qx pgsql || {
  echo "  ⚠️ 这个包里**不含 PG 镜像**。服务器上必须已经有 ${PG_IMAGE}；"
  echo "     首次部署请改用：bash tools/deploy/pack-images.sh --all"
}
echo "  scp ${TAR} docker-compose.yml <用户>@<服务器>:/opt/blog/"
echo "  # 服务器：cd /opt/blog && gunzip -c $(basename "$TAR") | docker load && docker compose up -d"
echo
echo "  💡 不想在本地和服务器各落一个中转文件，可以一条流水线搞定："
echo "     docker save ${SAVE_ARGS[*]} | gzip | ssh <用户>@<服务器> 'gunzip | docker load'"
echo
echo "  镜像 tag ${STAMP} 已写入。回滚时用它，见 tools/deploy/README.md §5。"
