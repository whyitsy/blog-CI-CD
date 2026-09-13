// ============================================================================
// CDP 客户端：把 29 个脚本里**逐份复制粘贴**的那 40 行胶水代码抽出来。
//
// 这是 G2 里真正有价值的一步 —— 不是"把文件搬个位置"，而是：
//
//   搬之前：每个脚本都自带一份 pickTarget() + WebSocket + send() + ev() + sleep()，
//           写一个新检查要 50 行样板，所以大家宁可复制旧脚本再改。
//   搬之后：写一个新检查只要 5 行（见 checks/ 里最短的那几个）。
//
// 依赖：**零 npm 依赖**。Node 22+ 内置全局 WebSocket，所以不需要 puppeteer / playwright。
//      这是刻意的选择：CDP 的能力足够本项目的 UI 级检查，
//      而引入一个浏览器驱动框架会带来安装体积、版本漂移和 CI 集成成本。
//
// ⚠️ 与原来那 29 个脚本的三个**有意**差异（都在下面用 [差异] 标出）：
//   1. 每次检查开一个**新标签页**，结束后关掉 —— 避免用例之间互相污染；
//   2. 固定 sleep 换成**轮询等待条件成立** —— 更快也更不容易假红；
//   3. eval 抛异常时**显式报错**，而不是静默返回 undefined。
// ============================================================================

import { config, assertNodeVersion } from './config.mjs'

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

/**
 * 「等超时」与「其它错误」必须能区分开。
 *
 * ⚠️ 这条约束是被一次真实的假红换来的：
 *    07 里写的是 `try { await page.waitForText(...) } catch { rendered = false }`，
 *    而当时库里的方法名是 `waitText`（少了个 For）。
 *    于是 `page.waitForText is not a function` 这个 **TypeError**
 *    被那个 catch 吞掉，变成了"空态文案没出现"——
 *    6 条断言全红，全部指向错误的结论，而真实原因只是我打错了一个方法名。
 *
 *    这就是 docs/14 第 8 条讲的同一件事：
 *    **一个把"出错了"和"条件不成立"混为一谈的 catch，会稳定地产生假红，
 *      而假红会训练人忽略红色。**
 *
 *    所以：只有 WaitTimeoutError 允许被"软处理"，其它异常必须继续往外抛。
 */
export class WaitTimeoutError extends Error {
  constructor(message) {
    super(message)
    this.name = 'WaitTimeoutError'
    this.isWaitTimeout = true
  }
}

async function httpJson(url, method = 'GET') {
  // GET 用在 /json/list、/json/close；PUT 用在 /json/new
  // （Chrome 111 起 /json/new 只接受 PUT，否则 405）
  const res = await fetch(url, { method })
  if (!res.ok) throw new Error(`${method} ${url} -> HTTP ${res.status}`)
  const text = await res.text()
  if (!text) return null
  try {
    return JSON.parse(text)
  } catch {
    return text
  }
}

/** 连不上 Chrome 时的报错。这句话必须能直接告诉人「下一步做什么」 */
function chromeUnreachable(err) {
  return new Error(
    `连不上 Chrome 的调试端口 ${config.cdpUrl}（${err?.message ?? err}）。\n` +
      `  → 先启动它：tools/e2e/start-chrome.ps1（Windows）或 tools/e2e/start-chrome.sh（WSL）\n` +
      `  → 如果 Chrome 已经在跑但没带 --remote-debugging-port，必须先完全退出再重开：\n` +
      `     已存在的 Chrome 进程不会接受新的调试端口参数。`,
  )
}

// ────────────────────────────────────────────────────────────────────────────
// 标签页
// ────────────────────────────────────────────────────────────────────────────

/** [差异 1] 开一个全新标签页。用独立标签隔离用例，而不是复用同一个页面 */
async function createTarget() {
  try {
    return await httpJson(`${config.cdpUrl}/json/new?about:blank`, 'PUT')
  } catch (err) {
    throw chromeUnreachable(err)
  }
}

async function closeTarget(id) {
  try {
    await httpJson(`${config.cdpUrl}/json/close/${id}`)
  } catch {
    // 关不掉就算了：Chrome 退出时自然会清理
  }
}

/**
 * 打开一个受控页面。所有检查的入口。
 * @returns {Promise<Page>}
 */
