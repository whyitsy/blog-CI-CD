// ============================================================================
// 06 · 专栏管理：编排文章顺序并确认**真的持久化到后端**
//
// 来自 coll-manage.mjs。原脚本有一处做法特别好，被完整保留下来：
//
//   它断言「**后端返回的顺序 == 保存前界面上的顺序**」，逐项比对，
//   **不写死标题**。这是把「测试数据」和「测试断言」解耦的正确写法 ——
//   数据换了，断言依然成立，而且断言强度更高。
//
// 搬进仓库时改掉的两处：
//   1. 专栏 slug 从接口发现，不再写死 'csharp-advanced'；
//   2. 顺序改完（并断言）后**尽力恢复原顺序**，避免反复运行把开发数据搅乱。
//      恢复失败只提示、不判失败 —— 它不是被测对象。
// ============================================================================

import { main, login, apiGet, apiSend } from '../lib/cdp.mjs'
import { config } from '../lib/config.mjs'

await main('06-collection-manage', async ({ page, run }) => {
  await login(page, config.admin)

  const list = await apiGet(page, '/api/collections?includeUnpublished=true')
  const collections = list?.body?.data ?? []
  if (!Array.isArray(collections) || collections.length === 0) {
    run.skip('开发库里一个专栏都没有 —— 无法验证编排面板。')
    return
  }
  const target = collections.find((c) => c.postCount >= 2) ?? collections[0]
  run.note(`目标专栏：${target.title}（slug=${target.slug}，${target.postCount} 篇）`)

  // ── 管理端列表页 ───────────────────────────────────────────────────
  await page.navigate('/admin/collections', { ready: 'document.readyState === "complete"' })
  await page.waitForText('专栏', { label: '专栏管理页渲染' })
  const pageText = await page.text()
  run.check('管理端专栏页渲染', pageText.includes('专栏管理') || pageText.includes('新建专栏'))
  run.check('侧边栏有「专栏管理」入口', pageText.includes('专栏管理'))
  for (const c of collections) {
    run.check(`管理端列出专栏「${c.title}」`, pageText.includes(c.title))
  }
  await page.shot('final-admin-collections')

  // ── 打开编排面板 ───────────────────────────────────────────────────
  const opened = await page.eval(`(() => {
    const rows = [...document.querySelectorAll('.row')];
    const r = rows.find(x => x.innerText.includes(${JSON.stringify(target.slug)}));
    if (!r) return { ok: false, reason: 'row-not-found', rows: rows.map(x => x.innerText.slice(0, 40)) };
    const btn = [...r.querySelectorAll('button')].find(b => b.textContent.includes('编排文章'));
    if (!btn) return { ok: false, reason: 'no-button' };
    btn.click(); return { ok: true };
  })()`)
  run.check('打开文章编排面板', opened?.ok === true, JSON.stringify(opened))
  if (opened?.ok !== true) return

  await page.waitFor(`!!document.querySelector('.ordered-item')`, { label: '编排面板加载' })

  // 接口侧的「真相」
  const before = await apiGet(page, `/api/collections/${encodeURIComponent(target.slug)}`)
  const originalOrder = (before?.body?.data?.posts ?? []).map((p) => p.title)
  const originalIds = (before?.body?.data?.posts ?? []).map((p) => p.id)
  const versionBefore = before?.body?.data?.version

  const state = await page.eval(`(() => ({
    ordered: [...document.querySelectorAll('.ordered-item .opt-title')].map(e => e.textContent.trim()),
    options: document.querySelectorAll('.opt').length,
  }))()`)
  run.check(
    `编排面板列出当前的 ${originalOrder.length} 篇（与接口一致）`,
    JSON.stringify(state?.ordered) === JSON.stringify(originalOrder),
    `界面=${JSON.stringify(state?.ordered)}\n        接口=${JSON.stringify(originalOrder)}`,
  )
  run.check('左侧给出可选文章列表', state?.options > 0, `options=${state?.options}`)

  if (originalOrder.length < 2) {
    run.skip('该专栏不足 2 篇文章 —— 无法验证「上移后顺序变化」与持久化。')
    return
  }

  // ── 上移第 2 篇 → 顺序应变化 ───────────────────────────────────────
  await page.eval(
    `(() => {
       const btns = [...document.querySelectorAll('.ordered-item')][1].querySelectorAll('.mini');
       btns[0].click(); return true;
     })()`,
  )
  await page.waitFor(
    `JSON.stringify([...document.querySelectorAll('.ordered-item .opt-title')].map(e => e.textContent.trim()))
     !== ${JSON.stringify(JSON.stringify(originalOrder))}`,
    { label: '顺序发生变化' },
  )
  const after = await page.eval(
    `[...document.querySelectorAll('.ordered-item .opt-title')].map(e => e.textContent.trim())`,
  )
  run.check(
    '上移后界面顺序发生变化',
    JSON.stringify(after) !== JSON.stringify(originalOrder),
    `${JSON.stringify(originalOrder)} -> ${JSON.stringify(after)}`,
  )

  // ── 保存 ───────────────────────────────────────────────────────────
  await page.eval(
    `(() => {
       const b = [...document.querySelectorAll('button')].find(x => x.textContent.includes('保存编排'));
       b.click(); return true;
     })()`,
  )
  // ⚠️ flash 提示只显示 2.5 秒，必须尽快检查 —— 所以这里用短超时的 waitFor，
  //    而不是"等页面静下来再看"。
  let flashed = true
  try {
    await page.waitFor(`/已保存|专栏文章已保存/.test(document.body.innerText)`, {
      label: '保存成功提示',
      timeoutMs: 5000,
    })
  } catch {
    flashed = false
  }
  run.check('保存后给出成功提示', flashed)

  // ── 持久化回读：断言「后端顺序 == 保存前界面上的顺序」 ────────────────
  const persisted = await apiGet(page, `/api/collections/${encodeURIComponent(target.slug)}`)
  const backendOrder = (persisted?.body?.data?.posts ?? []).map((p) => p.title)
  const uiOrder = (after ?? []).map((x) => String(x).trim())
  run.check(
    '顺序已持久化到后端（与界面顺序完全一致）',
    JSON.stringify(backendOrder) === JSON.stringify(uiOrder),
    `界面=${JSON.stringify(uiOrder)}\n        后端=${JSON.stringify(backendOrder)}`,
  )

  await page.shot('final-collection-manage')

  // ── 清理：把顺序恢复成原样（尽力而为）────────────────────────────────
  try {
    const now = await apiGet(page, `/api/collections/${encodeURIComponent(target.slug)}`)
    const version = now?.body?.data?.version
    if (version !== undefined && originalIds.length) {
      const restored = await apiSend(page, 'PUT', `/api/collections/${target.id}/posts`, {
        postIds: originalIds,
        version,
      })
      if (restored?.status === 200) run.note('已把专栏顺序恢复为本次运行前的样子')
      else run.note(`⚠️ 顺序恢复失败（HTTP ${restored?.status}），开发库里的顺序已被本次检查改动`)
    }
  } catch (err) {
    run.note(`⚠️ 顺序恢复出错（不影响结论）：${err.message}`)
  }
  void versionBefore
})
