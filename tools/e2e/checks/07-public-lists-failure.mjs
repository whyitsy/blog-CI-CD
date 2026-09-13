// ============================================================================
// 07 · 三个公开列表页在「接口失败」时都应显示空态，且不把错误暴露给用户
//
// 来自 collections-empty.mjs + backend-down-public-pages.mjs，**合并并加强了**。
//
// 为什么合并：原来那两个脚本的前置条件互相冲突 ——
//   · collections-empty.mjs 要求后端**在跑**（它自己伪造失败）；
//   · backend-down-public-pages.mjs 要求后端**停掉**（它靠 Vite 代理返回 502）。
//   所以它们不可能在一次运行里都满足，只能手工切换状态，等于不能自动化。
//
// 为什么加强：原来只测了**一种**失败形态，但真实世界有两种，且前端走的是**不同代码路径**：
//
//   | 形态 | 谁产生 | 前端看到什么 | 走哪条分支 |
//   |---|---|---|---|
//   | 应用级错误 | Kestrel 返回 500 + JSON `{code:5000}` | `res.json()` 成功 | `code !== 0` 分支 |
//   | 基础设施错误 | nginx / 代理返回 502 + **HTML** | `res.json()` **抛异常** | catch 分支 |
//
//   只测一种的话，另一种的回归检查是空白的。
//
// 手法：用 `Page.addScriptToEvaluateOnNewDocument` 在页面脚本运行**之前**
//      把 fetch 打补丁 —— 比停后端干净得多，而且能同时测两种形态。
// ============================================================================

import { main, openPage } from '../lib/cdp.mjs'

/**
 * ⚠️ `api` 必须**显式写出来**，不能从 `path` 推。
 *
 *    这不是洁癖，是被两次失败逼出来的：
 *      ① 归档页的**页面路径是 `/archive`（单数）**，猜成 `/archives` 会渲染 404 页，
 *         而断言只会说"没找到「暂无文章」"，看起来像前端缺陷；
 *      ② 归档页请求的接口是 **`/api/posts/archives`**，按页面路径推出来的
 *         `/api/archive` 根本拦不到 —— 于是页面拿到真实数据、断言失败，
 *         而失败原因（"拦截器没生效"）从断言文字里完全看不出来。
 *
 *    两次都靠"失败时打印实际正文"才定位到。这就是**可诊断性**的价值：
 *    断言的消息里必须带上现场，否则你只能重跑一遍去猜。
 */
const PAGES = [
  { path: '/collections', api: '/api/collections', empty: '暂无专栏', what: '专栏' },
  { path: '/tags', api: '/api/tags', empty: '暂无标签', what: '标签' },
  { path: '/categories', api: '/api/categories', empty: '暂无分类', what: '分类' },
  // 归档页原先和标签/分类一样漏了 catch，是这条检查把它们三个一起揪出来的（docs/14 第 9 条）
  { path: '/archive', api: '/api/posts/archives', empty: '暂无文章', what: '归档' },
]

/** 两种真实的失败形态 */
const FAILURES = [
  {
    label: '应用级错误（500 + JSON）',
    status: 500,
    body: '{"code":5000,"message":"服务暂时不可用，请稍后重试","data":null}',
  },
  {
    label: '基础设施错误（502 + HTML，像 nginx 那样）',
    status: 502,
    body: '<html><head><title>502 Bad Gateway</title></head><body><h1>502 Bad Gateway</h1></body></html>',
  },
]

/** 这些文案说明错误**漏到了用户面前** —— 出现任意一个都算失败 */
const LEAKED = /服务暂时不可用|网络异常|加载失败|Bad Gateway/

await main('07-public-lists-failure', async ({ run }) => {
  for (const p of PAGES) {
    for (const f of FAILURES) {
      // 每个用例开**独立标签页**：intercept 一旦注入就对该标签的后续导航一直生效，
      // 复用标签会让「哪个用例装了拦截器」变成运行顺序的函数 —— 也就是 flaky。
      const page = await openPage()
      try {
        const exceptions = page.collect('Runtime.exceptionThrown')
        // pattern 直接传 RegExp：生成出来的源码里就是 /api\/tags(\?|$)/.test(url)
        await page.interceptFetch(new RegExp(`${p.api}(\\?|$)`), f)
        await page.navigate(p.path, { ready: 'document.readyState === "complete"' })

        // 空态文案必须出现。
        // ⚠️ 用 `soft: true` 而不是 try/catch：
        //    只有"等超时"会被当成 false，**其它异常照旧往外抛**。
        //    写成 try/catch 的话，一个方法名打错（TypeError）就会伪装成
        //    "空态文案没出现"，6 条断言全红且全部指向错误的结论 —— 这个坑真的踩过。
        const rendered = await page.waitForText(p.empty, { timeoutMs: 8000, soft: true })
        const text = await page.text()
        // 失败时把**实际正文**打出来 —— 否则你只知道"没找到那四个字"，
        // 不知道页面到底显示了什么，还得再跑一遍去猜。
        const excerpt = (text || '').replace(/\s+/g, ' ').slice(0, 160)

        run.check(`${p.what}页 · ${f.label} → 显示「${p.empty}」`, rendered === true, `实际正文：${excerpt}`)
        run.check(
          `${p.what}页 · ${f.label} → 不把错误暴露给用户`,
          !LEAKED.test(text),
          `实际正文：${excerpt}`,
        )
        run.check(
          `${p.what}页 · ${f.label} → 没有未捕获异常`,
          exceptions.length === 0,
          exceptions.map((e) => e.exceptionDetails?.exception?.description ?? e.exceptionDetails?.text).join(' | ').slice(0, 200),
        )
      } finally {
        await page.close()
      }
    }
  }
})
