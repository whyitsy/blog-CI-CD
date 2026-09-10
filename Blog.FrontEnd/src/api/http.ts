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

const BASE = import.meta.env.VITE_API_BASE ?? ''

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let res: Response
  try {
    // multipart 上传必须让浏览器自行生成 boundary，因此不能预设 Content-Type
    const headers = init?.body instanceof FormData
      ? { ...init?.headers }
      : { 'Content-Type': 'application/json', ...init?.headers }

    res = await fetch(`${BASE}${path}`, { ...init, headers })
  } catch {
    throw new ApiError(-1, '网络异常，请检查后端服务是否启动')
  }

  // 429 限流 / 其他非 JSON 响应兜底
  const body = (await res.json().catch(() => null)) as ApiResponse<T> | null
  if (!body) {
    throw new ApiError(res.status, res.status === 429 ? '请求过于频繁，请稍后再试' : `请求失败（HTTP ${res.status}）`)
  }
  if (body.code !== 0) {
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
