// ============================================================================
// 05 · 专栏公开页：列表 / 详情 / 导航入口 / 文章页的专栏徽章
//
// 来自 coll.mjs。**但把数据依赖整个换掉了** —— 这是搬进仓库时最必要的一处修改：
//
//   原脚本写死了这些：
//     · 专栏 slug          'csharp-advanced'
//     · 专栏名             「C# 进阶系列」「示例专栏」
//     · 文章标题           「更新标题-标签同步验证」「并发冲突测试」
//     · 文章 UUID          01a07531-b772-7624-8624-123c23236859
//     · 文章数             「2 篇文章」
//
//   那些都是**当时那台机器的开发库**里的数据。换一套种子数据、或者你自己在
//   后台改了个标题，整套检查就红 —— 而代码一行没错。
//   这就是"假红"，它比没有检查更糟：它会训练你忽略红色。
//
//   改法：**先问接口要数据，再断言界面与接口一致**。
//   这样断言不但不依赖具体数据，而且比原来更强 ——
//   原来只验证"标题里出现某几个字"，现在验证"界面和接口说的完全一样"。
// ============================================================================

import { main, login, apiGet } from '../lib/cdp.mjs'
import { config } from '../lib/config.mjs'

await main('05-public-collections', async ({ page, run }) => {
  // 用管理员身份：GET /api/collections 是公开的，但下面要读 includeUnpublished，
  // 而且详情页对未发布专栏的表现不同。统一用管理员，结果可预期。
  await login(page, config.admin)

  const list = await apiGet(page, '/api/collections')
  const collections = list?.body?.data ?? []
  run.note(`接口返回 ${collections.length} 个专栏：${collections.map((c) => c.title).join(' / ') || '（空）'}`)

  if (!Array.isArray(collections) || collections.length === 0) {
    run.skip('开发库里一个专栏都没有 —— 无法验证专栏页面。请先在后台建一个专栏并加入文章。')
    return
  }

  // ── 挑一个「确实有文章」的专栏 ──────────────────────────────────────
  //
  // ⚠️ **不能信列表接口里的 `postCount`。** `GET /api/collections` 的结果是**被缓存的**
  //    （`CollectionService.GetAllAsync` 走 `GetOrCreateAsync`，TTL 约 30 分钟 + 抖动），
  //    而 `GET /api/collections/{slug}`（详情）**不走缓存**。
  //    实测撞到过：列表接口返回 1 个 postCount=0 的专栏，而详情接口对同一个 slug
  //    返回 2 篇文章 —— 于是这个检查会误判成"该专栏下没有文章"而 SKIP，
  //    看起来像环境数据不足，实际是**列表缓存陈旧**。
  //    （清掉 Redis 里的 `blog:taxonomy:collections:v1:published` 后立刻恢复。）
  //
  //    所以这里改成：逐个看一眼**不缓存的详情接口**，以它为准。
  const detailOf = async (slug) => (await apiGet(page, `/api/collections/${encodeURIComponent(slug)}`))?.body?.data

  let target = null
  let detail = null
  for (const c of collections) {
    const d = await detailOf(c.slug)
    if (d && (d.posts?.length ?? 0) > 0) {
      target = c
      detail = d
      break
    }
  }
  if (!target) {
    target = collections[0]
    detail = await detailOf(target.slug)
  }

  // 列表 vs 详情不一致 → 明确说出来（这是"缓存陈旧"的信号，不是页面缺陷）
  if (detail && (detail.posts?.length ?? 0) !== target.postCount) {
    run.note(
      `⚠️ 列表接口与详情接口不一致：「${target.title}」列表说 postCount=${target.postCount}，` +
        `详情说有 ${detail.posts?.length ?? 0} 篇。` +
        `列表接口是**被缓存**的（TTL 约 30 分钟），详情不走缓存 —— 多半是缓存陈旧，不是页面缺陷。`,
    )
  }

  // ── 列表页 ─────────────────────────────────────────────────────────
  await page.navigate('/collections', { ready: 'document.readyState === "complete"' })
  await page.waitFor(`document.body.innerText.includes('专栏')`, { label: '专栏列表页渲染' })
  const listText = await page.text()

  run.check('专栏列表页渲染标题「专栏」', listText.includes('专栏'))
  for (const c of collections) {
    run.check(`列出专栏「${c.title}」`, listText.includes(c.title))
  }
  // 文章数与接口一致（原来写死「2 篇文章」）
  run.check(
    `文章数与接口一致（「${target.title}」= ${target.postCount} 篇）`,
    target.postCount === 0 || new RegExp(`${target.postCount}\\s*篇文章`).test(listText),
    `页面上没找到「${target.postCount} 篇文章」`,
  )
  await page.shot('final-collections')

  // ── 导航入口 ───────────────────────────────────────────────────────
  await page.navigate('/', { ready: 'document.readyState === "complete"' })
  await page.waitFor(`!!document.querySelector('.nav-link')`, { label: '导航栏渲染' })
  const hasNav = await page.eval(
    `[...document.querySelectorAll('.nav-link')].some(a => a.textContent.trim() === '专栏')`,
  )
  run.check('导航栏出现「专栏」入口', hasNav === true)

  // ── 详情页 ─────────────────────────────────────────────────────────
  // detail 已经在上面取过（不缓存的接口），这里不再重复请求
  const posts = detail?.posts ?? []
  run.note(`专栏「${target.slug}」详情接口返回 ${posts.length} 篇文章`)

  await page.navigate(`/collections/${target.slug}`, { ready: 'document.readyState === "complete"' })
  await page.waitForText(target.title, { label: '专栏详情渲染' })
  const detailText = await page.text()

  run.check('专栏详情渲染专栏名', detailText.includes(target.title))
  run.check('含「按专栏顺序阅读」', detailText.includes('按专栏顺序阅读'))

  if (posts.length === 0) {
    run.skip(
      `「${target.title}」下没有已发布文章 —— 跳过文章列表与序号的验证。\n` +
        `        ⚠️ 若你确认它其实有文章，先怀疑**专栏列表缓存陈旧**：\n` +
        `           GET /api/collections 走缓存（TTL 约 30 分钟），详情接口不走。\n` +
        `           清掉即可：docker exec -i redis redis-cli DEL blog:taxonomy:collections:v1:published\n` +
        `           （注意：这条不是"页面有缺陷"，而是测试**取样取到了旧数据**。）`,
    )
  } else {
    for (const p of posts) {
      run.check(`详情列出文章「${p.title}」`, detailText.includes(p.title))
    }
    // 序号必须是 1..N 且顺序与接口一致。
    // 断言「界面 == 接口」而不是写死 1,2 —— 数据换了也不会假红。
    const uiOrders = await page.eval(
      `[...document.querySelectorAll('.order')].map(e => e.textContent.trim())`,
    )
    const expectOrders = posts.map((_, i) => String(i + 1))
    run.check(
      `文章序号为 1..${expectOrders.length}（专栏内排序生效）`,
      JSON.stringify(uiOrders) === JSON.stringify(expectOrders),
      `界面=${JSON.stringify(uiOrders)} 期望=${JSON.stringify(expectOrders)}`,
    )

    // ── 文章详情页的专栏徽章 ─────────────────────────────────────────
    const first = posts[0]
    await page.navigate(`/post/${first.id}`, { ready: 'document.readyState === "complete"' })
    await page.waitFor(`!!document.querySelector('.collection-badge')`, { label: '专栏徽章渲染' })
    const badge = await page.eval(`document.querySelector('.collection-badge')?.textContent.trim() ?? ''`)
    run.check('文章详情显示所属专栏徽章', badge.includes(target.title), `实际「${badge}」`)
    await page.shot('final-post-collection')
  }
})
