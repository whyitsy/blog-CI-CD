import { del, get, post, put } from './http'
import type { AuthorDto, UpdateAuthorPayload } from '@/types'

/**
 * 作者（**内容层**的署名对象，不是登录账号）。
 * 与账号（`api/users.ts`）分工见 docs/tech.md §2.2。
 */

export interface CreateAuthorPayload {
  name: string
  email: string
  bio: string
  avatar: string
}

export function getAuthors(): Promise<AuthorDto[]> {
  return get<AuthorDto[]>('/api/authors')
}

export function getAuthor(id: string): Promise<AuthorDto> {
  return get<AuthorDto>(`/api/authors/${id}`)
}

/**
 * 当前登录账号的署名身份（作者工作区「个人资料」用）。
 * 账号未关联作者时后端返回 404。
 */
export function getMyAuthor(): Promise<AuthorDto> {
  return get<AuthorDto>('/api/authors/me')
}

/** 创建作者（仅管理员） */
export function createAuthor(payload: CreateAuthorPayload): Promise<AuthorDto> {
  return post<AuthorDto>('/api/authors', payload)
}

/** 更新作者资料（乐观锁；管理员可改任何人，作者只能改自己） */
export function updateAuthor(id: string, payload: UpdateAuthorPayload): Promise<AuthorDto> {
  return put<AuthorDto>(`/api/authors/${id}`, {
    name: payload.name,
    email: payload.email,
    bio: payload.bio,
    avatar: payload.avatar,
    version: payload.version,
  })
}

/** 删除作者（仅管理员，软删除；其署名文章的 AuthorId 置空） */
export function deleteAuthor(id: string, version: number): Promise<null> {
  return del<null>(`/api/authors/${id}?version=${version}`)
}