export async function openPage() {
  assertNodeVersion()
  const target = await createTarget()
  if (!target?.webSocketDebuggerUrl) throw chromeUnreachable(new Error('没有返回 webSocketDebuggerUrl'))

  const ws = new WebSocket(target.webSocketDebuggerUrl)
  let nextId = 0
  const pending = new Map()
  const eventWaiters = new Map()
  const collected = new Map()

  ws.addEventListener('message', (ev) => {
    const msg = JSON.parse(ev.data)
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id)
      pending.delete(msg.id)
      if (msg.error) reject(new Error(`${msg.error.message}（${JSON.stringify(msg.error.data ?? '')}）`))
      else resolve(msg.result)
      return
    }
    if (!msg.method) return
    if (collected.has(msg.method)) collected.get(msg.method).push(msg.params)
    if (eventWaiters.has(msg.method)) {
      const waiters = eventWaiters.get(msg.method)
      eventWaiters.delete(msg.method)
      for (const w of waiters) w(msg.params)
    }
  })

  await new Promise((resolve, reject) => {
    ws.addEventListener('open', resolve, { once: true })
    ws.addEventListener('error', () => reject(chromeUnreachable(new Error('WebSocket 连接失败'))), { once: true })
  })

  function send(method, params = {}) {
    const id = ++nextId
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject })
      ws.send(JSON.stringify({ id, method, params }))
    })
  }

  /** 等某个 CDP 事件。必须在触发动作**之前**调用，否则会错过（见 navigate） */
  function nextEvent(method, timeoutMs = config.timeoutMs) {
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error(`等待 CDP 事件超时：${method}`)), timeoutMs)
      const wrapped = (params) => {
        clearTimeout(timer)
        resolve(params)
      }
      if (!eventWaiters.has(method)) eventWaiters.set(method, [])
      eventWaiters.get(method).push(wrapped)
    })
  }

  await send('Page.enable')
  await send('Runtime.enable')
  await send('Emulation.setDeviceMetricsOverride', {
    width: config.viewport.width,
    height: config.viewport.height,
    deviceScaleFactor: 1,
    mobile: false,
  })

  // 截图目录名在开页时就固定，保证同一次检查的截图落在同一个目录里
  const runDirName = currentRunDir() ?? 'unset'

  const page = {
    /** 原始 CDP 调用，需要用到本封装没覆盖的能力时用 */
    send,
    nextEvent,

    /**
     * 订阅某类 CDP 事件，返回一个**持续累积**的数组。
     * 典型用法：`Runtime.exceptionThrown` —— 用它断言「页面上没有未捕获异常」。
     * （原来某个脚本用 `window.__errCount` 来判断，但那个变量从来没人写过，
     *   所以那条断言永远通过 —— 又一处"看起来在检查"。）
     */
    collect(method) {
      if (!collected.has(method)) collected.set(method, [])
      return collected.get(method)
    },

    /**
     * 在页面里求值。约定与原来一致：`returnByValue` + `awaitPromise`，
     * 所以 `(async()=>{...})()` 可以直接拿到 resolve 后的值。
     *
     * [差异 3] 原来异常时返回 undefined，导致断言失败只能看到 `undefined`，
     * 分不清「表达式写错了」和「页面状态不对」。这里显式抛出。
     */
    async eval(expression) {
      const r = await send('Runtime.evaluate', {
        expression,
        returnByValue: true,
        awaitPromise: true,
      })
      if (r.exceptionDetails) {
        const d = r.exceptionDetails
        throw new Error(`页面内求值抛异常：${d.exception?.description ?? d.text}\n  表达式：${expression.slice(0, 160)}`)
      }
      return r.result?.value
    },

    /** 当前路径（含 query） */
    path() {
      return this.eval('location.pathname + location.search')
    },

    /** 纯路径，去掉 query —— 路由断言几乎总是只关心它 */
    async pathOnly() {
      const p = await this.path()
      return (p ?? '').split('?')[0]
    },

    /**
     * 固定等一小段。
     *
     * ⚠️ 什么时候**必须**用它而不是 waitFor：
     *    断言「应该**停留**在某个路径」时，waitPath 会在跳转发生**之前**就立刻通过，
     *    于是它永远发现不了「本该跳走却没跳」。这种情况只能等一小段再读最终状态。
     *    （这正是原来那些脚本用固定 sleep 的原因 —— 不是因为懒。）
     */
    settle(ms = 800) {
      return sleep(ms)
    },

    /**
     * 轮询直到表达式返回真值。返回那个真值。
     * [差异 2] 这就是替代固定 sleep 的东西：条件早成立就早继续，慢机器也不会假红。
     */
    async waitFor(expression, { timeoutMs = config.timeoutMs, intervalMs = 200, label, soft = false } = {}) {
      const deadline = Date.now() + timeoutMs
      for (;;) {
        let v
        try {
          v = await this.eval(expression)
        } catch {
          v = undefined // 导航中途上下文销毁是正常的，重试即可
        }
        if (v) return v
        if (Date.now() > deadline) {
          // soft: true —— 把「超时」当成 false 返回，而不是抛异常。
          // 这样调用方不必写 try/catch，也就不会顺手把别的异常一起吞掉。
          if (soft) return false
          throw new WaitTimeoutError(`等待超时（${timeoutMs}ms）：${label ?? expression}`)
        }
        await sleep(intervalMs)
      }
    },

    /** 等页面正文里出现某段文字 */
    waitForText(text, opts) {
      return this.waitFor(`document.body.innerText.includes(${JSON.stringify(text)})`, {
        label: `正文出现「${text}」`,
        ...opts,
      })
    },

    /** 等某个选择器出现 */
    waitForSelector(sel, opts) {
      return this.waitFor(`!!document.querySelector(${JSON.stringify(sel)})`, {
        label: `出现 ${sel}`,
        ...opts,
      })
    },

    /** 等路由落到指定路径。SPA 守卫是异步的，所以必须等而不是 sleep */
    waitPath(expected, opts) {
      return this.waitFor(`(location.pathname + location.search).split('?')[0] === ${JSON.stringify(expected)}`, {
        label: `路由落到 ${expected}`,
        ...opts,
      })
    },

    /**
     * 导航并等**新文档**加载完成。
     *
     * ⚠️ 这里有个经典竞态：`Page.navigate` 返回后立刻读 `document.readyState`，
     *    读到的可能还是**旧文档**的 'complete'，于是你以为加载完了，其实还没开始。
     *    所以先注册 loadEventFired 的监听，再发 navigate。
     */
    async navigate(path, { ready } = {}) {
      const url = path.startsWith('http') ? path : config.baseUrl + path
      const loaded = nextEvent('Page.loadEventFired')
      await send('Page.navigate', { url })
      await loaded
      if (ready) await this.waitFor(ready, { label: typeof ready === 'string' ? ready : '就绪条件' })
      return this.path()
    },

    /** 正文文本（断言的主力） */
    text() {
      return this.eval('document.body.innerText')
    },

    /** [差异 1] 截图落到 artifacts/ 下，不再写死 D:/tmpbuild/shots/ */
    async shot(name) {
      if (!config.shots) return
      const r = await send('Page.captureScreenshot', { format: 'png' })
      const { mkdir, writeFile } = await import('node:fs/promises')
      const { join } = await import('node:path')
      const dir = join(config.artifactsDir, runDirName)
      await mkdir(dir, { recursive: true })
      await writeFile(join(dir, `${name}.png`), Buffer.from(r.data, 'base64'))
    },

    /**
     * 让**后续导航**里的 fetch 命中一个假的响应。
     * 用来在真实环境里制造「后端挂了」而不用真的去停后端
     * （从 collections-empty.mjs 里学来的手法，很好用）。
     *
     * @param {RegExp} pattern 匹配请求 URL 的正则。会**原样拼进**页面里的源码，
     *   所以必须传 RegExp 字面量（传字符串会得到 `api/tags.test(url)` 这种错东西）。
     */
    async interceptFetch(pattern, { status, body }) {
      if (!(pattern instanceof RegExp)) {
        throw new Error(`interceptFetch 的 pattern 必须是 RegExp，收到 ${typeof pattern}`)
      }
      await send('Page.addScriptToEvaluateOnNewDocument', {
        source: `(() => {
          const orig = window.fetch;
          window.fetch = function (input, init) {
            const url = typeof input === 'string' ? input : (input && input.url) || '';
            if (${pattern}.test(url)) {
              return Promise.resolve(new Response(${JSON.stringify(body)}, {
                status: ${status}, headers: { 'Content-Type': 'application/json' }
              }));
            }
            return orig.call(this, input, init);
          };
        })();`,
      })
    },

    close: () => {
      try {
        ws.close()
      } catch {
        /* 忽略 */
      }
      return closeTarget(target.id)
    },
  }

  return page
}

