<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useSiteStore } from '@/stores/site'
import SocialIcon from '@/components/common/SocialIcon.vue'

const site = useSiteStore()

const subtitles = computed(() => site.config?.heroSubtitles ?? [])
const backgrounds = computed(() => site.config?.heroBackgrounds ?? [])
const visibleLinks = computed(() => site.socialLinks.filter((l) => l.isVisible))

/**
 * 首屏背景：从后台配置的多张图里**随机挑一张**。
 *
 * 两个细节：
 *   · 用 watch + immediate 而不是 onMounted —— 站点配置是异步拉的，
 *     HeroSection 挂载时 config 往往还是 null，写死在 mounted 里会永远选不到图。
 *   · 只在「当前这张不在新列表里」时才重新抽 —— 否则 site.refreshAll() 之类的
 *     刷新会把访客正在看的背景换掉。
 *
 * 背景图与内置渐变**二选一**（见模板）：
 *   · 抽到图 → 渲染 .hero-bg + .hero-mask，不渲染 .aurora-blobs
 *   · 列表为空、pickedBackground 为空串 → 不渲染图片层与遮罩，只留 .aurora-blobs 的渐变
 * 两者条件必须互斥：三个层都是 position:absolute 且同 z-index，同时存在会按 DOM 顺序
 * 把渐变叠在照片上，观感发花。
 */
const pickedBackground = ref('')

watch(
  backgrounds,
  (list) => {
    if (!list.length) {
      pickedBackground.value = ''
      return
    }
    if (!list.includes(pickedBackground.value)) {
      pickedBackground.value = list[Math.floor(Math.random() * list.length)]
    }
  },
  { immediate: true },
)

function scrollToList() {
  document.getElementById('post-list')?.scrollIntoView({ behavior: 'smooth' })
}
</script>

<template>
  <section class="hero">
    <!-- 背景图与内置渐变二选一：配了图只留图 + 遮罩；没配图才用 .aurora-blobs 的渐变本体 -->
    <div v-if="pickedBackground" class="hero-bg" :style="{ backgroundImage: `url(${pickedBackground})` }" />
    <div v-if="pickedBackground" class="hero-mask" />
    <div v-else class="aurora-blobs" />

    <div class="hero-content">
      <p class="hero-eyebrow">AURORA · BLOG</p>
      <h1 class="hero-title gradient-text">{{ site.config?.siteName ?? "kky's blog" }}</h1>

      <p class="hero-subtitle">
        <span v-typewriter="subtitles" class="tw-text" />
        <span class="tw-cursor">|</span>
      </p>

      <!-- 有可见文字就不再需要 aria-label：否则读屏会把同一句话念两遍 -->
      <button class="scroll-down" @click="scrollToList">
        <span>向下翻阅文章</span>
        <svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2"
          stroke-linecap="round" stroke-linejoin="round">
          <path d="M12 5v14m0 0 6-6m-6 6-6-6" />
        </svg>
      </button>
    </div>

    <div v-if="visibleLinks.length" class="hero-socials">
      <a v-for="link in visibleLinks" :key="link.id" :href="link.url" target="_blank" rel="noopener noreferrer"
        class="social-link" :title="link.name">
        <SocialIcon :icon="link.icon" :name="link.name" :size="26" />
      </a>
    </div>
  </section>
</template>

<style scoped>
.hero {
  position: relative;
  height: 100vh;
  min-height: 560px;
  display: flex;
  align-items: center;
  justify-content: center;
  overflow: hidden;
}

.hero-bg {
  position: absolute;
  inset: 0;
  background-size: cover;
  background-position: center;
  transform: scale(1.05);
}

.hero-mask {
  position: absolute;
  inset: 0;
  background: linear-gradient(180deg, rgba(19, 20, 26, 0.55) 0%, rgba(19, 20, 26, 0.35) 45%, var(--bg-canvas) 100%);
}

[data-theme='light'] .hero-mask {
  background: linear-gradient(180deg, rgba(241, 243, 249, 0.45) 0%, rgba(241, 243, 249, 0.3) 45%, var(--bg-canvas) 100%);
}

.hero-content {
  position: relative;
  z-index: 2;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
  padding: 0 var(--space-6);
}

.hero-eyebrow {
  font: var(--text-caption);
  letter-spacing: 4px;
  color: var(--text-muted);
  margin-bottom: var(--space-4);
}

.hero-title {
  font: var(--text-display);
  margin-bottom: var(--space-5);
  /* 主文本轻微上下浮动 */
  animation: float-title 3s ease-in-out infinite;
}

.hero-subtitle {
  display: flex;
  align-items: baseline;
  min-height: 28px;
  font-size: 17px;
  color: var(--text-default);
  text-shadow: 0 1px 8px rgba(0, 0, 0, 0.35);
}

.tw-cursor {
  margin-left: 2px;
  color: var(--brand-500);
  animation: blink 1s step-end infinite;
}

/* 向下按钮：从纯图标圆形改为「图标 + 文案」胶囊，保留浮动呼吸动画。
   边框与底色沿用此前手工调整过的值（3px / 浅灰半透明）。 */
.scroll-down {
  margin-top: var(--space-16);
  display: inline-flex;
  align-items: center;
  gap: var(--space-2);
  height: 44px;
  padding: 0 var(--space-5);
  border-radius: var(--radius-2xl);
  border: 3px solid var(--border-default);
  font-size: 14px;
  font-weight: 500;
  color: var(--text-default);
  background: rgba(208, 211, 224, 0.35);
  backdrop-filter: blur(6px);
  animation: float-btn 2.4s ease-in-out infinite;
  transition: border-color var(--transition-fast), color var(--transition-fast);
}

.scroll-down:hover {
  border-color: var(--brand-500);
  color: var(--brand-500);
}

.hero-socials {
  position: absolute;
  bottom: var(--space-16);
  left: 0;
  right: 0;
  z-index: 2;
  display: flex;
  justify-content: center;
  gap: var(--space-4);
}

.social-link {
  display: grid;
  place-items: center;
  width: 52px;
  height: 52px;
  border-radius: 50%;
  border: 1px solid var(--border-default);
  color: var(--text-muted);
  background: rgba(27, 29, 36, 0.35);
  backdrop-filter: blur(6px);
  transition: color var(--transition-fast), border-color var(--transition-fast), transform var(--transition-fast), box-shadow var(--transition-fast);
}

.social-link:hover {
  color: var(--brand-500);
  border-color: var(--brand-500);
  transform: translateY(-3px);
  box-shadow: var(--glow-cyan);
}

@keyframes float-title {
  0%,
  100% {
    transform: translateY(0);
  }

  50% {
    transform: translateY(-6px);
  }
}

@keyframes float-btn {
  0%,
  100% {
    transform: translateY(0);
    opacity: 1;
  }

  50% {
    transform: translateY(14px);
    opacity: 0.35;
  }
}

@keyframes blink {
  0%,
  100% {
    opacity: 1;
  }

  50% {
    opacity: 0;
  }
}
</style>
