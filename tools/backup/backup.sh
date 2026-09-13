#!/usr/bin/env bash
# PostgreSQL 逻辑备份。在仓库根目录执行：bash tools/backup/backup.sh [输出目录]
#
# 为什么用 pg_dump 而不是直接拷数据卷：
#   卷是 PostgreSQL 的内部格式，跨版本/跨架构不保证可用；
#   pg_dump 产出的是 SQL，能读、能改、能在任何 PG 上还原。
#
# 为什么必须配合本目录的 README 做「恢复演练」：
#   备份脚本成功 ≠ 备份可用。没还原过的备份等于没有备份。
set -euo pipefail

OUT_DIR="${1:-./backups}"
STAMP="$(date +%Y%m%d-%H%M%S)"
DB="${POSTGRES_DB:-blog_stage2}"
USER="${POSTGRES_USER:-kky}"
FILE="${OUT_DIR}/blog_${DB}_${STAMP}.sql.gz"

mkdir -p "$OUT_DIR"

echo "==> 导出 ${DB} 到 ${FILE}"

# -T 关掉伪终端：不加的话 gzip 会写进 tty，拿到的是空文件。
docker compose exec -T pgsql pg_dump -U "$USER" -d "$DB" --no-owner --no-privileges \
  | gzip > "$FILE"

# 校验：小于 1KB 基本可以断定是空转。
# 为什么需要这一步：`docker compose exec` 失败时，管道里 gzip 仍会成功退出，
# 于是 set -e 不会触发，脚本会"成功地"产出一个空备份 —— 这是最危险的一种失败。
SIZE=$(stat -c%s "$FILE" 2>/dev/null || stat -f%z "$FILE")
if [ "$SIZE" -lt 1024 ]; then
  echo "❌ 备份文件只有 ${SIZE} 字节，判定为失败：$FILE" >&2
  exit 1
fi

echo "✅ 完成：$FILE（${SIZE} 字节）"
echo
echo "⚠️ 备份没验证过 = 没有备份。请定期跑一次还原演练，见 tools/backup/README.md"
