import { upload } from './http'

/** 上传文件（图片等），返回后端可访问的相对 URL */
export function uploadFile(file: File): Promise<string> {
  return upload<{ url: string }>('/api/files/upload', file).then((res) => res.url)
}
