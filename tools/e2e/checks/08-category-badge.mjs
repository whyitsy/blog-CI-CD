// ============================================================================
// 08 · 首页卡片上的分类徽章：必须是「渐变实心」，且与标签视觉可区分
//
// 来自 cat-badge.mjs。这是一条**视觉断言**，而视觉断言正是自动化里最容易写歪的一类：
// 它断言的不是"好看"，而是两条**可判定**的规则：
//   1. 分类徽章用了渐变（`backgroundImage` 含 gradient），标签没有 —— 两者不是同一个样式；
//   2. 分类徽章可点击并跳到按分类筛选的地址。
//
// 剩下真正主观的东西（够不够好看）依然只能靠人眼 —— 这点在 tools/e2e/README.md 里写明。
//
// 数据依赖：需要**至少一篇带分类的文章**。从接口里现找，找不到就如实跳过。
// ============================================================================

import { main, login, apiGet } from '../lib/cdp.mjs'
import { config } from '../lib/config.mjs'

await main('08-category-badge', async ({ page, run }) => {
  await login(page, config.admin)

  const list = await apiGet(page, '/api/posts?page=1&pageSize=50')
  const items = list?.body?.data?.items ?? list?.body?.data ?? []
  const withCategory = (Array.isArray(items) ? items : []).filter((p) => p.categoryId || p.category)
  run.note(`接口返回 ${Array.isArray(items) ? items.length : 0} 篇文章，其中 ${withCategory.length} 篇带分类`)

  if (withCategory.length === 0) {
    run.skip('没有带分类的文章 —— 无法验证分类徽章。请先给某篇文章设置分类。')
    return
  }

  await page.navigate('/', { ready: 'document.readyState === "complete"' })
  await page.waitFor(`!!document.querySelector('.category-badge')`, { label: '首页卡片渲染分类徽章' })
  await page.eval(`document.getElementById('post-list')?.scrollIntoView()`)
  await page.settle(400)

  const info = await page.eval(`(() => {
    const cat = document.querySelector('.category-badge');
    const tag = document.querySelector('.tag-badge');
    const cs = el => el ? getComputedStyle(el) : null;
    const c = cs(cat), g = cs(tag);
    return {
      hasCat: !!cat, catText: cat?.textContent.trim(), catHref: cat?.getAttribute('href'),
      catBg: c?.backgroundImage?.slice(0, 60),
      hasTag: !!tag, tagBg: g?.backgroundImage?.slice(0, 60),
      sameBg: c && g ? c.backgroundImage === g.backgroundImage : null,
    };
  })()`)

  run.check('首页卡片渲染出分类徽章', info?.hasCat === true, JSON.stringify(info))
  run.check(
    '分类徽章是渐变实心',
    typeof info?.catBg === 'string' && info.catBg.includes('gradient'),
    `backgroundImage = ${info?.catBg}`,
  )
  run.check(
    '分类与标签视觉可区分',
    info?.sameBg === false,
    `分类=${info?.catBg}\n        标签=${info?.tagBg}`,
  )
  run.check(
    '分类徽章可点击跳筛选页',
    typeof info?.catHref === 'string' && info.catHref.includes('categoryId'),
    `href = ${info?.catHref}`,
  )

  await page.shot('final-category-badge')
})
