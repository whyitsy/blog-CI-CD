<#
============================================================================
 启动一个**专用的**、带调试端口的 Chrome，供 tools/e2e 的检查连接。

 用法：
   pwsh -File tools/e2e/start-chrome.ps1              # 无头（默认，推荐）
   pwsh -File tools/e2e/start-chrome.ps1 -Headless:$false   # 有界面，便于人眼看
   pwsh -File tools/e2e/start-chrome.ps1 -Port 9333

 ⚠️ 这里有一个**关键**细节，也是 docs/07 §6.2 原来那条"必须先完全退出 Chrome"
    警告的来源：
    
    Chrome 是**单例**的 —— 如果你直接跑 `chrome.exe --remote-debugging-port=9222`，
    而此时已经有一个 Chrome 在跑（用的是默认配置目录），
    新命令**只会把一个标签页交给已有进程**，调试端口根本不会打开，
    你却在 /json/version 上连不上，还以为是防火墙问题。

    解法不是"先退出 Chrome"（那样每次都要关掉自己所有窗口），
    而是**给 E2E 一个独立的 user-data-dir**：
    配置目录不同 = 另一个浏览器实例 = 它自己会开调试端口。
    下面的 $ProfileDir 就是干这个的。
============================================================================
#>
param(
  [int]$Port = 9222,
  # 独立配置目录：既避开单例问题，也把「上次跑完留下的登录态」和你的日常浏览器隔开
  [string]$ProfileDir = (Join-Path $env:TEMP 'blog-e2e-chrome-profile'),
  [switch]$Headless = $true,
  [int]$Width = 1440,
  [int]$Height = 1100
)

$ErrorActionPreference = 'Stop'

# ── 找 chrome.exe ──────────────────────────────────────────────────────────
$candidates = @(
  "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
  "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
  "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe"
)
$chrome = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $chrome) {
  throw "找不到 chrome.exe。找过这些位置：`n  $($candidates -join "`n  ")"
}

# ── 如果端口已经能用，就不要重复启动 ────────────────────────────────────────
try {
  $existing = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/json/version" -TimeoutSec 2
  Write-Host "端口 $Port 上已经有一个可用的 Chrome：$($existing.Browser)" -ForegroundColor Green
  Write-Host "  （要换一个，先关掉它，或用 -Port 指定别的端口）"
  exit 0
} catch {
  # 连不上 = 需要启动，继续
}

New-Item -ItemType Directory -Force -Path $ProfileDir | Out-Null

$arguments = @(
  "--remote-debugging-port=$Port"
  "--user-data-dir=$ProfileDir"
  "--window-size=$Width,$Height"
  '--no-first-run'
  '--no-default-browser-check'
  '--disable-gpu'
  '--hide-scrollbars'
  '--disable-extensions'
  'about:blank'
)
if ($Headless) { $arguments = @('--headless=new') + $arguments }

Write-Host "启动 Chrome："
Write-Host "  $chrome"
Write-Host "  配置目录 $ProfileDir"
Write-Host "  调试端口 $Port   无头=$Headless"

Start-Process -FilePath $chrome -ArgumentList $arguments | Out-Null

# ── 等它就绪（"进程起来了" != "调试端口能连了"，这和 CI 等 PG 就绪是同一个道理）──
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
  try {
    $v = Invoke-RestMethod -Uri "http://127.0.0.1:$Port/json/version" -TimeoutSec 2
    Write-Host "`n就绪：$($v.Browser)" -ForegroundColor Green
    Write-Host "WebSocket 端点：$($v.webSocketDebuggerUrl)"
    Write-Host "`n现在可以跑检查了：" -ForegroundColor Cyan
    Write-Host '  "/mnt/c/Program Files/nodejs/node.exe" D:\path\to\tools\e2e\run.mjs'
    exit 0
  } catch {
    Start-Sleep -Milliseconds 500
  }
}

throw "Chrome 起来了但 $Port 端口在 30 秒内没有就绪。检查 $ProfileDir 下有没有别的实例占用，或换个 -Port。"