// 一次进程内只跑一个检查，所以用模块级变量记录本次运行的目录名即可
let _runDir = null
function currentRunDir() {
  return _runDir
}
export function setRunDir(name) {
  _runDir = name
}

// ────────────────────────────────────────────────────────────────────────────
// 登录 / 直接打接口
// ────────────────────────────────────────────────────────────────────────────

/**
 * 页面内的 fetch + **结构化结果**。
 *
 * ⚠️ 这个函数存在的唯一理由：**让失败信息指向正确的地方。**
 *
 *    直接写 `const b = await r.json()` 的后果是：后端没起来时（Vite 代理返回 502 + HTML），
 *    抛出来的是
 *
 *        SyntaxError: Failed to execute 'json' on 'Response': Unexpected end of JSON input
 *
 *    这句话读起来像"响应格式不对"，**而不是"后端没在跑"** —— 它会把排查方向带偏。
 *    （这个坑在本项目里真的发生过两次：一次是登录端点改名，一次是后端没启动，
 *      两次的表现都是这句 JSON 解析错误。）
 *
 *    所以这里把三种情况分开：
 *      · transport —— fetch 本身就失败了（DNS/连接被拒/超时）
 *      · non-json  —— 拿到了响应，但**不是 JSON**（502/504 的 HTML 错误页属于这种）
 *      · json      —— 真正的业务响应
 */
