import { get } from './http'
import type { ArchiveGroupDto, PagedResult, PostDetailDto, PostListItemDto, PostQuery } from '@/types'

export function getPosts(query: PostQuery): Promise<PagedResult<PostListItemDto>> {
  return get('/api/posts', {
    page: query.page ?? 1,
    pageSize: query.pageSize ?? 12,
    categoryId: query.categoryId,
    tagId: query.tagId,
    keyword: query.keyword,
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
