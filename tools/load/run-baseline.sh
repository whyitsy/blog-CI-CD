#!/usr/bin/env bash
# ============================================================================
# 本地基线（B1）：六个接口各跑一遍**恒定小载荷**，拿 p50 / p95 / p99
#
#   ./tools/load/run-baseline.sh                    # 10 RPS × 30s，含冷缓存探针
#   ./tools/load/run-baseline.sh --rate 20 --duration 60s
#   ./tools/load/run-baseline.sh --skip-cold        # 跳过冷缓存（默认会跑）
#
# 它做了什么（顺序不能乱）：
#   ① 记录环境规格     —— 没有 CPU 核数/内存/数据量/冷热状态，数字无法复现（learn/03 §11.1 纪律 3）
#   ② 等 /health 就绪  —— 坑 7：容器启动 ≠ 可连接
#   ③ 校验限流已关     —— 没关就**直接退出**：那种数字测的是限流器，不是应用（坑 1）
#   ④ 预热缓存         —— 热缓存组
#   ⑤ B0 冒烟          —— 冒烟不过，后面全部作废
#   ⑥ 六接口基线       —— 每个脚本自带 thresholds，跑完自动判 SLO
#   ⑦ 冷缓存探针       —— FLUSHDB 后单次请求，拿"最坏情况"的数字
#   ⑧ 汇总成 markdown  —— results/baseline-*.md，可直接贴进报告
#
# ⚠️ 关于 k6 的路径坑（本项目特有）：仓库路径含中文，k6 会把脚本路径当 URL 转义后
#    stat 一个不存在的文件并 panic。本脚本会自动在 /tmp 建一个**纯 ASCII 软链**再执行，
#    脚本本身仍然住在仓库里（版本控制不受影响）。
# ============================================================================

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
K6_DIR="tools/load/k6"
RAW_DIR="tools/load/results/raw"

RATE=10
DURATION=30s
# ⚠️ 写路径**不能用读路径的载荷**：POST /api/posts 是低并发、高成本的写操作，
#    真实个人博客一天也发不了几篇。用 10 RPS 压它等于把"写容量"当成"读容量"，
#    而且会顺手造出 300 篇垃圾文章。默认 0.5 RPS（2 秒一篇）。
WRITE_RATE=0.5
SKIP_COLD=0
COLD_ROUNDS=20

while [[ $# -gt 0 ]]; do
  case "$1" in
    --rate) RATE="$2"; shift 2 ;;
    --write-rate) WRITE_RATE="$2"; shift 2 ;;
    --duration) DURATION="$2"; shift 2 ;;
    --cold-rounds) COLD_ROUNDS="$2"; shift 2 ;;
    --skip-cold) SKIP_COLD=1; shift ;;
    -h|--help) sed -n '2,25p' "$0"; exit 0 ;;
    *) echo "未知参数：$1"; exit 2 ;;
  esac
done

# k6 只认 ASCII 路径：含非 ASCII 时用软链换一个入口
if printf '%s' "$REPO_ROOT" | LC_ALL=C grep -q '[^ -~]'; then
  ASCII_LINK="${LOADTEST_ASCII_LINK:-/tmp/blog-load-repo}"
  ln -sfn "$REPO_ROOT" "$ASCII_LINK"
  cd "$ASCII_LINK"
  echo "# 仓库路径含非 ASCII 字符，已通过软链 $ASCII_LINK 执行 k6（k6 的已知限制）"
else
  cd "$REPO_ROOT"
fi

mkdir -p "$RAW_DIR"
STAMP="$(date +%Y%m%d-%H%M%S)"
REPORT="tools/load/results/baseline-${STAMP}.md"
WORK="tools/load/results/raw/baseline-${STAMP}"

# ---------------------------------------------------------------- ① 环境规格

