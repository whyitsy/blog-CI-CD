<script setup lang="ts">
import { computed } from 'vue'
import { useSiteStore } from '@/stores/site'
import SocialIcon from '@/components/common/SocialIcon.vue'
import heroBg from '@/assets/hero.png'

const site = useSiteStore()

const subtitles = computed(() => site.config?.heroSubtitles ?? [])
const background = computed(() => site.config?.heroBackground || heroBg)
const visibleLinks = computed(() => site.socialLinks.filter((l) => l.isVisible))

function scrollToList() {
  document.getElementById('post-list')?.scrollIntoView({ behavior: 'smooth' })
}
</script>

<template>
  <section class="hero">
    <!-- 全屏背景图 + 极光遮罩 -->
    <div class="hero-bg" :style="{ backgroundImage: `url(${background})` }" />
    <div class="hero-mask" />
    <div class="aurora-blobs" />

    <div class="hero-content">
      <p class="hero-eyebrow">AURORA · BLOG</p>
      <h1 class="hero-title gradient-text">{{ site.config?.siteName ?? "kky's blog" }}</h1>

      <p class="hero-subtitle">
        <span v-typewriter="subtitles" class="tw-text" />
        <span class="tw-cursor">|</span>
      </p>

      <button class="scroll-down" aria-label="向下翻阅文章" @click="scrollToList">
        <svg viewBox="0 0 24 24" width="26" height="26" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <path d="M12 5v14m0 0 6-6m-6 6-6-6" />
        </svg>
      </button>
    </div>

    <div v-if="visibleLinks.length" class="hero-socials">
      <a
        v-for="link in visibleLinks"
        :key="link.id"
        :href="link.url"
        target="_blank"
        rel="noopener noreferrer"
        class="social-link"
        :title="link.name"
      >
        <SocialIcon :icon="link.icon" :name="link.name" />
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

/* 向下按钮：较大幅度浮动 + 透明度呼吸 */
.scroll-down {
  margin-top: var(--space-16);
  display: grid;
  place-items: center;
  width: 52px;
  height: 52px;
  border-radius: 50%;
  border: 1px solid var(--border-default);
  color: var(--text-default);
  background: rgba(27, 29, 36, 0.35);
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
  bottom: var(--space-10);
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
  width: 42px;
  height: 42px;
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
