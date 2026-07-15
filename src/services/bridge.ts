import type { BridgeHealth, DashboardSummary, GlobalSearchResult, LibrarySummary, MediaLibrary, MediaPageResult, MovieDetail, TaskItem } from '@/types/media'
import type { SettingsSnapshot } from '@/types/settings'

export const BRIDGE_ORIGIN = 'http://127.0.0.1:47831'

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${BRIDGE_ORIGIN}${path}`, init)
  if (!response.ok) {
    const message = await response.text()
    throw new Error(message || `Bridge request failed (${response.status})`)
  }
  return response.json() as Promise<T>
}

export const bridge = {
  health: () => request<BridgeHealth>('/health'),
  summary: () => request<LibrarySummary>('/api/library/summary'),
  dashboard: () => request<DashboardSummary>('/api/dashboard'),
  videos: (limit = 24, offset = 0, search = '', sort = 'newest') => {
    const query = new URLSearchParams({ limit: String(limit), offset: String(offset), search, sort })
    return request<MediaPageResult>(`/api/videos?${query}`)
  },
  settings: () => request<SettingsSnapshot>('/api/settings'),
  movie: (id: number) => request<MovieDetail>(`/api/videos/${id}`),
  search: (query: string, limit = 12) => request<GlobalSearchResult>(`/api/search?${new URLSearchParams({ q: query, limit: String(limit) })}`),
  libraries: () => request<MediaLibrary[]>('/api/libraries'),
  tasks: (limit = 100) => request<TaskItem[]>(`/api/tasks?limit=${limit}`),
  play: (dataId: number) =>
    request<{ started: boolean; path: string }>(`/api/videos/${dataId}/play`, {
      method: 'POST',
    }),
}
