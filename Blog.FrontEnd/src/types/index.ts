/** 与后端 DTO 对齐的类型定义 */

export interface ApiResponse<T> {
  code: number
  message: string
  data: T
}

export interface PagedResult<T> {
  items: T[]
  /** 后端 PagedResult.Total -> total */
  total: number
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

/** 站点配置的 Key 常量，与后端 SiteConfigKeys 对齐 */
export const SiteConfigKey = {
  SiteName: 'SiteName',
  HeroSubtitles: 'HeroSubtitles',
  FoundingDate: 'FoundingDate',
  HeroBackground: 'HeroBackground',
} as const

export type SiteConfigKeyValue = (typeof SiteConfigKey)[keyof typeof SiteConfigKey]

export interface SiteConfigDto {
  siteName: string
  heroSubtitles: string[]
  heroBackground: string | null
  foundingDate: string | null
  /** 各配置项当前的乐观锁版本号（Key -> Version）；缺失的 Key 表示尚未创建，保存时版本号传 0 */
  versions: Record<string, number>
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

/** 社交链接批量保存请求体（Id/Version 为空表示新增） */
export interface UpsertSocialLinkPayload {
  id?: string | null
  name: string
  icon: string
  url: string
  sortOrder: number
  isVisible: boolean
  version?: number | null
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
  version: number
}

/** 更新博主资料请求体 */
export interface UpdateAuthorPayload {
  name: string
  email: string
  bio: string
  avatar: string
  version: number
}

export interface PostQuery {
  page?: number
  pageSize?: number
  categoryId?: string
  tagId?: string
  keyword?: string
  /** 按专栏过滤 */
  collectionId?: string
  /** 按作者过滤 */
  authorId?: string
  /** 管理后台使用，true 时包含草稿（需登录，Author 角色只能看到自己的） */
  includeUnpublished?: boolean
  /** 作者工作区：只看自己创建的文章 */
  mine?: boolean
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

/* ------------------------------------------------------------------ 认证 */
/**
 * 账号角色。只有两档（见 docs/tech.md §2.4）：
 *  - Admin：站点管理员，管理全部内容、账号、作者与站点配置
 *  - Author：内容作者，只能管理自己的文章与个人资料
 */
export type UserRole = 'Admin' | 'Author'

/**
 * 当前登录用户。与后端 CurrentUserDto / UserDto 对齐。
 * **绝不含 PasswordHash**：后端 DTO 从不返回凭据字段。
 */
export interface AuthUser {
  id: string
  email: string
  role: UserRole
  isActive: boolean
  /** 关联的署名对象（Author）Id，可为 null（如管理员账号未关联作者） */
  authorId: string | null
  /** 关联作者名，用于界面上显示「我」的身份 */
  authorName: string | null
  lastLoginAt: string | null
}

export interface LoginRequest {
  email: string
  password: string
}

/** 登录响应：只发 Access Token，无 Refresh Token（T7） */
export interface LoginResponse {
  token: string
  expiresAt: string
  role: UserRole
  user: AuthUser
}