echo "== ① 记录环境规格 =="
{
  echo "## 环境规格（$STAMP）"
  echo ""
  echo "| 项 | 值 |"
  echo "|---|---|"
  echo "| 主机 | $(uname -srmo) |"
  echo "| CPU 核数 | $(nproc) |"
  echo "| 内存 | $(free -h | awk '/^Mem:/{print $2" total / "$7" available"}') |"
  echo "| 压测前负载 | $(uptime | sed 's/.*load average/load average/') |"
  echo "| 容器运行时 | $(docker version --format '{{.Server.Version}}' 2>/dev/null || echo 'n/a') |"
  echo "| k6 | $(k6 version 2>/dev/null || echo '未安装') |"
  echo "| 压测入口 | ${BASE_URL:-http://127.0.0.1:8080}（nginx:80 → webapi:8080）|"
  echo "| 载荷 | 读接口 ${RATE} RPS × ${DURATION}｜写接口 ${WRITE_RATE} RPS × ${DURATION}（恒定到达速率）|"
  echo ""
  echo "**容器镜像与资源**"
  echo ""
  echo '```'
  docker compose ps --format 'table {{.Service}}\t{{.Image}}\t{{.Status}}' 2>/dev/null || true
  echo '```'
  echo ""
  echo "**容器资源占用（压测前静置快照）**"
  echo ""
  echo '```'
  docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}' 2>/dev/null || true
  echo '```'
  echo ""
  echo "**数据量**（压测前的库内实际值）"
  echo ""
  echo '```'
  docker compose exec -T pgsql psql -U "${LOADTEST_PG_USER:-kky}" -d "${LOADTEST_PG_DB:-blog_stage2}" -c \
    'SELECT (SELECT count(*) FROM "Posts") AS posts,
            (SELECT count(*) FROM "Posts" WHERE "PublishedAt" IS NOT NULL) AS published,
            (SELECT count(*) FROM "PostTag") AS posttag,
            (SELECT count(*) FROM "Tags") AS tags,
            (SELECT count(*) FROM "Categories") AS categories;' \
    -c "SELECT pg_size_pretty(pg_total_relation_size('\"Posts\"')) AS posts_size;"
  echo '```'
  echo ""
  echo "**PostgreSQL / Redis 版本**"
  echo ""
  echo '```'
  docker compose exec -T pgsql psql -U "${LOADTEST_PG_USER:-kky}" -d "${LOADTEST_PG_DB:-blog_stage2}" -tAc 'SELECT version();'
  docker compose exec -T redis redis-cli INFO server | grep -E 'redis_version|os:' || true
  echo '```'
} > "tools/load/results/env-${STAMP}.md"
cat "tools/load/results/env-${STAMP}.md"

# ---------------------------------------------------------------- ② 等就绪

echo ""
echo "== ② 等待 /health 就绪 =="
for i in $(seq 1 60); do
  if curl -s --noproxy '*' -o /dev/null -w '%{http_code}' http://127.0.0.1:8080/health | grep -q 200; then
    echo "  /health 200（第 ${i} 次探测）"
    break
  fi
  sleep 2
  [[ $i -eq 60 ]] && { echo "  ❌ /health 一直不就绪，先修环境"; exit 1; }
done

# ---------------------------------------------------------------- ③ 限流必须已关

echo ""
echo "== ③ 校验限流已关闭 =="
# search 规则容量 20：连打 25 次，若出现 429 说明限流仍开着
RL_HITS=$(for i in $(seq 1 25); do
  curl -s --noproxy '*' -o /dev/null -w '%{http_code}\n' "http://127.0.0.1:8080/api/posts/search?keyword=%E7%BC%93%E5%AD%98"
done | sort | uniq -c | tr '\n' ' ')
echo "  25 次 search 的状态码分布：$RL_HITS"
if echo "$RL_HITS" | grep -q '429'; then
  cat <<'EOF'
  ❌ 检测到 429：限流仍然开着。**这种数字测的是限流器，不是应用**（learn/03 §9 热点一）。
     先执行：
       docker compose -f docker-compose.yml -f tools/load/docker-compose.loadtest.yml up -d webapi
     等它 healthy（约 10~30 秒，会重跑一次迁移检查）后重跑本脚本。
EOF
  exit 1
fi
echo "  ✅ 未触发限流"

