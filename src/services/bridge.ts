import { invoke } from '@tauri-apps/api/core'
import type { ActorDetail, ActorRepairPreview, AdvancedSearchFilters, BridgeHealth, DashboardSummary, DiagnosticsResult, EntityPageResult, GlobalSearchResult, ImageAsset, ImageCacheCleanupResult, ImageCachePreview, ImageCacheRebuildLaunchResult, ImageMutationResult, ImpactPreview, LibraryDeletePreview, LibraryInput, LibraryMutationResult, LibrarySummary, MediaLibrary, MediaPageResult, MetadataOverview, MovieDeletePreview, MovieDetail, MutationResult, NeighborResult, NfoMutationResult, NfoPreview, ScanLaunchResult, TaskItem, TaskLogItem, TaskMutationResult } from '@/types/media'
import type { MetaTubeSettings, NfoSettings, ProviderConnectionResult, SettingsSnapshot } from '@/types/settings'

export const BRIDGE_ORIGIN = 'http://127.0.0.1:47831'

let tokenPromise: Promise<string> | undefined
const sessionToken = () => tokenPromise ??= invoke<string>('bridge_session_token').catch(() => import.meta.env.VITE_LMM_BRIDGE_TOKEN || '')

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  if (init?.method && init.method !== 'GET') {
    const token = await sessionToken()
    if (token) headers.set('X-LMM-Session', token)
  }
  if (init?.body) headers.set('Content-Type', 'application/json')
  const response = await fetch(`${BRIDGE_ORIGIN}${path}`, { ...init, headers })
  if (!response.ok) {
    const body = await response.text()
    try { throw new Error((JSON.parse(body) as { message?: string }).message || body) }
    catch (error) { if (error instanceof SyntaxError) throw new Error(body || `Bridge request failed (${response.status})`); throw error }
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
  saveMetaTubeSettings: (value: MetaTubeSettings) => request<MetaTubeSettings>('/api/settings/providers/metatube', { method: 'PUT', body: JSON.stringify(value) }),
  testMetaTube: (value: MetaTubeSettings) => request<ProviderConnectionResult>('/api/settings/providers/metatube/test', { method: 'POST', body: JSON.stringify(value) }),
  nfoSettings: () => request<NfoSettings>('/api/settings/nfo'),
  saveNfoSettings: (value: NfoSettings) => request<NfoSettings>('/api/settings/nfo', { method: 'PUT', body: JSON.stringify(value) }),
  movie: (id: number) => request<MovieDetail>(`/api/videos/${id}`),
  movieImages: (id: number) => request<ImageAsset[]>(`/api/videos/${id}/images`),
  setImageLock: (imageId: number, locked: boolean) => request<ImageMutationResult>(`/api/image-assets/${imageId}/lock`, { method: 'PUT', body: JSON.stringify({ locked }) }),
  imageCachePreview: () => request<ImageCachePreview>('/api/images/cache/cleanup-preview'),
  cleanupImageCache: (confirmationToken: string) => request<ImageCacheCleanupResult>('/api/images/cache/cleanup', { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  rebuildImageCache: () => request<ImageCacheRebuildLaunchResult>('/api/images/cache/rebuild', { method: 'POST' }),
  previewNfoExport: (movieId: number) => request<NfoPreview>(`/api/videos/${movieId}/nfo/export-preview`),
  exportNfo: (movieId: number, confirmationToken: string, separateWhenLocked = false) => request<NfoMutationResult>(`/api/videos/${movieId}/nfo/export?separateWhenLocked=${separateWhenLocked}`, { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  previewNfoImport: (movieId: number) => request<NfoPreview>(`/api/videos/${movieId}/nfo/import-preview`),
  importNfo: (movieId: number, confirmationToken: string) => request<NfoMutationResult>(`/api/videos/${movieId}/nfo/import`, { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  syncMovie: (id: number) => request<ScanLaunchResult>(`/api/videos/${id}/sync`, { method: 'POST' }),
  neighbors: (id: number, search = '', sort = 'newest') => request<NeighborResult>(`/api/videos/${id}/neighbors?${new URLSearchParams({ search, sort })}`),
  search: (query: string, limit = 12) => request<GlobalSearchResult>(`/api/search?${new URLSearchParams({ q: query, limit: String(limit) })}`),
  libraries: () => request<MediaLibrary[]>('/api/libraries'),
  createLibrary: (value: LibraryInput) => request<LibraryMutationResult>('/api/libraries', { method: 'POST', body: JSON.stringify(value) }),
  updateLibrary: (libraryId: number, value: LibraryInput) => request<LibraryMutationResult>(`/api/libraries/${libraryId}`, { method: 'PUT', body: JSON.stringify(value) }),
  previewDeleteLibrary: (libraryId: number) => request<LibraryDeletePreview>(`/api/libraries/${libraryId}/delete-preview`),
  deleteLibrary: (libraryId: number, confirmationToken: string) => request<LibraryMutationResult>(`/api/libraries/${libraryId}/delete`, { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  scanLibrary: (libraryId: number, fullScan = false, autoSync = true) => request<ScanLaunchResult>(`/api/libraries/${libraryId}/scan`, { method: 'POST', body: JSON.stringify({ fullScan, autoSync }) }),
  tasks: (limit = 100) => request<TaskItem[]>(`/api/tasks?limit=${limit}`),
  taskLogs: (taskId: number, limit = 200) => request<TaskLogItem[]>(`/api/tasks/${taskId}/logs?limit=${limit}`),
  pauseTask: (taskId: number) => request<TaskMutationResult>(`/api/tasks/${taskId}/pause`, { method: 'POST' }),
  resumeTask: (taskId: number) => request<TaskMutationResult>(`/api/tasks/${taskId}/resume`, { method: 'POST' }),
  cancelTask: (taskId: number) => request<TaskMutationResult>(`/api/tasks/${taskId}/cancel`, { method: 'POST' }),
  retryTask: (taskId: number) => request<ScanLaunchResult>(`/api/tasks/${taskId}/retry`, { method: 'POST' }),
  entities: (type: 'actors' | 'tags', search = '', sort = 'count', limit = 48, offset = 0) => request<EntityPageResult>(`/api/entities/${type}?${new URLSearchParams({ search, sort, limit: String(limit), offset: String(offset) })}`),
  actor: (id: number) => request<ActorDetail>(`/api/actors/${id}`),
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
  setUserState: (movieId: number, value: { favorite?: boolean; rating?: number; clearRating?: boolean }) =>
    request<MutationResult>(`/api/videos/${movieId}/state`, { method: 'PATCH', body: JSON.stringify(value) }),
  setBatchFavorite: (movieIds: number[], favorite: boolean) => request<MutationResult>('/api/videos/batch/favorite', { method: 'POST', body: JSON.stringify({ movieIds, favorite }) }),
  createTag: (value: { name: string; description?: string; color?: string }) => request<MutationResult & { id: number }>('/api/tags', { method: 'POST', body: JSON.stringify(value) }),
  updateTag: (tagId: number, value: { name: string; description?: string; color?: string }) => request<MutationResult>(`/api/tags/${tagId}`, { method: 'PUT', body: JSON.stringify(value) }),
  previewDeleteTag: (tagId: number) => request<ImpactPreview>(`/api/tags/${tagId}/delete-preview`),
  deleteTag: (tagId: number, confirmationToken: string) => request<MutationResult>(`/api/tags/${tagId}/delete`, { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  updateMovieTags: (movieId: number, addTagIds: number[], removeTagIds: number[]) => request<MutationResult>(`/api/videos/${movieId}/tags`, { method: 'PATCH', body: JSON.stringify({ addTagIds, removeTagIds }) }),
  updateBatchTags: (movieIds: number[], addTagIds: number[], removeTagIds: number[]) => request<MutationResult>('/api/videos/batch/tags', { method: 'POST', body: JSON.stringify({ movieIds, addTagIds, removeTagIds }) }),
  updateActor: (actorId: number, value: { name: string; alias?: string; gender?: number; birthDate?: string; description?: string }) => request<MutationResult>(`/api/actors/${actorId}`, { method: 'PUT', body: JSON.stringify(value) }),
  setMovieActors: (movieId: number, actorIds: number[]) => request<MutationResult>(`/api/videos/${movieId}/actors`, { method: 'PUT', body: JSON.stringify({ actorIds }) }),
  previewActorRepair: () => request<ActorRepairPreview>('/api/actors/repair-preview'),
  applyActorRepair: (confirmationToken: string) => request<MutationResult>('/api/actors/repair', { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  rollbackOperation: (auditId: number) => request<MutationResult>(`/api/operations/${auditId}/rollback`, { method: 'POST' }),
  previewDeleteMovie: (movieId: number) => request<MovieDeletePreview>(`/api/videos/${movieId}/delete-preview`),
  deleteMovie: (movieId: number, confirmationToken: string) => request<MutationResult>(`/api/videos/${movieId}/delete`, { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
}