function pageFetchScript(method, path, payload) {
  const hasPayload = payload !== undefined
  return `(async () => {
    const auth = { 'Authorization': 'Bearer ' + (localStorage.getItem(${JSON.stringify(config.storageKeys.token)}) || '') };
    const init = {
      method: ${JSON.stringify(method)},
      // 只在真的有 body 时才带 Content-Type —— GET 没有 body，塞上它是多余的
      headers: ${hasPayload ? "{ 'Content-Type': 'application/json', ...auth }" : 'auth'}
    };
    ${hasPayload ? `init.body = ${JSON.stringify(JSON.stringify(payload))};` : ''}
    let r;
    try {
      r = await fetch(${JSON.stringify(path)}, init);
    } catch (e) {
      return { kind: 'transport', detail: String((e && e.message) || e) };
    }
    const text = await r.text();
    let body = null;
    try { body = JSON.parse(text); } catch { /* 保持 null，下面按 non-json 处理 */ }
    if (body === null) {
      return { kind: 'non-json', httpStatus: r.status, detail: text.slice(0, 160) || '(空响应体)' };
    }
    return { kind: 'json', httpStatus: r.status, body };
  })()`
}

/** 把 transport / non-json 两种失败翻译成**能直接指导下一步**的报错 */
function describeFetchFailure(what, res) {
  if (res?.kind === 'transport') {
    return new Error(`${what}：连不上（${res.detail}）。检查前端 dev server 是否在跑。`)
  }
  if (res?.kind === 'non-json') {
    return new Error(
      `${what}：服务端返回的不是 JSON（HTTP ${res.httpStatus}）—— **后端或反向代理很可能没有在运行**。\n` +
        `        响应片段：${res.detail.replace(/\s+/g, ' ')}\n` +
        `        → 后端应在 5131 上（docs/07 §4.1）；run.mjs 的 preflight 告警指的是同一件事。`,
    )
  }
  return null
}

/**
 * 通过接口拿 token 并写进 localStorage —— 比"在登录页填表单"稳定得多，
 * 而且它同时验证了「凭据有效」这件事。
 * 之后必须 `navigate` 一次（整页加载），Vue 的 auth store 才会重新读到 token。
 */
export async function login(page, account) {
  await page.navigate('/login', { ready: 'document.readyState === "complete"' })
  await page.eval('localStorage.clear()')
  const res = await page.eval(pageFetchScript('POST', config.loginPath, {
    email: account.email,
    password: account.password,
  }))

  // 环境类失败优先报出来 —— 别让它伪装成"凭据错误"
  const envError = describeFetchFailure(`登录（${account.email}）`, res)
  if (envError) throw envError

  if (res.body.code !== 0) {
    throw new Error(`登录失败：${account.email} -> code=${res.body.code} ${res.body.message ?? ''}`)
  }

  // 只把 data 这一小段注入页面（而不是把整个响应体塞进去）
  await page.eval(`(() => {
    const d = ${JSON.stringify(res.body.data)};
    localStorage.setItem(${JSON.stringify(config.storageKeys.token)}, d.token);
    localStorage.setItem(${JSON.stringify(config.storageKeys.user)}, JSON.stringify(d.user));
    localStorage.setItem(${JSON.stringify(config.storageKeys.expires)}, d.expiresAt);
    return true;
  })()`)

  return { ok: true, role: res.body.data.role, userId: res.body.data.user?.id }
}

