import { get, post, put } from './http'
import type { AuthUser, UserRole } from '@/types'

/**
 * 账号管理接口。**整个模块仅 Admin 可用**（后端 `[Authorize(Policy = "AdminOnly")]`）。
 *
 * 这是作者账号的唯一创建入口（T1：作者不开放自助注册）。
 * 所有 DTO 都不含凭据字段。
 */

/** 账号列表项（与后端 UserDto 对齐，比 AuthUser 多 version/createdAt） */
export interface UserListItem extends AuthUser {
  createdAt: string
  version: number
}

export interface CreateUserPayload {
  email: string
  password: string
  role: UserRole
  /** role=Author 时建议指定：即「给哪位作者开通登录」 */
  authorId: string | null
}

export interface UpdateUserPayload {
  role: UserRole
  authorId: string | null
  isActive: boolean
  version: number
}

export function getUsers(): Promise<UserListItem[]> {
  return get<UserListItem[]>('/api/users')
}

export function createUser(payload: CreateUserPayload): Promise<UserListItem> {
  return post<UserListItem>('/api/users', payload)
}

export function updateUser(id: string, payload: UpdateUserPayload): Promise<UserListItem> {
  return put<UserListItem>(`/api/users/${id}`, payload)
}

/** 重置密码：后端会提升 TokenVersion，使该账号旧 token 立即失效 */
export function resetUserPassword(id: string, newPassword: string, version: number): Promise<UserListItem> {
  return post<UserListItem>(`/api/users/${id}/reset-password`, { newPassword, version })
}

/** 停用账号（不物理删除，保留审计线索） */
export function disableUser(id: string, version: number): Promise<null> {
  return post<null>(`/api/users/${id}/disable?version=${version}`)
}
