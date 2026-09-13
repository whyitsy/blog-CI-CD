// ============================================================================
// 02 · 登录闭环：token 注入 → 按角色落地 → 越权拦截 → 退出登录
//
// 来自 auth-flow.mjs。断言的是「前端真的把 token 用上了」——
// 这是后端 195 项测试**测不到**的那一段（它们直接打 HTTP，绕过了 http.ts 的注入逻辑）。
//
// ⚠️ 前置数据：作者账号 writer@example.com 必须存在。
//    它由开发库的种子数据提供（BlogDbContext 播种），**不是**本检查创建的。
//    换到没有这个账号的环境（比如整栈容器首次启动）时，
//    用 E2E_AUTHOR_EMAIL / E2E_AUTHOR_PASSWORD 覆盖，或先在后台建一个。
// ============================================================================

import { main, login, loginAndGo, apiGet } from '../lib/cdp.mjs'
import { config } from '../lib/config.mjs'

const { admin, author } = config

await main('02-auth-flow', async ({ page, run }) => {
  // ── 管理员 ─────────────────────────────────────────────────────────
  await page.navigate('/admin/login', { ready: 'document.readyState === "complete"' })
  const adminLogin = await login(page, admin)
  run.check('管理员凭据可换到 token', adminLogin.ok === true, JSON.stringify(adminLogin))

  await page.navigate('/admin')
  await page.settle(800)
  run.check('管理员可直接进入 /admin', (await page.pathOnly()) === '/admin', `实际 ${await page.path()}`)

  // 受保护接口带 token 可读 —— 这一步证明 http.ts 真的注入了 Authorization 头。
  // 只是"能进 /admin 页面"还不够：前端守卫可能放行而请求仍是匿名的。
  const users = await apiGet(page, '/api/users')
  run.check('管理员带 token 可读 /api/users（200）', users?.status === 200, `实际 HTTP ${users?.status}`)

  await page.navigate('/me')
  await page.settle(800)
  run.check('管理员可进入 /me（写作台）', (await page.pathOnly()) === '/me', `实际 ${await page.path()}`)

  // ── 作者 ───────────────────────────────────────────────────────────
  const authorLogin = await login(page, author)
  run.check(
    '作者凭据可换到 token 且角色为 Author',
    authorLogin.ok === true && authorLogin.role === 'Author',
    JSON.stringify(authorLogin),
  )

  await loginAndGo(page, author, '/me')
  await page.settle(800)
  run.check('作者可进入 /me', (await page.pathOnly()) === '/me', `实际 ${await page.path()}`)

  // 越权：作者进不了管理端。前端跳走只是 UX；真正的拒绝在服务端（见 02 的下一行断言）
  await page.navigate('/admin')
  await page.settle(800)
  run.check('作者访问 /admin 被守卫挡回 /me', (await page.pathOnly()) === '/me', `实际 ${await page.path()}`)

  // 这一条才是**安全**断言：即使前端守卫被绕过，服务端也必须拒绝。
  // 它把「前端跳转」和「服务端拒绝」两件事分开验证 —— 只有后者是安全保证。
  const forbidden = await apiGet(page, '/api/users')
  run.check('作者直接打 /api/users 被服务端拒绝（403）', forbidden?.status === 403, `实际 HTTP ${forbidden?.status} body=${JSON.stringify(forbidden?.body)?.slice(0, 120)}`)

  // 已登录访问登录页应被送回
  await page.navigate('/admin/login')
  await page.settle(800)
  run.check('已登录作者访问 /admin/login 被送回 /me', (await page.pathOnly()) === '/me', `实际 ${await page.path()}`)

  // 清空凭证后受保护页应回登录页。
  // ⚠️ 期望是 `/login`（统一登录页）而**不是** `/admin/login`：
  //    后台登录页后来并入了统一登录页，`/admin/login` 只作为旧链接转发存在
  //    （router/index.ts:43）。原脚本写的是 `/admin/login`，已经过期。
  //    详见 01-route-guard 的说明与 docs/14 第 9 条。
  await page.eval('localStorage.clear()')
  await page.navigate('/admin')
  await page.settle(800)
  const afterClear = await page.path()
  run.check(
    '清空凭证后访问 /admin 回到登录页',
    (afterClear ?? '').split('?')[0] === '/login' && (afterClear ?? '').includes('returnUrl=/admin'),
    `实际 ${afterClear}`,
  )

  await page.shot('final-auth-flow')
})
