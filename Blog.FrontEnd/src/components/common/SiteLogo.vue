<script setup lang="ts">
import { computed } from 'vue'
import { useSiteStore } from '@/stores/site'

/**
 * 站点 Logo —— 全站**唯一**渲染 logo 的地方。
 *
 * 改造前 `k` 这个字符硬编码在 5 个文件里（导航栏 / 页脚 / 管理端侧栏 /
 * 作者工作区侧栏 / 登录页），每处还各写了一份几乎相同的 `.logo-dot` 样式。
 * 现在收敛成一个组件，尺寸差异用 `size` 吸收，是否带发光用 `glow` 吸收
 * （页脚原来就没有 box-shadow）。
 *
 * 两种形态：
 *   · 配置了 `SiteLogo` 图片 → 展示图片
 *   · 未配置 → 渐变圆点 + `LogoName` 文字（缺省 "k"，与改造前的样子一致）
 *
 * 安全说明：图片地址来自站点配置，服务端 MediaPath 白名单已保证它只可能是本站
 * `/api/files/` 路径，这里直接进 `<img src>` 是安全的。
 */
withDefaults(defineProps<{ size?: number; glow?: boolean }>(), {
  size: 32,
  glow: true,
})

const site = useSiteStore()

const logoName = computed(() => site.config?.logoName || 'k')
const logoImage = computed(() => site.config?.siteLogo || '')
</script>

<template>
  <img
    v-if="logoImage"
    class="site-logo"
    :src="logoImage"
    alt=""
    :style="{ '--logo-size': `${size}px` }"
  />
  <span
    v-else
    class="logo-dot"
    :class="{ glow }"
    :style="{ '--logo-size': `${size}px` }"
  >
    {{ logoName }}
  </span>
</template>

<style scoped>
.logo-dot,
.site-logo {
  width: var(--logo-size);
  height: var(--logo-size);
  flex-shrink: 0;
  border-radius: 50%;
}

.logo-dot {
  display: grid;
  place-items: center;
  font-size: calc(var(--logo-size) * 0.5);
  font-weight: 700;
  color: #fff;
  background: linear-gradient(135deg, var(--gradient-start), var(--gradient-mid), var(--gradient-end));
}

.logo-dot.glow {
  box-shadow: var(--glow-purple);
}

.site-logo {
  display: block;
  object-fit: cover;
}
</style>
