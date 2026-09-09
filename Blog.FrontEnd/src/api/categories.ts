import { del, get, post, put } from './http'
import type { CategoryDto } from '@/types'

export function getCategories(): Promise<CategoryDto[]> {
  return get<CategoryDto[]>('/api/categories')
}

export function createCategory(name: string): Promise<CategoryDto> {
  return post<CategoryDto>('/api/categories', { name })
}

export function updateCategory(id: string, name: string, version: number): Promise<CategoryDto> {
  return put<CategoryDto>(`/api/categories/${id}`, { name, version })
}

export function deleteCategory(id: string, version: number): Promise<null> {
  return del<null>(`/api/categories/${id}?version=${version}`)
}