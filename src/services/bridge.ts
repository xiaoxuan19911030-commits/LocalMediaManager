import type { AdvancedSearchFilters, BridgeHealth, DashboardSummary, DiagnosticsResult, EntityPageResult, GlobalSearchResult, LibrarySummary, MediaLibrary, MediaPageResult, MetadataOverview, MovieDetail, TaskItem } from '@/types/media'
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
  entities: (type: 'actors' | 'tags', search = '', sort = 'count', limit = 48, offset = 0) => request<EntityPageResult>(`/api/entities/${type}?${new URLSearchParams({ search, sort, limit: String(limit), offset: String(offset) })}`),
  entityMovies: (type: 'actors' | 'tags', id: number, limit = 48, offset = 0) => request<MediaPageResult>(`/api/entities/${type}/${id}/movies?${new URLSearchParams({ limit: String(limit), offset: String(offset) })}`),
  collection: (kind: 'favorites' | 'history', limit = 48, offset = 0) => request<MediaPageResult>(`/api/collections/${kind}?${new URLSearchParams({ limit: String(limit), offset: String(offset) })}`),
  advancedSearch: (filters: AdvancedSearchFilters) => {
    const query = new URLSearchParams({ q: filters.query, limit: String(filters.limit ?? 48), offset: String(filters.offset ?? 0), sort: filters.sort ?? 'newest', metadata: filters.metadata ?? 'all', fileStatus: filters.fileStatus ?? 'all', ratingMin: String(filters.ratingMin ?? 0) })
    if (filters.actorId) query.set('actorId', String(filters.actorId)); if (filters.tagId) query.set('tagId', String(filters.tagId)); if (filters.favorite !== undefined) query.set('favorite', String(filters.favorite)); if (filters.libraryId) query.set('libraryId', String(filters.libraryId))
    return request<MediaPageResult>(`/api/search/advanced?${query}`)
  },
  metadataOverview: () => request<MetadataOverview>('/api/metadata/overview'),
  diagnostics: () => request<DiagnosticsResult>('/api/diagnostics'),
  play: (dataId: number) =>
    request<{ started: boolean; path: string }>(`/api/videos/${dataId}/play`, {
      method: 'POST',
    }),
}
