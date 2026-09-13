// ============================================================================
// 03 · 角色差异：编辑页里「快速新建分类 / 标签」只应给管理员看
//
// 来自 role-ui.mjs。这是**按角色裁剪 UI** 的回归检查：
// 后端允许作者创建分类/标签（不然写作时没法新建），
// 但产品上决定只有管理员能看到那两个快捷入口。
//
// 这类「同一个页面、两种角色、渲染不同」的缺陷，集成测试完全覆盖不到。
// ============================================================================

import { main, loginAndGo } from '../lib/cdp.mjs'
import { config } from '../lib/config.mjs'

const { admin, author } = config

await main('03-role-ui', async ({ page, run }) => {
  // ── 作者：不应看到 ─────────────────────────────────────────────────
  await loginAndGo(page, author, '/me/posts/new', { ready: 'document.readyState === "complete"' })
  await page.settle(1000)
  let text = await page.text()
  run.check('作者看不到「快速新建分类」', !text.includes('快速新建分类'))
  run.check('作者看不到「快速新建标签」', !text.includes('快速新建标签'))

  // ── 管理员：应看到 ─────────────────────────────────────────────────
  await loginAndGo(page, admin, '/admin/posts/new', { ready: 'document.readyState === "complete"' })
  await page.settle(1000)
  text = await page.text()
  run.check('管理员看到「快速新建分类」', text.includes('快速新建分类'))
  run.check('管理员看到「快速新建标签」', text.includes('快速新建标签'))

  await page.shot('final-role-ui')
})
