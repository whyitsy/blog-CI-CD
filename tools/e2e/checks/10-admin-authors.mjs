// ============================================================================
// 10 · 作者管理：列表、新建，以及作者侧的「个人资料」页
//
// 来自 authors-ui.mjs。搬进仓库时改掉的两处：
//
//   1. 原来断言 `列出 kky`（写死的作者名）→ 改成「界面列出接口返回的第一个作者」，
//      数据换了也不会假红；
//   2. 新建作者的名字/邮箱带本次运行唯一后缀，否则第二次运行必然红。
//
// 特意保留的一条断言：`侧边栏不再有「博主资料」`。
// 那是被删掉的旧入口 —— 这条断言防的是"有人把旧菜单加回来"，
// 属于**回归断言**（断言一件"不应该存在"的事）。这类断言很容易被当成废代码删掉，
// 但它恰好是防回归最便宜的形式。
// ============================================================================

import { main, login, loginAndGo, apiGet, apiSend } from '../lib/cdp.mjs'
import { config, runId } from '../lib/config.mjs'

const stamp = runId()
const AUTHOR_NAME = `E2E 作者 ${stamp}`
const AUTHOR_EMAIL = `e2e-author-${stamp}@example.com`

await main('10-admin-authors', async ({ page, run }) => {
  await login(page, config.admin)

  const existing = await apiGet(page, '/api/authors')
  const authors = existing?.body?.data ?? []
  run.note(`接口返回 ${authors.length} 位作者：${authors.map((a) => a.name).join(' / ') || '（空）'}`)
  if (authors.length === 0) {
    run.skip('开发库里一位作者都没有 —— 请先种入作者数据。')
    return
  }

  // ── 管理端作者列表 ─────────────────────────────────────────────────
  await page.navigate('/admin/authors', { ready: 'document.readyState === "complete"' })
  await page.waitForText('作者', { label: '作者管理页渲染' })
  const text = await page.text()

  run.check('作者管理页渲染', text.includes('作者管理') || text.includes('新建作者'))
  run.check('侧边栏有「作者管理」', text.includes('作者管理'))
  run.check('侧边栏不再有「博主资料」（旧入口已收敛）', !text.includes('博主资料'))
  for (const a of authors) {
    run.check(`列出作者「${a.name}」`, text.includes(a.name))
  }
  await page.shot('final-admin-authors')

  // ── 新建作者 ───────────────────────────────────────────────────────
  const created = await page.eval(`(async () => {
    const setVal = (el, v) => {
      const d = Object.getOwnPropertyDescriptor(el.constructor.prototype, 'value').set;
      d.call(el, v);
      el.dispatchEvent(new Event('input', { bubbles: true }));
    };
    const open = [...document.querySelectorAll('button')].find(b => b.textContent.includes('新建作者'));
    if (!open) return { ok: false, step: 'no-open-button' };
    open.click();
    await new Promise(r => setTimeout(r, 500));
    const name = [...document.querySelectorAll('input')].find(i => (i.placeholder || '').includes('作者名'));
    const email = [...document.querySelectorAll('input')].find(i => (i.placeholder || '').includes('@example.com'));
    if (!name || !email) return { ok: false, step: 'fields-not-found',
      placeholders: [...document.querySelectorAll('input')].map(i => i.placeholder) };
    setVal(name, ${JSON.stringify(AUTHOR_NAME)}); setVal(email, ${JSON.stringify(AUTHOR_EMAIL)});
    await new Promise(r => setTimeout(r, 300));
    const submit = [...document.querySelectorAll('button')].find(b => b.textContent.trim() === '创建');
    if (!submit) return { ok: false, step: 'no-submit' };
    submit.click();
    await new Promise(r => setTimeout(r, 2200));
    return { ok: true, text: document.body.innerText };
  })()`)
  run.check(
    '新建作者成功并出现在列表',
    created?.ok === true && typeof created.text === 'string' && created.text.includes(AUTHOR_NAME),
    JSON.stringify(created?.step ?? created?.text?.slice(0, 120) ?? ''),
  )
  if (created?.ok) await page.shot('final-admin-authors-created')

  // ── 作者侧的「个人资料」──────────────────────────────────────────────
  await loginAndGo(page, config.author, '/me/profile', { ready: 'document.readyState === "complete"' })
  await page.waitFor(`!!document.querySelector('form input')`, { label: '个人资料表单加载' })
  const profileText = await page.text()
  run.check('作者个人资料页渲染', profileText.includes('个人资料'))
  run.check('顶栏有「个人资料」入口', profileText.includes('个人资料'))
  run.check('表单已加载（账号已关联作者）', (await page.eval(`!!document.querySelector('form input')`)) === true)
  await page.shot('final-me-profile')

  // ── 清理：删掉本次创建的作者（尽力而为）──────────────────────────────
  try {
    await login(page, config.admin)
    const after = await apiGet(page, '/api/authors')
    const hit = (after?.body?.data ?? []).find((a) => a.email === AUTHOR_EMAIL)
    if (hit?.id) {
      // ⚠️ 同 04：DELETE 需要 `?version=N`，漏了就是 400（乐观锁适用于删除）。
      //    AuthorDto 里本来就带 Version，直接用。
      const del = await apiSend(page, 'DELETE', `/api/authors/${hit.id}?version=${hit.version}`)
      if (del?.status === 200 || del?.status === 204) run.note(`已清理本次创建的测试作者（${hit.id}）`)
      else run.note(`⚠️ 清理未成功（HTTP ${del?.status}，version=${hit.version}），开发库里会留下「${AUTHOR_NAME}」`)
    } else {
      run.note(`⚠️ 没在作者列表里找到 ${AUTHOR_EMAIL}，未清理`)
    }
  } catch (err) {
    run.note(`⚠️ 清理过程出错（不影响结论）：${err.message}`)
  }
})
