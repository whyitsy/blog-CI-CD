import { get, put } from './http'
import type { AuthorDto, UpdateAuthorPayload } from '@/types'

/** 作者列表（当前无认证，单人博客取首条作为博主） */
export function getAuthors(): Promise<AuthorDto[]> {
  return get<AuthorDto[]>('/api/authors')
}

export function getAuthor(id: string): Promise<AuthorDto> {
  return get<AuthorDto>(`/api/authors/${id}`)
}

/** 更新博主资料（乐观锁：必须携带 version） */
export function updateAuthor(id: string, payload: UpdateAuthorPayload): Promise<AuthorDto> {
  return put<AuthorDto>(`/api/authors/${id}`, {
    name: payload.name,
    email: payload.email,
    bio: payload.bio,
    avatar: payload.avatar,
    version: payload.version,
  })
}
