import { del, get, post, put } from './http'
import type { CollectionDetailDto, CollectionDto, CollectionPayload } from '@/types'

/**
 * 专栏接口。
 * 读接口公开（前台专栏页）；写接口仅管理员（与分类/标签一致）。
 */

/** 专栏列表。includeUnpublished=true 仅管理员可用 */
export function getCollections(includeUnpublished = false): Promise<CollectionDto[]> {
  return get<CollectionDto[]>('/api/collections', { includeUnpublished })
}

/** 按 slug 取专栏详情（含已发布文章）。未发布的专栏对匿名返回 404 */
export function getCollectionBySlug(slug: string): Promise<CollectionDetailDto> {
  return get<CollectionDetailDto>(`/api/collections/${encodeURIComponent(slug)}`)
}

/** 管理端按 id 取详情（含未发布文章） */
export function getCollectionById(id: string): Promise<CollectionDetailDto> {
  return get<CollectionDetailDto>(`/api/collections/id/${id}`)
}

export function createCollection(payload: CollectionPayload): Promise<CollectionDto> {
  return post<CollectionDto>('/api/collections', {
    title: payload.title,
    slug: payload.slug,
    description: payload.description,
    coverImage: payload.coverImage,
    sortOrder: payload.sortOrder,
    isPublished: payload.isPublished,
  })
}

/** 更新专栏（乐观锁：必须携带 version） */
export function updateCollection(id: string, payload: CollectionPayload): Promise<CollectionDto> {
  if (payload.version == null) throw new Error('更新专栏必须携带版本号')
  return put<CollectionDto>(`/api/collections/${id}`, {
    title: payload.title,
    slug: payload.slug,
    description: payload.description,
    coverImage: payload.coverImage,
    sortOrder: payload.sortOrder,
    isPublished: payload.isPublished,
    version: payload.version,
  })
}

export function deleteCollection(id: string, version: number): Promise<null> {
  return del<null>(`/api/collections/${id}?version=${version}`)
}

/** 整体设置专栏内文章与顺序（数组顺序即专栏内排序） */
export function setCollectionPosts(
  id: string,
  postIds: string[],
  version: number,
): Promise<CollectionDetailDto> {
  return put<CollectionDetailDto>(`/api/collections/${id}/posts`, { postIds, version })
}

