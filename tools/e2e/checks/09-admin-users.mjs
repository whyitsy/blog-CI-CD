// ============================================================================
// 09 · 账号管理：列表、自我保护、创建、以及重复邮箱的报错
//
// 来自 users-ui.mjs。搬进仓库时改掉的关键一处：
//
//   原脚本创建的邮箱是写死的 `uitest@example.com`。
//   → **第二次运行必然失败**（那个邮箱已经存在了），
//     然后你会得出"这脚本过期了"的结论，把它删掉。
//   → 现在邮箱带本次运行唯一后缀（config.runId()），跑多少次都一样。
//
// ⚠️ 已知副作用：`/api/users` **没有删除接口**（只有 disable，见 UsersController），
//    所以每次运行都会在开发库里留下一个测试账号。这是刻意接受的成本 ——
//    它们不影响任何断言，而且禁用比删除更接近真实运营行为。
// ============================================================================

import { main, login } from '../lib/cdp.mjs'
import { config, runId } from '../lib/config.mjs'

const stamp = runId()
const NEW_EMAIL = `e2e-user-${stamp}@example.com`
const DUP_EMAIL = `e2e-dup-${stamp}@example.com`
const PASSWORD = 'E2eTest@12345'

/** 打开新建表单并提交一个账号，返回是否成功走完流程 */
const CREATE_HELPER = `async (email) => {
  const setVal = (el, v) => {
    const d = Object.getOwnPropertyDescriptor(el.constructor.prototype, 'value').set;
    d.call(el, v);
    el.dispatchEvent(new Event('input', { bubbles: true }));
  };
  const openBtn = [...document.querySelectorAll('button')].find(b => b.textContent.includes('新建账号'));
  if (openBtn && openBtn.textContent.includes('新建')) { openBtn.click(); await new Promise(r => setTimeout(r, 400)); }
  const emailEl = [...document.querySelectorAll('input')].find(i => i.placeholder === 'author@example.com');
  const pwdEl = [...document.querySelectorAll('input[type=password]')].find(i => (i.placeholder || '').includes('8 位'));
  if (!emailEl || !pwdEl) return { ok: false, reason: 'fields-not-found' };
  setVal(emailEl, email); setVal(pwdEl, ${JSON.stringify(PASSWORD)});
  await new Promise(r => setTimeout(r, 300));
  const submit = [...document.querySelectorAll('button')].find(b => b.textContent.trim() === '创建');
  if (!submit) return { ok: false, reason: 'no-submit' };
  submit.click();
  await new Promise(r => setTimeout(r, 2200));
  return { ok: true, text: document.body.innerText };
}`

await main('09-admin-users', async ({ page, run }) => {
  await login(page, config.admin)
  await page.navigate('/admin/users', { ready: 'document.readyState === "complete"' })
  await page.waitFor(`location.pathname === '/admin/users' || location.pathname === '/login'`, {
    label: '进入账号管理页',
  })
  await page.settle(800)

  run.check('管理员可打开 /admin/users', (await page.pathOnly()) === '/admin/users', `实际 ${await page.path()}`)

  const text = await page.text()
  run.check('列出既有账号（含管理员自己）', text.includes(config.admin.email))
  run.check('标识出「我自己」', text.includes('我自己'))
  run.check('侧边栏有「账号管理」入口', text.includes('账号管理'))

  // 自我保护：自己的那一行不能有「停用」按钮。
  // （原脚本这里断言的是"含「我自己」的行数 === 1"，那件事其实是在数行数，
  //   和"不能停用自己"无关 —— 换成直接断言按钮不存在。）
  const selfRow = await page.eval(`(() => {
    const row = [...document.querySelectorAll('.row')].find(r => r.innerText.includes('我自己'));
    if (!row) return { found: false };
    const hasDisable = [...row.querySelectorAll('button')].some(b => b.textContent.includes('停用'));
    return { found: true, hasDisable, buttons: [...row.querySelectorAll('button')].map(b => b.textContent.trim()) };
  })()`)
  run.check('「我自己」那一行不提供「停用」按钮', selfRow?.found === true && selfRow.hasDisable === false, JSON.stringify(selfRow))

  // ── 创建账号 ───────────────────────────────────────────────────────
  const created = await page.eval(`(${CREATE_HELPER})(${JSON.stringify(NEW_EMAIL)})`)
  run.check('打开新建表单并提交', created?.ok === true, JSON.stringify(created))
  run.check('新账号出现在列表', typeof created?.text === 'string' && created.text.includes(NEW_EMAIL), NEW_EMAIL)

  await page.shot('final-admin-users')

  // ── 重复邮箱必须给出明确提示 ───────────────────────────────────────
  const dup = await page.eval(`(${CREATE_HELPER})(${JSON.stringify(DUP_EMAIL)})`)
  run.check('第一次创建成功（为重复验证准备数据）', dup?.ok === true && dup.text?.includes(DUP_EMAIL), DUP_EMAIL)

  const dupAgain = await page.eval(`(${CREATE_HELPER})(${JSON.stringify(DUP_EMAIL)})`)
  const dupText = typeof dupAgain?.text === 'string' ? dupAgain.text : ''
  run.check(
    '重复邮箱给出明确提示（不是静默失败）',
    dupText.includes('已被占用'),
    dupText.split('\n').find((l) => l.includes('占用')) ?? dupText.slice(0, 120).replace(/\n/g, ' / '),
  )
})
