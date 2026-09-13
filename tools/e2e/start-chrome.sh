#!/usr/bin/env bash
# ============================================================================
# 启动一个专用的、带调试端口的 Chrome（Linux / WSL 侧）
#
# 用法：
#   bash tools/e2e/start-chrome.sh                 # 无头
#   HEADLESS=0 bash tools/e2e/start-chrome.sh      # 有界面
#   PORT=9333 bash tools/e2e/start-chrome.sh
#
# ⚠️ 本项目当前的开发环境是 **Windows 编译 + WSL 发命令**。
#    在那种环境下，请**优先用 start-chrome.ps1 起 Chrome、并用 Windows 的 node 跑检查**：
#
#      Windows 的 Chrome 默认只监听 Windows 的 127.0.0.1，而 WSL2 的 localhost
#      是 WSL 自己的回环 —— 两边不是同一个网络栈（除非开了 mirrored networking）。
#      所以 `curl 127.0.0.1:9222` 在 WSL 里会连不上，而 Windows 侧一切正常。
#
#    这个 .sh 是给「Chrome 和 node 跑在同一台 Linux 机器上」的场景准备的
#    （比如把 E2E 接进 Linux CI）。
# ============================================================================
set -euo pipefail

PORT="${PORT:-9222}"
PROFILE_DIR="${PROFILE_DIR:-${TMPDIR:-/tmp}/blog-e2e-chrome-profile}"
HEADLESS="${HEADLESS:-1}"
WIDTH="${WIDTH:-1440}"
HEIGHT="${HEIGHT:-1100}"

# 如果端口已经可用，就不用重复启动
if curl -sf "http://127.0.0.1:${PORT}/json/version" >/dev/null 2>&1; then
  echo "端口 ${PORT} 上已经有一个可用的 Chrome："
  curl -s "http://127.0.0.1:${PORT}/json/version"
  echo
  exit 0
fi

CHROME=""
for c in google-chrome google-chrome-stable chromium chromium-browser; do
  if command -v "$c" >/dev/null 2>&1; then CHROME="$(command -v "$c")"; break; fi
done
if [ -z "$CHROME" ]; then
  echo "找不到 Chrome/Chromium。装一个，或用 E2E_CDP_URL 指向别处的调试端口。" >&2
  exit 1
fi

mkdir -p "$PROFILE_DIR"

ARGS=(
  "--remote-debugging-port=${PORT}"
  "--user-data-dir=${PROFILE_DIR}"
  "--window-size=${WIDTH},${HEIGHT}"
  --no-first-run
  --no-default-browser-check
  --disable-gpu
  --hide-scrollbars
  --disable-extensions
  about:blank
)
# ⚠️ user-data-dir 必须是独立的：Chrome 是单例的，
#    复用默认配置目录时新命令只会把标签页交给已有进程，调试端口根本不会开。
if [ "$HEADLESS" != "0" ]; then
  ARGS=("--headless=new" "${ARGS[@]}")
fi

echo "启动 Chrome：$CHROME"
echo "  配置目录 $PROFILE_DIR"
echo "  调试端口 $PORT   无头=$HEADLESS"

nohup "$CHROME" "${ARGS[@]}" >/dev/null 2>&1 &

# "进程起来了" != "调试端口能连了" —— 必须等，否则会随机失败
for _ in $(seq 1 60); do
  if curl -sf "http://127.0.0.1:${PORT}/json/version" >/dev/null 2>&1; then
    echo
    echo "就绪："
    curl -s "http://127.0.0.1:${PORT}/json/version"
    echo
    echo "现在可以跑检查了：  node tools/e2e/run.mjs"
    exit 0
  fi
  sleep 0.5
done

echo "Chrome 起来了但 ${PORT} 端口在 30 秒内没有就绪。" >&2
exit 1
