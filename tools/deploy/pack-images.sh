#!/usr/bin/env bash
# 本地：构建三个镜像并打包成一个 tar.gz，供传到服务器。
#
# 为什么在本地构建而不是在服务器上构建：
#   ① 服务器只需要跑容器，不需要 .NET SDK 与 Node（省 2~3 GB 磁盘与 2 GB 内存）
#   ② 构建产物与开发机完全一致 —— 少一个"服务器上构建出来不一样"的变量
#   ③ 2 核 2G 的机器上跑 `dotnet publish` + `vite build` 有 OOM 风险
#
# 代价：每次上线要传约 500 MB（PG 镜像 1.98 GB 只在第一次传，之后就复用它）。
#
# 用法（在仓库根目录）：
#   bash tools/deploy/pack-images.sh [输出目录]
set -euo pipefail

OUT_DIR="${1:-./dist-images}"
STAMP="$(date +%Y%m%d-%H%M%S)"
TAR="${OUT_DIR}/blog-images-${STAMP}.tar.gz"

PG_IMAGE="blog-postgres-zhparser:18"
WEBAPI_IMAGE="blog-webapi:local"
NGINX_IMAGE="blog-nginx:local"

mkdir -p "$OUT_DIR"

echo "==> 构建镜像（compose build）"
docker compose build

echo
echo "==> 核对三个镜像都在"
for img in "$PG_IMAGE" "$WEBAPI_IMAGE" "$NGINX_IMAGE"; do
  if ! docker image inspect "$img" >/dev/null 2>&1; then
    echo "❌ 镜像不存在：$img" >&2
    exit 1
  fi
  printf '  %-32s %s\n' "$img" "$(docker image inspect -f '{{.Size}}' "$img" | awk '{printf "%.0f MB", $1/1024/1024}')"
done

echo
echo "==> 打包到 ${TAR}"
# 同时打上时间戳 tag：服务器上可以据此回滚到上一个版本（见 tools/deploy/README.md §5）
docker tag "$WEBAPI_IMAGE" "blog-webapi:${STAMP}"
docker tag "$NGINX_IMAGE" "blog-nginx:${STAMP}"

docker save "$PG_IMAGE" "$WEBAPI_IMAGE" "$NGINX_IMAGE" \
            "blog-webapi:${STAMP}" "blog-nginx:${STAMP}" | gzip > "$TAR"

SIZE=$(stat -c%s "$TAR" 2>/dev/null || stat -f%z "$TAR")
if [ "$SIZE" -lt 1048576 ]; then
  echo "❌ 打包文件只有 ${SIZE} 字节，判定为失败" >&2
  exit 1
fi
echo "✅ 完成：${TAR}（$(echo "$SIZE" | awk '{printf "%.0f MB", $1/1024/1024}')）"

echo
echo "下一步：把镜像与部署文件传到服务器（在仓库根目录执行）"
echo "  scp ${TAR} docker-compose.yml <用户>@<服务器>:/opt/blog/"
echo
echo "  ⚠️ 只需要 docker-compose.yml —— 镜像已经装好了，服务器不构建，"
echo "     所以不需要源码，也不需要 deploy/ 下的 Dockerfile（那两行 build: 只有构建时才读）。"
echo
echo "  ⚠️ .env 要单独传，且绝不要经过 git："
echo "     scp .env <用户>@<服务器>:/opt/blog/.env"
echo
echo "  镜像 tag ${STAMP} 已写入。回滚时用它，见 tools/deploy/README.md §5。"
