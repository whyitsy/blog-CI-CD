import { del, get, post, put } from './http'
import type {
  ArchiveGroupDto,
  CategoryDto,
  PagedResult,
  PostDetailDto,
  PostListItemDto,
  PostPayload,
  PostQuery,
  TagDto,
} from '@/types'

export function getPosts(query: PostQuery): Promise<PagedResult<PostListItemDto>> {
  return get('/api/posts', {
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 12,
    categoryId: query.categoryId,
    tagId: query.tagId,
    keyword: query.keyword,
    includeUnpublished: query.includeUnpublished,
  })
}

export function getPostDetail(id: string): Promise<PostDetailDto> {
  return get(`/api/posts/${id}`)
}

export function searchPosts(keyword: string, page = 1, pageSize = 12): Promise<PagedResult<PostListItemDto>> {
  return get('/api/posts/search', { keyword, page, pageSize })
}

export function getArchives(): Promise<ArchiveGroupDto[]> {
  return get('/api/posts/archives')
}

// ---- 管理端 CRUD ----

/** 新建文章 */
export function createPost(payload: PostPayload): Promise<PostDetailDto> {
  return post<PostDetailDto>('/api/posts', {
    title: payload.title,
    content: payload.content,
    coverImage: payload.coverImage || '',
    categoryId: payload.categoryId,
    tagIds: payload.tagIds,
    publish: payload.publish ?? true,
  })
}

/** 更新文章（必须带回服务端下发的 version，乐观锁） */
export function updatePost(id: string, payload: PostPayload): Promise<PostDetailDto> {
  if (payload.version == null) throw new Error('更新文章必须携带版本号')
  return put<PostDetailDto>(`/api/posts/${id}`, {
    title: payload.title,
    content: payload.content,
    coverImage: payload.coverImage || '',
    categoryId: payload.categoryId,
    tagIds: payload.tagIds,
    version: payload.version,
  })
}

/** 发布 / 下架（query 参数：version, publish） */
export function publishPost(id: string, version: number, publish: boolean): Promise<PostDetailDto> {
  return post<PostDetailDto>(`/api/posts/${id}/publish?version=${version}&publish=${publish}`)
}

/** 软删除（必须携带版本号） */
export function deletePost(id: string, version: number): Promise<null> {
  return del<null>(`/api/posts/${id}?version=${version}`)
}

export type { TagDto, CategoryDto }