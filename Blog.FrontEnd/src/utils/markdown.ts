import DOMPurify from 'dompurify'
import { marked } from 'marked'

/**
 * Markdown → 安全的 HTML。
 *
 * <h3>为什么要把 sanitize 从视图里抽出来</h3>
 * 这段逻辑以前直接写在 PostDetailView.vue 里，两件事混在一起：
 * 「Markdown 怎么渲染」和「哪些 HTML 允许留下」。抽成模块之后：
 *   · 去重逻辑只在一个地方注册，不会因为视图组件被多次挂载 / HMR 而重复注册；
 *   · 将来如果预览、评论、摘要也要渲染 Markdown，可以直接复用同一套白名单，
 *     不会出现「正文干净了、预览还脏着」的漏网。
 *
 * <h3>为什么要剥掉内联 style</h3>
 * 文章的富文本常常是从别的编辑器**粘贴**进来的，会夹带一堆内联样式，例如：
 *
 * ```html
 * <font style="color:rgb(15, 17, 21);">是一个</font>
 * ```
 *
 * `rgb(15, 17, 21)` 是近黑色。亮色主题下它和默认文字色差不多，看不出问题；
 * 一旦切到暗色主题（深底浅字），这段字就变成**黑底黑字**，完全读不了。
 * 根因不是 CSS 变量没适配——正文排版全部用的是 `var(--text-*)`，
 * 而是这些内联样式**优先级高于**主题变量，把主题覆盖掉了。
 *
 * 顺带还堵掉一类问题：内联样式本身就是 CSS 注入面，
 * 例如 `background:url(//evil.com/track)` 可以在不执行任何 JS 的情况下做追踪像素。
 *
 * 处理策略是**只剥属性、不删内容**：
 *   · `FORBID_ATTR: ['style']` —— 所有内联样式一律丢弃
 *   · `<font>` 这类纯样式包装标签做 **unwrap**（保留子节点、丢掉标签本身），
 *     避免留下一层没有意义的嵌套，影响 `:deep(p)` 之类的排版选择器命中
 *
 * `<span>` / `<div>` 不做 unwrap：它们可能是作者有意留下的锚点结构，
 * 而 style 已经被剥掉，留着也不会再干扰主题。
 */

let hooksRegistered = false

function registerHooks(): void {
  // 模块级去重：addHook 是全局的，重复注册会让同一个节点被处理多次
  if (hooksRegistered) return
  hooksRegistered = true

  DOMPurify.addHook('uponSanitizeElement', (node, data) => {
    if (data.tagName !== 'font') return

    const element = node as Element
    const parent = element.parentNode
    if (!parent) return

    // unwrap：把子节点搬到父节点上，再把自己移除
    while (element.firstChild) {
      parent.insertBefore(element.firstChild, element)
    }
    parent.removeChild(element)
  })
}

/** 把 Markdown 渲染成可安全 `v-html` 的 HTML 字符串 */
export function renderMarkdown(content: string): string {
  if (!content) return ''

  registerHooks()

  // 用 marked 的同步模式：异步模式返回 Promise，v-html 拿不到字符串
  const raw = marked.parse(content, { async: false }) as string

  return DOMPurify.sanitize(raw, {
    // 标题锚点：目录（TOC）靠 id 跳转，DOMPurify 默认会把它剥掉
    ADD_ATTR: ['id'],
    // 内联样式一律不放行，理由见文件头注释
    FORBID_ATTR: ['style'],
  })
}
