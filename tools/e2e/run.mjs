// ============================================================================
// E2E 检查的汇总入口
//
//   node tools/e2e/run.mjs                # 跑全部
//   node tools/e2e/run.mjs 03 07          # 只跑编号 03 与 07
//   node tools/e2e/run.mjs auth-flow      # 按名字片段筛选
//
// 为什么要**一个检查一个子进程**（而不是在这个文件里顺序 import）：
//   ① 每个检查结尾都会 process.exit()，同进程里跑第二个就跑不起来了；
//   ② 子进程天然隔离：某个检查把模块状态、浏览器标签搞脏了，不会污染下一个；
//   ③ 一个检查崩了不影响其余检查 —— 你能一次看到**全部**失败，而不是只看到第一个。
//
// 先做 preflight：Chrome 或前端没起的时候，直接给出「该怎么起」，
// 而不是让 10 个检查各自失败一次、刷 10 屏一模一样的报错。
// ============================================================================

import { spawn } from 'node:child_process'
import { readdir } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'
import { config, assertNodeVersion } from './lib/config.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const checksDir = join(here, 'checks')

const C = { reset: '\x1b[0m', red: '\x1b[31m', green: '\x1b[32m', yellow: '\x1b[33m', dim: '\x1b[2m', bold: '\x1b[1m' }
const ok = (s) => `${C.green}${s}${C.reset}`
const bad = (s) => `${C.red}${s}${C.reset}`
const warn = (s) => `${C.yellow}${s}${C.reset}`

async function preflight() {
  const problems = []

  try {
    const r = await fetch(`${config.cdpUrl}/json/version`)
    if (!r.ok) throw new Error(`HTTP ${r.status}`)
    const v = await r.json()
    console.log(`  ${ok('✓')} Chrome 调试端口可用：${v['Browser'] ?? 'unknown'}`)
  } catch (err) {
    problems.push(
      `连不上 Chrome 调试端口 ${config.cdpUrl}（${err.message}）\n` +
        `      → Windows:  pwsh -File tools/e2e/start-chrome.ps1\n` +
        `      → WSL/Linux: bash tools/e2e/start-chrome.sh\n` +
        `      ⚠️ Chrome 已经在跑时必须**先完全退出**再带调试端口重开 ——\n` +
        `         已存在的 Chrome 进程会忽略新的 --remote-debugging-port。`,
    )
  }

  try {
    const r = await fetch(config.baseUrl)
    if (!r.ok) throw new Error(`HTTP ${r.status}`)
    console.log(`  ${ok('✓')} 前端可用：${config.baseUrl}`)
  } catch (err) {
    problems.push(
      `连不上前端 ${config.baseUrl}（${err.message}）\n` +
        `      → 在 Blog.FrontEnd 下起 dev server（见 docs/07 §4.3）\n` +
        `      → 或把整栈容器跑起来后用 E2E_BASE_URL=http://localhost:8080 指过去`,
    )
  }

  // 后端只管告警：08 号检查本来就是在验证「后端不可用时前端的行为」
  try {
    const r = await fetch(`${config.baseUrl}/api/site/config`)
    const b = await r.json()
    if (b?.code !== 0) throw new Error(`code=${b?.code}`)
    console.log(`  ${ok('✓')} 后端（经前端代理）可用`)
  } catch (err) {
    console.log(`  ${warn('!')} 后端（经前端代理）不可用：${err.message}`)
    console.log(`      ${C.dim}只有需要真实数据的检查会失败；08 号检查反而正是要这种状态。${C.reset}`)
  }

  if (problems.length) {
    console.log(`\n${bad('前置条件不满足，先解决这些再跑：')}`)
    problems.forEach((p, i) => console.log(`  ${i + 1}. ${p}`))
    process.exit(2)
  }
}

function runCheck(file) {
  return new Promise((resolve) => {
    const child = spawn(process.execPath, [join(checksDir, file)], { stdio: ['ignore', 'pipe', 'pipe'] })
    let out = ''
    child.stdout.on('data', (d) => {
      out += d
      process.stdout.write(d)
    })
    child.stderr.on('data', (d) => {
      out += d
      process.stderr.write(d)
    })
    child.on('close', (code) => {
      const line = out.split('\n').reverse().find((l) => l.startsWith('##E2E## '))
      let parsed = null
      if (line) {
        try {
          parsed = JSON.parse(line.slice('##E2E## '.length))
        } catch {
          /* 解析不了就退回用退出码判断 */
        }
      }
      resolve(parsed ?? { name: file.replace(/\.mjs$/, ''), passed: 0, failed: 1, skipped: 0, failures: ['检查进程异常退出'], exitCode: code })
    })
  })
}

// ── 主流程 ─────────────────────────────────────────────────────────────────

assertNodeVersion()

const filters = process.argv.slice(2)
const all = (await readdir(checksDir)).filter((f) => f.endsWith('.mjs')).sort()
const selected = filters.length ? all.filter((f) => filters.some((x) => f.includes(x))) : all

if (selected.length === 0) {
  console.error(bad(`没有匹配的检查。可用：${all.join(' ')}`))
  process.exit(2)
}

console.log(`${C.bold}E2E 前置检查${C.reset}`)
await preflight()

console.log(`\n${C.bold}开始运行 ${selected.length} 个检查${C.reset}  ${C.dim}（前端 ${config.baseUrl} / CDP ${config.cdpUrl}）${C.reset}`)

const started = Date.now()
const results = []
for (const file of selected) {
  results.push(await runCheck(file))
}

// ── 汇总 ───────────────────────────────────────────────────────────────────
const totalPassed = results.reduce((n, r) => n + r.passed, 0)
const totalFailed = results.reduce((n, r) => n + r.failed, 0)
const totalSkipped = results.reduce((n, r) => n + r.skipped, 0)
const failedChecks = results.filter((r) => r.failed > 0)

console.log(`\n${C.bold}════════════ 汇总 ════════════${C.reset}`)
for (const r of results) {
  const mark = r.failed > 0 ? bad('FAIL') : ok('PASS')
  const skip = r.skipped ? warn(` / ${r.skipped} 跳过`) : ''
  console.log(`  ${mark}  ${r.name.padEnd(28)} ${r.passed} 通过 / ${r.failed} 失败${skip}`)
  for (const f of r.failures ?? []) console.log(`        ${C.dim}· ${f}${C.reset}`)
}

const seconds = ((Date.now() - started) / 1000).toFixed(1)
console.log(
  `\n  断言合计：${ok(`${totalPassed} 通过`)} / ${totalFailed ? bad(`${totalFailed} 失败`) : '0 失败'}` +
    `${totalSkipped ? warn(` / ${totalSkipped} 跳过`) : ''}   耗时 ${seconds}s`,
)
if (totalSkipped) {
  console.log(`  ${C.dim}⚠️ 「跳过」是**前置数据不足**，不是通过 —— 它意味着那部分没有被验证。${C.reset}`)
}
if (config.shots) {
  console.log(`  ${C.dim}截图（如果有）：${config.artifactsDir}${C.reset}`)
}

// 退出码：0 = 全绿；1 = 有失败。跳过**不**让运行失败（否则缺数据的环境永远红），
// 但会在上面显式计数，避免"悄悄少验证了一块"。
process.exit(totalFailed > 0 ? 1 : 0)
