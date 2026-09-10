import { del, get, put } from './http'
import type {
  SiteConfigDto,
  SiteStatsDto,
  SocialLinkDto,
  UpsertSocialLinkPayload,
} from '@/types'

/** 站点配置（含各配置项版本号 versions） */
export const getSiteConfig = () => get<SiteConfigDto>('/api/site/config')

/** 更新单个配置项（乐观锁：version 不匹配返回 4090）；Key 不存在时 version 传 0 表示新增 */
export function updateSiteConfig(key: string, value: string, version: number): Promise<SiteConfigDto> {
  return put<SiteConfigDto>('/api/site/config', { key, value, version })
}

/** 社交链接；includeHidden=true 时含隐藏项（管理端配置页） */
export function getSocialLinks(includeHidden = false): Promise<SocialLinkDto[]> {
  return get<SocialLinkDto[]>('/api/site/social-links', { includeHidden })
}

/** 批量保存社交链接（每条更新项必须携带自己的 version，新增项 Id/Version 传 null） */
export function saveSocialLinks(items: UpsertSocialLinkPayload[]): Promise<SocialLinkDto[]> {
  return put<SocialLinkDto[]>('/api/site/social-links', items)
}

export const getSiteStats = () => get<SiteStatsDto>('/api/site/stats')

/** 删除社交链接（软删除，需携带当前 version） */
export function deleteSocialLink(id: string, version: number): Promise<null> {
  return del<null>(`/api/site/social-links/${id}?version=${version}`)
}
