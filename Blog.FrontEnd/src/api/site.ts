import { get } from './http'
import type { AuthorDto, SiteConfigDto, SiteStatsDto, SocialLinkDto } from '@/types'

export const getSiteConfig = () => get<SiteConfigDto>('/api/site/config')
export const getSocialLinks = () => get<SocialLinkDto[]>('/api/site/social-links')
export const getSiteStats = () => get<SiteStatsDto>('/api/site/stats')
export const getAuthors = () => get<AuthorDto[]>('/api/authors')