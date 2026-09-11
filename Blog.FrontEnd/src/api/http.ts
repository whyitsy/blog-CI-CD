import type { ApiResponse } from '@/types'

/** 业务异常：code !== 0 时抛出，携带后端业务码与消息 */
export class ApiError extends Error {
  code: number

  constructor(code: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.code = code
  }
}

/** 后端统一业务码（与 Blog.Application/Common/ErrorCodes.cs 对齐） */
export const ErrorCode = {
  Ok: 0,
  InvalidArgument: 4001,
  BusinessRule: 4002,
  DuplicateResource: 4003,
  /** 未认证：token 缺失/无效/过期，或账号被停用 */
  Unauthorized: 4010,
  /** 已认证但无权限（角色不足或越权访问他人资源） */
  Forbidden: 4030,
  NotFound: 4040,
  ConcurrencyConflict: 4090,
  RateLimited: 4091,
  PayloadTooLarge: 4130,
  InternalError: 5000,
} as const

/**
 * 判断错误是否为「资源不存在」。
 * 后端统一业务码为 4040；反向代理等返回的裸 404 没有响应体，只能靠 HTTP 状态码兜底。
 */
export function isNotFoundError(e: unknown): boolean {
  return e instanceof ApiError && (e.code === ErrorCode.NotFound || e.code === 404)
}

const BASE = import.meta.env.VITE_API_BASE ?? ''

/**
 * token 的读取方式由 main.ts 注入，避免 http 层直接依赖 store/router 造成循环引用。
 */
let tokenProvider: () => string = () => ''
/** 收到 401 时的回调（由 main.ts 注入：清理登录态并跳转登录页） */
let unauthorizedHandler: (() => void) | null = null

export function configureHttp(options: {
  getToken: () => string
  onUnauthorized: () => void
}) {
  tokenProvider = options.getToken
  unauthorizedHandler = options.onUnauthorized
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response

  const token = tokenProvider()
  const authHeader: Record<string, string> = token ? { Authorization: `Bearer ${token}` } : {}

  try {
    // multipart 上传必须让浏览器自行生成 boundary，因此不能预设 Content-Type
    const headers =
      init?.body instanceof FormData
        ? { ...authHeader, ...init?.headers }
        : { 'Content-Type': 'application/json', ...authHeader, ...init?.headers }

    res = await fetch(`${BASE}${path}`, { ...init, headers })
  } catch {
    throw new ApiError(-1, '网络异常，请检查后端服务是否启动')
  }

  // 429 限流 / 其他非 JSON 响应兜底
  const body = (await res.json().catch(() => null)) as ApiResponse<T> | null

  if (!body) {
    if (res.status === 429) {
      throw new ApiError(ErrorCode.RateLimited, '请求过于频繁，请稍后再试')
    }
    if (res.status === 401) {
      // 后端正常情况下会返回统一响应体；这里兜底处理反向代理等返回的裸 401
      unauthorizedHandler?.()
      throw new ApiError(ErrorCode.Unauthorized, '未认证或登录已过期，请重新登录')
    }
    // 非统一响应体（反向代理 502/503、网关超时、路由未匹配等）：
    // 文案一律中性化，不把 HTTP 状态码等基础设施细节暴露给用户；
    // 状态码仍保留在 ApiError.code 里，供调用方分支判断与排查。
    if (res.status === 404) {
      throw new ApiError(ErrorCode.NotFound, '请求的资源不存在')
    }
    throw new ApiError(res.status, '服务暂时不可用，请稍后重试')
  }

  if (body.code !== ErrorCode.Ok) {
    // 401 统一处理：清理登录态并跳登录页。
    // 登录接口本身返回 401（凭据错误）时不应跳转，由调用方的 onUnauthorized 自行判断。
    if (body.code === ErrorCode.Unauthorized) {
      unauthorizedHandler?.()
    }
    throw new ApiError(body.code, body.message || '请求失败')
  }

  return body.data
}

export function get<T>(path: string, params?: Record<string, string | number | boolean | undefined>): Promise<T> {
  const qs = params
    ? '?' +
      Object.entries(params)
        .filter(([, v]) => v !== undefined && v !== '')
        .map(([k, v]) => `${encodeURIComponent(k)}=${encodeURIComponent(String(v))}`)
        .join('&')
    : ''
  return request<T>(`${path}${qs}`)
}

export function post<T>(path: string, body?: unknown): Promise<T> {
  return request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
}

export function put<T>(path: string, body?: unknown): Promise<T> {
  return request<T>(path, { method: 'PUT', body: JSON.stringify(body) })
}

export function del<T>(path: string): Promise<T> {
  return request<T>(path, { method: 'DELETE' })
}

/**
 * 上传文件（multipart/form-data）。
 * 注意：不能带 Content-Type: application/json，浏览器需自行生成 multipart boundary。
 */
export function upload<T = { url: string }>(path: string, file: File): Promise<T> {
  const form = new FormData()
  form.append('file', file)
  return request<T>(path, { method: 'POST', body: form })
}
