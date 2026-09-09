/** 与后端 DTO 对齐的类型定义 */

export interface ApiResponse<T> {
  code: number
  message: string
  data: T
}

export interface PagedResult<T> {
  items: T[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

export interface TagDto {
  id: string
  name: string
  postCount: number
  version: number
}

export interface CategoryDto {
  id: string
  name: string
  postCount: number
  version: number
}

export interface PostListItemDto {
  id: string
  title: string
  summary: string
  coverImage: string
  categoryId: string | null
  categoryName: string | null
  tags: TagDto[]
  publishedAt: string | null
  viewCount: number
  wordCount: number
}

export interface PostDetailDto {
  id: string
  title: string
  content: string
  summary: string
  coverImage: string
  categoryId: string | null
  categoryName: string | null
  tags: TagDto[]
  authorId: string
  authorName: string
  authorAvatar: string
  publishedAt: string | null
  updatedAt: string | null
  viewCount: number
  wordCount: number
  version: number
}

export interface ArchiveItemDto {
  id: string
  title: string
  publishedAt: string
}

export interface ArchiveGroupDto {
  year: number
  month: number
  items: ArchiveItemDto[]
}

export interface SiteConfigDto {
  siteName: string
  heroSubtitles: string[]
  heroBackground: string | null
  foundingDate: string | null
}

export interface SocialLinkDto {
  id: string
  name: string
  icon: string
  url: string
  sortOrder: number
  isVisible: boolean
  version: number
}

export interface SiteStatsDto {
  siteDays: number
  totalPosts: number
  totalWords: number
  totalViews: number
  tagCount: number
  categoryCount: number
}

export interface AuthorDto {
  id: string
  name: string
  email: string
  avatar: string
  bio: string
  createdAt: string
}

export interface PostQuery {
  page?: number
  pageSize?: number
  categoryId?: string
  tagId?: string
  keyword?: string
  /** 管理后台使用，true 时包含草稿 */
  includeUnpublished?: boolean
}

/** 创建/更新文章请求体（与后端 CreatePostRequest / UpdatePostRequest 对齐） */
export interface PostPayload {
  title: string
  content: string
  summary: string
  coverImage: string
  categoryId: string | null
  tagIds: string[]
  /** 创建时可指定是否立即发布；更新时未使用，发布/下架走单独接口 */
  publish?: boolean
  /** 更新时必须携带版本号（由 PostDetailDto.version 提供），创建时可省略 */
  version?: number
}
