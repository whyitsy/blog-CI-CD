import type { Directive } from 'vue'

/**
 * v-typewriter="['文案一', '文案二']"
 * 循环打字 → 停留 → 删除 → 下一句。元素内需含 .tw-text 子节点或使用元素本身。
 */
interface TypewriterState {
  timer: number
  observer: MutationObserver | null
}

const states = new WeakMap<HTMLElement, TypewriterState>()

function start(el: HTMLElement, phrases: string[]) {
  stop(el)
  if (!phrases.length) return

  let phraseIdx = 0
  let charIdx = 0
  let deleting = false
  let cancelled = false

  const tick = () => {
    if (cancelled) return
    const phrase = phrases[phraseIdx % phrases.length] ?? ''

    if (!deleting) {
      charIdx++
      el.textContent = phrase.slice(0, charIdx)
      if (charIdx >= phrase.length) {
        deleting = true
        states.get(el)!.timer = window.setTimeout(tick, 2200) // 完整停留
        return
      }
      states.get(el)!.timer = window.setTimeout(tick, 90 + Math.random() * 60)
    } else {
      charIdx--
      el.textContent = phrase.slice(0, charIdx)
      if (charIdx <= 0) {
        deleting = false
        phraseIdx++
        states.get(el)!.timer = window.setTimeout(tick, 500)
        return
      }
      states.get(el)!.timer = window.setTimeout(tick, 40)
    }
  }

  states.set(el, { timer: window.setTimeout(tick, 300), observer: null })
  void cancelled
}

function stop(el: HTMLElement) {
  const state = states.get(el)
  if (state) {
    clearTimeout(state.timer)
    state.observer?.disconnect()
    states.delete(el)
  }
}

export const typewriter: Directive<HTMLElement, string[]> = {
  mounted(el, binding) {
    start(el, Array.isArray(binding.value) ? binding.value : [String(binding.value)])
  },
  updated(el, binding) {
    if (JSON.stringify(binding.value) !== JSON.stringify(binding.oldValue)) {
      start(el, Array.isArray(binding.value) ? binding.value : [String(binding.value)])
    }
  },
  unmounted(el) {
    stop(el)
  },
}
