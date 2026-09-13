#!/usr/bin/env bash
# 从备份还原。在仓库根目录执行：bash tools/backup/restore.sh <备份文件> [目标库名]
#
# ⚠️ 这会**覆盖**目标库的现有数据。
#    因此默认还原到 <库名>_restore 供核对，确认无误后再手工改名 ——
#    还原演练绝不该碰正式库。
set -euo pipefail

FILE="${1:?用法: bash tools/backup/restore.sh <备份文件> [目标库名]}"
SRC_DB="${POSTGRES_DB:-blog_stage2}"
TARGET_DB="${2:-${SRC_DB}_restore}"
USER="${POSTGRES_USER:-kky}"

[ -f "$FILE" ] || { echo "❌ 找不到备份文件：$FILE" >&2; exit 1; }

echo "==> 目标库：${TARGET_DB}（源库名：${SRC_DB}）"

echo "==> 重建目标库（先删后建，确保是干净还原）"
docker compose exec -T pgsql psql -U "$USER" -d postgres -v ON_ERROR_STOP=1 \
  -c "DROP DATABASE IF EXISTS \"${TARGET_DB}\";" \
  -c "CREATE DATABASE \"${TARGET_DB}\" OWNER \"${USER}\";"

echo "==> 灌入 ${FILE}"
gunzip -c "$FILE" | docker compose exec -T pgsql psql -U "$USER" -d "$TARGET_DB" \
  -v ON_ERROR_STOP=1 > /dev/null

echo "==> 核对还原结果"
docker compose exec -T pgsql psql -U "$USER" -d "$TARGET_DB" -tAc \
  "select 'Posts=' || count(*) from \"Posts\";"
docker compose exec -T pgsql psql -U "$USER" -d "$TARGET_DB" -tAc \
  "select 'Users=' || count(*) from \"Users\";"
docker compose exec -T pgsql psql -U "$USER" -d "$TARGET_DB" -tAc \
  "select 'zhparser 扩展=' || count(*) from pg_extension where extname='zhparser';"
docker compose exec -T pgsql psql -U "$USER" -d "$TARGET_DB" -tAc \
  "select 'chinese 检索配置=' || count(*) from pg_ts_config where cfgname='chinese';"

echo
echo "✅ 已还原到 ${TARGET_DB}。"
echo "   核对无误后改为正式库（⚠️ 会覆盖正式库）："
echo "     docker compose exec -T pgsql psql -U ${USER} -d postgres \\"
echo "       -c 'DROP DATABASE \"${SRC_DB}\";' \\"
echo "       -c 'ALTER DATABASE \"${TARGET_DB}\" RENAME TO \"${SRC_DB}\";'"
echo "   演练完毕后清理："
echo "     docker compose exec -T pgsql psql -U ${USER} -d postgres -c 'DROP DATABASE \"${TARGET_DB}\";'"
