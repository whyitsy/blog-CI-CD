// ============================================================================
// 01 · 路由守卫：未登录访问受保护页面必须被重定向
//
// 来自 guard-check-win.mjs（同名脚本还有一个 guard-check.mjs：
// 两者**唯一**的区别是 -win 版本先清了一次 localStorage，因此对「上次跑完还留着
// 登录态」是幂等的。留着的那份会在你刚跑完 02 之后随机失败 —— 所以只保留这个版本。）
//
// 这个检查覆盖的是**前端的 UX 边界**，不是安全边界：
// 真正的授权在服务端（learn/01-后端知识地图.md §6.5），前端守卫只是让用户少看到一次空白页。
// 所以它断言的是「会跳走」，而不是「跳走了就等于安全」。
//
// ⚠️ 搬进仓库时发现：原脚本的期望**已经过期了**。
//
//   它写的是「未登录访问 /admin → 期望 /admin/login」。
//   但前端后来把后台登录页并入了统一登录页：
//
//     router/index.ts:43  // 旧的后台登录页已并入 /login，保留路径并转发查询参数（returnUrl），
//                         // 避免旧链接失效
//     router/index.ts:108 // 只有一个登录页：无论是 /admin 还是 /me 都回到它，登录后按角色落地
//
//   所以现在实际是 `/login?returnUrl=/admin` —— **这是刻意的设计**，不是缺陷。
//   如果当时图省事"改代码让检查通过"，就会把一个刻意的设计决定改回去。
//   这条经历记在 archive/问题排查记录.md §9：**旧脚本里可能写着过期的期望**。
//
//   现在不但对齐了现状，还多断言了一件事：`returnUrl` 必须带上原地址 ——
//   那才是「登录后能回到原处」的机制，原先根本没验。
// ============================================================================

import { main } from '../lib/cdp.mjs'

/**
 * 每个用例：
 *   from    从哪出发
 *   expect  期望最终落在哪个**路径**（不含 query）
 *   query   可选：期望 query 里必须出现这个片段
 */
const CASES = [
  { name: '未登录访问 /admin 回到统一登录页', from: '/admin', expect: '/login', query: 'returnUrl=/admin' },
  { name: '未登录访问 /me 回到统一登录页', from: '/me', expect: '/login', query: 'returnUrl=/me' },
  { name: '未登录访问 /admin/site 回到统一登录页', from: '/admin/site', expect: '/login', query: 'returnUrl=/admin/site' },
  { name: '旧链接 /admin/login 被转发到 /login（不失效）', from: '/admin/login', expect: '/login' },
  { name: '未登录访问 /login（应停留）', from: '/login', expect: '/login' },
  { name: '未知路径 -> 404 页（路径不变）', from: '/definitely-missing', expect: '/definitely-missing' },
  { name: '公开首页（应停留）', from: '/', expect: '/' },
]

await main('01-route-guard', async ({ page, run }) => {
  // 断言的是「未登录」行为，先清掉可能残留的登录态。
  // 必须在同源上下文里清（localStorage 是按 origin 隔离的）。
  await page.navigate('/', { ready: 'document.readyState === "complete"' })
  await page.eval('localStorage.clear()')

  for (const c of CASES) {
    await page.navigate(c.from)
    // ⚠️ 这里必须用 settle 而不是 waitPath：
    //    对于「应该停留」的用例，waitPath 会在跳转发生**之前**就通过，
    //    于是永远发现不了「本该跳走却没跳」。必须等一小段再读最终状态。
    await page.settle(800)

    const got = await page.path()
    const path = (got ?? '').split('?')[0]
    const pathOk = path === c.expect
    const queryOk = !c.query || (got ?? '').includes(c.query)
    run.check(c.name, pathOk && queryOk, `期望 ${c.expect}${c.query ? ` (${c.query})` : ''}  实际 ${got}`)
  }

  await page.shot('final-route-guard')
})
