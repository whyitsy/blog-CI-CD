import { defineStore } from 'pinia'
import { getSiteConfig, getSiteStats, getSocialLinks } from '@/api/site'
import type { SiteConfigDto, SiteStatsDto, SocialLinkDto } from '@/types'

/** 站点配置 / 社交链接 / Footer 统计（缓存于 store，全站只拉一次） */
export const useSiteStore = defineStore('site', {
  state: () => ({
    config: null as SiteConfigDto | null,
    socialLinks: [] as SocialLinkDto[],
    stats: null as SiteStatsDto | null,
    loaded: false,
  }),
  actions: {
    async ensureLoaded() {
      if (this.loaded) return
      const [config, socialLinks, stats] = await Promise.all([
        getSiteConfig().catch(() => null),
        getSocialLinks().catch(() => [] as SocialLinkDto[]),
        getSiteStats().catch(() => null),
      ])
      this.config = config
      this.socialLinks = socialLinks
      this.stats = stats
      this.loaded = true
    },
    /** 浏览量等数据会变化，Footer 需要时可刷新 */
    async refreshStats() {
      this.stats = await getSiteStats().catch(() => this.stats)
    },
  },
})
