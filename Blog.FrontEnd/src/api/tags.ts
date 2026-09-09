import { del, get, post, put } from './http'
import type { TagDto } from '@/types'

export function getTags(): Promise<TagDto[]> {
  return get<TagDto[]>('/api/tags')
}

export function createTag(name: string): Promise<TagDto> {
  return post<TagDto>('/api/tags', { name })
}

export function updateTag(id: string, name: string, version: number): Promise<TagDto> {
  return put<TagDto>(`/api/tags/${id}`, { name, version })
}

export function deleteTag(id: string, version: number): Promise<null> {
  return del<null>(`/api/tags/${id}?version=${version}`)
}