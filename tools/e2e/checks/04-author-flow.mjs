// ============================================================================
// 04 · 作者自助发文：/me/posts/new 走完整条「填表 → 创建 → 回到列表」
//
// 来自 author-flow.mjs。这是 docs/09 §1 里那条「前端表单保存链路缺自动化验证」
// 里**唯一被自动化掉的一段** —— 其余（校验提示、封面上传…）仍然靠人眼。
//
// ⚠️ 这个检查**会写入开发库**（真的创建一篇文章），所以：
//   1. 标题带本次运行唯一后缀，多次运行不会互相干扰；
//   2. 断言完成后**尽力清理**（删掉刚创建的文章）。
//      清理失败不算失败 —— 它不是被测的对象，不该产生假红。
// ============================================================================

import { main, login, loginAndGo, apiGet, apiSend } from '../lib/cdp.mjs'
import { config, runId } from '../lib/config.mjs'

const { author, admin } = config

// 本次运行唯一的标题 —— 这是「可重复运行」的关键（见 README）
const TITLE = `E2E 作者自助发文 ${runId()}`

await main('04-author-flow', async ({ page, run }) => {
  const loginRes = await login(page, author)
  run.check('作者登录', loginRes.ok === true, JSON.stringify(loginRes))

  await page.navigate('/me/posts/new', { ready: 'document.readyState === "complete"' })
  await page.settle(600)
  run.check('作者可直接打开 /me/posts/new', (await page.pathOnly()) === '/me/posts/new', `实际 ${await page.path()}`)

  // 填表。
  //
  // ⚠️⚠️ 这里有一个**原脚本就有、而且真的会咬人**的坑，值得单独写清楚：
  //
  //   原脚本用 `placeholder` 里的中文来找输入框：
  //       t.placeholder.includes('Markdown') || t.placeholder.includes('正文')
  //
  //   而**摘要框的 placeholder 是「可选：留空将自动取正文前 50 字」——
  //   它含「正文」两个字**，而且它在 DOM 里排在正文框**前面**。
  //   于是 `find` 命中的是摘要框：正文一个字都没填进去。
  //
  //   失败时的表现极具误导性：点击「创建」后页面停在原地，
  //   提示「**内容不能为空**」—— 看起来像前端的必填校验坏了，
  //   实际上是测试脚本填错了框。
  //
  //   所以这里改成**按 rows 选**：正文框 rows=14、摘要框 rows=3。
  //   这是个与文案无关的结构性判据，placeholder 再怎么改都不会失效。
  //
  //   （另一个坑保留：标题 input **没有 type 属性**，`input[type=text]` 选不到它。）
  const filled = await page.eval(`(() => {
    const setVal = (el, v) => {
      const d = Object.getOwnPropertyDescriptor(el.constructor.prototype, 'value').set;
      d.call(el, v);
      el.dispatchEvent(new Event('input', { bubbles: true }));
    };
    const title = [...document.querySelectorAll('input')].find(i => (i.placeholder || '').includes('标题'));
    const areas = [...document.querySelectorAll('textarea')].sort((a, b) => b.rows - a.rows);
    const area = areas[0];
    if (!title || !area) return { ok: false, title: !!title, area: !!area,
      inputs: [...document.querySelectorAll('input')].map(i => i.placeholder) };
    setVal(title, ${JSON.stringify(TITLE)});
    setVal(area, '由 E2E 检查创建的正文，用于验证作者可以自助发文。');
    // 回读一次，确认**真的落到了正文框**而不是别的框 —— 免得再被同一个坑咬第二次
    return { ok: true, titleValue: title.value, areaRows: area.rows, areaLen: area.value.length,
      allTextareas: areas.map(t => ({ rows: t.rows, len: t.value.length })) };
  })()`)
  run.check('定位并填写标题与正文', filled?.ok === true, JSON.stringify(filled))

  const clicked = await page.eval(`(() => {
    const b = [...document.querySelectorAll('button')].find(x => x.textContent.trim() === '创建');
    if (!b) return { ok: false, buttons: [...document.querySelectorAll('button')].map(x => x.textContent.trim()) };
    b.click(); return { ok: true };
  })()`)
  run.check('点击「创建」', clicked?.ok === true, JSON.stringify(clicked))

  // 提交后应回到「我的文章」列表，且列表里出现新文章。
  // 用 waitForText 而不是固定 sleep：创建成功就立刻继续。
  let listed = true
  try {
    await page.waitFor(`location.pathname === '/me'`, { label: '回到 /me' })
    await page.waitForText(TITLE, { timeoutMs: 20000 })
  } catch (err) {
    listed = false
    run.check('提交后回到我的文章列表并出现新文章', false, String(err.message))
  }
  if (listed) run.check('提交后回到我的文章列表并出现新文章', true)

  // 作者进不了管理端
  await page.navigate('/admin/posts/new')
  await page.settle(800)
  run.check('作者访问 /admin/posts/new 被挡回 /me', (await page.pathOnly()) === '/me', `实际 ${await page.path()}`)

  await page.shot('final-author-flow')

  // ── 清理（尽力而为）────────────────────────────────────────────────
  // 先以作者身份查列表找到 id，再删。删不掉只提示，不算失败。
  try {
    await page.navigate('/me')
    // 列表接口按标题过滤：用 admin 的列表接口更稳（作者的 /api/posts 只看自己的，
    // 但这里作者自己就是作者，所以用作者身份即可）
    const found = await apiGet(page, `/api/posts?page=1&pageSize=50`)
    const items = found?.body?.data?.items ?? found?.body?.data ?? []
    const hit = (Array.isArray(items) ? items : []).find((p) => p.title === TITLE)
    if (hit?.id) {
      // ⚠️ DELETE 也要带乐观锁版本号（`?version=N`），和 PUT 一样。
      //    漏了它后端返回 400 —— 清理会静默失败、测试数据越积越多。
      //    版本号要从**详情**接口取，列表项里不一定有。
      const detail = await apiGet(page, `/api/posts/${hit.id}`)
      const version = detail?.body?.data?.version
      const del = await apiSend(page, 'DELETE', `/api/posts/${hit.id}?version=${version}`)
      if (del?.status === 200 || del?.status === 204) run.note(`已清理本次创建的测试文章（${hit.id}）`)
      else run.note(`⚠️ 清理未成功（HTTP ${del?.status}，version=${version}），开发库里会留下《${TITLE}》`)
    } else {
      run.note(`⚠️ 没在列表接口里找到《${TITLE}》，未清理`)
    }
  } catch (err) {
    run.note(`⚠️ 清理过程出错（不影响结论）：${err.message}`)
  }
})