# ---------------------------------------------------------------- ④ 预热

echo ""
echo "== ④ 预热缓存（热缓存组的前提）=="
curl -s --noproxy '*' -o /dev/null "http://127.0.0.1:8080/api/posts?page=1&pageSize=12"
curl -s --noproxy '*' -o /dev/null "http://127.0.0.1:8080/api/posts/archives"
curl -s --noproxy '*' -o /dev/null "http://127.0.0.1:8080/api/site/config"
echo "  已预热 list / archives / config"

# ---------------------------------------------------------------- ⑤⑥ 跑脚本

# ⚠️ 日志后缀用 .txt 而不是 .log：仓库根的 .gitignore 忽略了 `*.log`
#    （"Logs" 那一节），用 .log 会让**实测证据进不了仓库**。
K6_FAILED=0

run_k6() {
  local script="$1" name="$2" rate="${3:-$RATE}" rc=0
  echo ""
  echo "---- $name（${rate} RPS）----"
  # 单个脚本失败（例如 SLO threshold 未达标）**不中断整轮**：
  # 冷缓存与汇总仍然要产出，否则一次未达标会让你连其它接口的数字都拿不到。
  # 退出码记在 K6_FAILED 里，最后统一以非 0 退出。
  set +e
  k6 run -e "RATE=${rate}" -e "DURATION=${DURATION}" "$K6_DIR/$script" 2>&1 \
    | tee "$WORK-${name}.txt" | grep -vE '^\s*$' | tail -20
  rc=${PIPESTATUS[0]}
  set -e
  if [[ $rc -ne 0 ]]; then
    echo "  ⚠️ $name 未通过（k6 退出码 ${rc}）—— 通常是 SLO threshold 未达标或请求出错，详见 $WORK-${name}.txt"
    K6_FAILED=1
  fi
}

echo ""
echo "== ⑤ B0 冒烟（${RATE} RPS × ${DURATION}）=="
set +e
k6 run -e "RATE=${RATE}" -e "DURATION=${DURATION}" "$K6_DIR/00-smoke.js" 2>&1 | tee "$WORK-smoke.txt" | tail -12
SMOKE_RC=${PIPESTATUS[0]}
set -e
if [[ $SMOKE_RC -ne 0 ]]; then
  echo "  ⚠️ B0 冒烟未通过（退出码 ${SMOKE_RC}）。冒烟是前置门禁：环境不干净时后面的基线数字不可用，"
  echo "     但仍继续跑完以保留证据 —— 请先修环境再重新执行本脚本。"
  K6_FAILED=1
fi

echo ""
echo "== ⑥ B1 基线：六接口各跑一遍（写路径用 ${WRITE_RATE} RPS）=="
run_k6 01-posts-list.js posts-list
run_k6 02-post-detail.js posts-detail
run_k6 03-posts-search.js posts-search
run_k6 04-posts-archives.js posts-archives
run_k6 05-site-config.js site-config
run_k6 06-posts-create.js posts-create "$WRITE_RATE"

# ---------------------------------------------------------------- ⑦ 冷缓存

if [[ "$SKIP_COLD" -eq 0 ]]; then
  echo ""
  echo "== ⑦ 冷缓存探针（每轮 FLUSHDB）=="
  node tools/load/cold-cache-probe.mjs --rounds "$COLD_ROUNDS" --json "$RAW_DIR/cold-cache.json" | tee "$WORK-cold.txt"
fi

# ---------------------------------------------------------------- ⑧ 汇总

echo ""
echo "== ⑧ 生成汇总报告 =="
node tools/load/summarize-baseline.mjs --env "tools/load/results/env-${STAMP}.md" \
  --out "$REPORT" \
  --cold "$RAW_DIR/cold-cache.json"

echo ""
if [[ $K6_FAILED -ne 0 ]]; then
  echo "⚠️ 基线完成，但有脚本未达标：$REPORT"
  echo "   明细：$RAW_DIR/"
  exit 1
fi
echo "✅ 基线完成：$REPORT"
echo "   明细：$RAW_DIR/"