/** 登录后直接整页导航到目标路径 */
export async function loginAndGo(page, account, path, opts) {
  await login(page, account)
  return page.navigate(path, opts)
}

/**
 * 用当前 localStorage 里的 token 打一个 GET 接口（同源，因此不会有 CORS 问题）。
 *
 * 返回形状保持 `{ status, body }`；后端不可用时额外带上
 * `nonJson: true` / `detail`，**而不是抛一个误导人的 JSON 解析错误**。
 */
export async function apiGet(page, path) {
  const res = await page.eval(pageFetchScript('GET', path))
  if (res?.kind === 'json') return { status: res.httpStatus, body: res.body }
  return { status: res?.httpStatus ?? 0, body: null, nonJson: true, detail: res?.detail }
}

/** 用当前 token 发一个带 JSON body 的请求 */
export async function apiSend(page, method, path, payload) {
  const res = await page.eval(pageFetchScript(method, path, payload ?? null))
  if (res?.kind === 'json') return { status: res.httpStatus, body: res.body }
  return { status: res?.httpStatus ?? 0, body: null, nonJson: true, detail: res?.detail }
}

// ────────────────────────────────────────────────────────────────────────────
// 断言与汇总
// ────────────────────────────────────────────────────────────────────────────

export class Runner {
  constructor(name) {
    this.name = name
    this.passed = 0
    this.failed = 0
    this.skipped = 0
    this.failures = []
  }

  /** 一条断言。detail 只在需要解释时给（失败时一定会打印） */
  check(name, ok, detail = '') {
    if (ok) this.passed++
    else {
      this.failed++
      this.failures.push(name)
    }
    const suffix = detail ? `\n        ${detail}` : ''
    console.log(`  ${ok ? 'PASS' : 'FAIL'}  ${name}${ok ? '' : suffix}`)
    return ok
  }

  /**
   * 前置数据不足时**如实跳过**，而不是假装通过、也不是记成失败。
   *
   * 为什么要有这个状态（这是原脚本没有的东西）：
   *   有些检查需要「某专栏至少有 2 篇文章」这类前置数据。硬写成 PASS 是撒谎；
   *   硬写成 FAIL 又会把「环境没准备好」和「代码坏了」混为一谈，
   *   久而久之你会习惯性忽略红色。所以给它第三种结果，并且在摘要里**显式计数**。
   */
  skip(reason) {
    this.skipped++
    console.log(`  SKIP  ${reason}`)
  }

  /** 打印一条信息（不是断言）。用于「清理成功」「已发现 N 条数据」这类上下文 */
  note(text) {
    console.log(`  ·     ${text}`)
  }

  /** 打印摘要。返回值给 run.mjs 用 */
  summary() {
    const total = this.passed + this.failed
    const skipText = this.skipped ? ` / ${this.skipped} 跳过` : ''
    console.log(`  结果：${this.passed} 通过 / ${this.failed} 失败${skipText}（共断言 ${total} 条）`)
    return { name: this.name, passed: this.passed, failed: this.failed, skipped: this.skipped, failures: this.failures }
  }
}

/**
 * 检查脚本的统一入口：建 Runner、开页面、跑用例、收尾、定退出码。
 *
 * 有了它，一个检查脚本的全部样板就只剩这一行：
 *     await main('auth-flow', async ({ page, run }) => { ... })
 *
 * @param {string} name 检查名（同时用作截图目录名）
 * @param {(ctx: {page: Page, run: Runner}) => Promise<void>} body
 */
export async function main(name, body) {
  assertNodeVersion()
  setRunDir(name)
  console.log(`\n──────── ${name} ────────`)
  const run = new Runner(name)
  let page
  try {
    page = await openPage()
    await body({ page, run })
  } catch (err) {
    // 让「脚本自己炸了」也表现为一条失败的断言，而不是一段看不懂的堆栈
    run.check('检查脚本执行完成（没有中途抛异常）', false, String(err?.message ?? err))
  } finally {
    if (page) await page.close()
  }
  const result = run.summary()
  // 给 run.mjs 用的机器可读结果。放在最后一行，便于父进程解析。
  console.log(`##E2E## ${JSON.stringify(result)}`)
  process.exit(result.failed === 0 ? 0 : 1)
}
