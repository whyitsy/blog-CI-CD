import { get, post } from './http'
import type { AuthUser, LoginRequest, LoginResponse } from '@/types'

/**
 * 认证接口。
 *
 * 按 T1 决策**没有注册端点**：作者账号由管理员在「账号管理」中创建，
 * 因此这里只有登录、注销与获取当前用户。
 */

/** 作者登录（仅 Author 角色可通过） */
export function authorLogin(payload: LoginRequest): Promise<LoginResponse> {
  return post<LoginResponse>('/api/auth/author/login', payload)
}

/** 管理员登录（仅 Admin 角色可通过） */
export function adminLogin(payload: LoginRequest): Promise<LoginResponse> {
  return post<LoginResponse>('/api/auth/admin/login', payload)
}

/** 当前登录用户 */
export function getMe(): Promise<AuthUser> {
  return get<AuthUser>('/api/auth/me')
}

/**
 * 注销。后端会提升该账号的 TokenVersion，
 * 使该账号**所有**已签发的 token 立即失效（JWT 本身无法主动失效）。
 */
export function logout(): Promise<null> {
  return post<null>('/api/auth/logout')
}
