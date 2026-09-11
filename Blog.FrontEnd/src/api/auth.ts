import { get, post } from './http'
import type { AuthUser, LoginRequest, LoginResponse } from '@/types'

/**
 * 认证接口。
 *
 * 按 T1 决策**没有注册端点**：作者账号由管理员在「账号管理」中创建，
 * 因此这里只有登录、注销与获取当前用户。
 */

/**
 * 登录。管理员与作者共用同一个端点，响应中的 role 决定登录后去向。
 *
 * 注意：「作者」在这里指 role=Author 的**账号**（User），
 * 与署名实体 Author 是两回事 —— Author 没有密码、不能登录。
 */
export function login(payload: LoginRequest): Promise<LoginResponse> {
  return post<LoginResponse>('/api/auth/login', payload)
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
