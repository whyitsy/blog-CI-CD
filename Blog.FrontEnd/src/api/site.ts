import { get } from './http'
import type { AuthorDto, CategoryDto, SiteConfigDto, SiteStatsDto, SocialLinkDto, TagDto } from '@/types'

export const getCategories = () => get<CategoryDto[]>('/api/categories')
export const getTags = () => get<TagDto[]>('/api/tags')
export const getSiteConfig = () => get<SiteConfigDto>('/api/site/config')
export const getSocialLinks = () => get<SocialLinkDto[]>('/api/site/social-links')
export const getSiteStats = () => get<SiteStatsDto>('/api/site/stats')
export const getAuthors = () => get<AuthorDto[]>('/api/authors')
