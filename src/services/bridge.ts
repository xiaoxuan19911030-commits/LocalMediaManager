import { invoke } from '@tauri-apps/api/core'
import type { ActorDetail, ActorRepairPreview, AdvancedSearchFilters, BridgeHealth, DashboardSummary, DiagnosticsResult, DuplicateResults, EntityPageResult, GlobalSearchResult, ImageAsset, ImageCacheCleanupResult, ImageCachePreview, ImageCacheRebuildLaunchResult, ImageCenterStatus, ImageCropCommand, ImageDeletePreview, ImageMutationResult, ImageTaskLaunchResult, ImpactPreview, LibraryDeletePreview, LibraryInput, LibraryMutationResult, LibrarySummary, MaintenanceReport, MediaLibrary, MediaPageResult, MetadataOverview, MovieDeletePreview, MovieDetail, MutationResult, NeighborResult, NfoMutationResult, NfoPreview, OrganizerLaunchResult, OrganizerPreview, PlatformOpenResult, SafeDeleteLaunchResult, SafeDeletePreview, SafeDeletePreviewCommand, ScanLaunchResult, TaskCleanupResult, TaskItem, TaskLogItem, TaskMutationResult } from '@/types/media'
import type { BackupCreateCommand, BackupResult, BackupValidation, DataSafetyOverview, MetaTubeSettings, ProviderConnectionResult, RestorePlan, SettingsExport, SettingsImportPreview, SettingsSnapshot, SystemDiagnostic, UnifiedSettings, UnifiedSettingsSaveResult } from '@/types/settings'

export const BRIDGE_ORIGIN = 'http://127.0.0.1:47831'

let tokenPromise: Promise<string> | undefined
const sessionToken = (refresh = false) => {
  if (refresh) tokenPromise = undefined
  return tokenPromise ??= invoke<string>('bridge_session_token').catch(() => import.meta.env.VITE_LMM_BRIDGE_TOKEN || '')
}

function parseBridgeError(body: string, status: number) {
  try {
    const parsed = JSON.parse(body) as { code?: string; message?: string }
    return {
      code: parsed.code,
      message: parsed.message || body || `Bridge request failed (${status})`,
    }
  } catch {
    return { message: body || `Bridge request failed (${status})` }
  }
}

async function request<T>(path: string, init?: RequestInit, retrySession = true): Promise<T> {
  const headers = new Headers(init?.headers)
  const method = (init?.method ?? 'GET').toUpperCase()
  if (method !== 'GET') {
    const token = await sessionToken()
    if (token) headers.set('X-LMM-Session', token)
  }
  if (init?.body) headers.set('Content-Type', 'application/json')
  const response = await fetch(`${BRIDGE_ORIGIN}${path}`, { ...init, headers })
  if (!response.ok) {
    const body = await response.text()
    const error = parseBridgeError(body, response.status)
    if (response.status === 401 && retrySession && method !== 'GET' && error.code === 'INVALID_SESSION') {
      console.warn('[bridge] Session token was rejected; refreshing once and retrying.', { path, method, status: response.status })
      await sessionToken(true)
      return request<T>(path, init, false)
    }
    if (response.status === 401 && error.code === 'INVALID_SESSION') {
      throw new Error('本地服务会话已失效，设置没有保存。请关闭残留的 Local Media Manager 进程后重新打开。')
    }
    throw new Error(error.message)
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
  allSettings: () => request<UnifiedSettings>('/api/settings/all'),
  defaultSettings: () => request<UnifiedSettings>('/api/settings/defaults'),
  saveAllSettings: (value: UnifiedSettings, createMissingMediaStorageRoot = false) => request<UnifiedSettingsSaveResult>(`/api/settings/all?${new URLSearchParams({ createMissingMediaStorageRoot: String(createMissingMediaStorageRoot) })}`, { method: 'PUT', body: JSON.stringify(value) }),
  dataSafetyOverview: () => request<DataSafetyOverview>('/api/settings/data-safety/overview'),
  createBackup: (value: BackupCreateCommand) => request<BackupResult>('/api/settings/data-safety/backup', { method: 'POST', body: JSON.stringify(value) }),
  validateBackup: (path: string) => request<BackupValidation>(`/api/settings/data-safety/backup/validate?${new URLSearchParams({ path })}`),
  createRestorePlan: (backupPath: string, mode: string) => request<RestorePlan>('/api/settings/data-safety/restore-plan', { method: 'POST', body: JSON.stringify({ backupPath, mode }) }),
  exportSettings: () => request<SettingsExport>('/api/settings/export'),
  previewSettingsImport: (value: unknown) => request<SettingsImportPreview>('/api/settings/import-preview', { method: 'POST', body: JSON.stringify(value) }),
  settingsDiagnostics: () => request<SystemDiagnostic>('/api/settings/diagnostics'),
  testMetaTube: (value: MetaTubeSettings) => request<ProviderConnectionResult>('/api/settings/providers/metatube/test', { method: 'POST', body: JSON.stringify(value) }),
  movie: (id: number) => request<MovieDetail>(`/api/videos/${id}`),
  movieImages: (id: number) => request<ImageAsset[]>(`/api/videos/${id}/images`),
  movieImageStatus: (id: number) => request<ImageCenterStatus>(`/api/videos/${id}/images/status`),
  setImageLock: (imageId: number, locked: boolean) => request<ImageMutationResult>(`/api/image-assets/${imageId}/lock`, { method: 'PUT', body: JSON.stringify({ locked }) }),
  replaceMovieImage: (movieId: number, type: string, path: string) => request<ImageMutationResult>(`/api/videos/${movieId}/images/${encodeURIComponent(type)}/replace`, { method: 'POST', body: JSON.stringify({ path }) }),
  cropMovieCard: (movieId: number, command: ImageCropCommand) => request<ImageMutationResult>(`/api/videos/${movieId}/images/crop-card`, { method: 'POST', body: JSON.stringify(command) }),
  generateMovieImage: (movieId: number, type: string) => request<ImageTaskLaunchResult>(`/api/videos/${movieId}/images/${encodeURIComponent(type)}/generate`, { method: 'POST' }),
  previewDeleteImage: (imageId: number) => request<ImageDeletePreview>(`/api/image-assets/${imageId}/delete-preview`),
  deleteImage: (imageId: number, confirmationToken: string) => request<ImageMutationResult>(`/api/image-assets/${imageId}/delete`, { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  revealImage: (imageId: number) => request<PlatformOpenResult>(`/api/image-assets/${imageId}/reveal`, { method: 'POST' }),
  openImageDirectory: (imageId: number) => request<PlatformOpenResult>(`/api/image-assets/${imageId}/open-directory`, { method: 'POST' }),
  imageCachePreview: () => request<ImageCachePreview>('/api/images/cache/cleanup-preview'),
  cleanupImageCache: (confirmationToken: string) => request<ImageCacheCleanupResult>('/api/images/cache/cleanup', { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  rebuildImageCache: () => request<ImageCacheRebuildLaunchResult>('/api/images/cache/rebuild', { method: 'POST' }),
  previewNfoExport: (movieId: number) => request<NfoPreview>(`/api/videos/${movieId}/nfo/export-preview`),
  exportNfo: (movieId: number, confirmationToken: string, separateWhenLocked = false) => request<NfoMutationResult>(`/api/videos/${movieId}/nfo/export?separateWhenLocked=${separateWhenLocked}`, { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  previewNfoImport: (movieId: number) => request<NfoPreview>(`/api/videos/${movieId}/nfo/import-preview`),
  importNfo: (movieId: number, confirmationToken: string) => request<NfoMutationResult>(`/api/videos/${movieId}/nfo/import`, { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
  organizerDryRun: (movieIds: number[], fileNameTemplate: string, destinationDirectory?: string) => request<OrganizerPreview>('/api/organizer/dry-run', { method: 'POST', body: JSON.stringify({ movieIds, fileNameTemplate, destinationDirectory: destinationDirectory || null }) }),
  organizerPreview: (taskId: number) => request<OrganizerPreview>(`/api/organizer/${taskId}/preview`),
  executeOrganizer: (taskId: number, confirmationToken: string) => request<OrganizerLaunchResult>(`/api/organizer/${taskId}/execute`, { method: 'POST', body: JSON.stringify({ confirmationToken }) }),
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
  cancelSyncTasks: (taskIds: number[]) => request<{ count: number; message: string }>('/api/tasks/batch/cancel-sync', { method: 'POST', body: JSON.stringify(taskIds) }),
  retryTask: (taskId: number) => request<ScanLaunchResult>(`/api/tasks/${taskId}/retry`, { method: 'POST' }),
  deleteTask: (taskId: number) => request<TaskCleanupResult>(`/api/tasks/${taskId}`, { method: 'DELETE' }),
  cleanupTasks: (status: 'completed' | 'failed' | 'cancelled' | 'terminal') => request<TaskCleanupResult>('/api/tasks/cleanup', { method: 'POST', body: JSON.stringify({ status }) }),
  openDirectory: (path: string) => request<PlatformOpenResult>('/api/platform/open-directory', { method: 'POST', body: JSON.stringify({ path }) }),
  revealFile: (path: string) => request<PlatformOpenResult>('/api/platform/reveal-file', { method: 'POST', body: JSON.stringify({ path }) }),
  entities: (type: 'actors' | 'directors' | 'series' | 'tags' | 'custom-tags' | 'movie-tags', search = '', sort = 'count', limit = 48, offset = 0) => request<EntityPageResult>(`/api/entities/${type}?${new URLSearchParams({ search, sort, limit: String(limit), offset: String(offset) })}`),
  actor: (id: number) => request<ActorDetail>(`/api/actors/${id}`),
  entityMovies: (type: 'actors' | 'directors' | 'series' | 'tags' | 'custom-tags' | 'movie-tags', id: number, limit = 48, offset = 0) => request<MediaPageResult>(`/api/entities/${type}/${id}/movies?${new URLSearchParams({ limit: String(limit), offset: String(offset) })}`),
  collection: (kind: 'favorites' | 'history', limit = 48, offset = 0) => request<MediaPageResult>(`/api/collections/${kind}?${new URLSearchParams({ limit: String(limit), offset: String(offset) })}`),
  advancedSearch: (filters: AdvancedSearchFilters) => {
    const query = new URLSearchParams({ q: filters.query, limit: String(filters.limit ?? 48), offset: String(filters.offset ?? 0), sort: filters.sort ?? 'newest', metadata: filters.metadata ?? 'all', fileStatus: filters.fileStatus ?? 'all', metadataStatus: filters.metadataStatus ?? 'all', ratingMin: String(filters.ratingMin ?? 0), ratingFilter: filters.ratingFilter ?? 'all' })
    if (filters.actorId) query.set('actorId', String(filters.actorId)); if (filters.tagId) query.set('tagId', String(filters.tagId)); if (filters.directorId) query.set('directorId', String(filters.directorId)); if (filters.movieTagId) query.set('movieTagId', String(filters.movieTagId)); if (filters.customTagId) query.set('customTagId', String(filters.customTagId)); if (filters.seriesId) query.set('seriesId', String(filters.seriesId)); if (filters.favorite !== undefined) query.set('favorite', String(filters.favorite)); if (filters.watched !== undefined) query.set('watched', String(filters.watched)); if (filters.libraryId) query.set('libraryId', String(filters.libraryId))
    return request<MediaPageResult>(`/api/search/advanced?${query}`)
  },
  metadataOverview: () => request<MetadataOverview>('/api/metadata/overview'),
  diagnostics: () => request<DiagnosticsResult>('/api/diagnostics'),
  duplicates: (rule: 'all' | 'code' | 'path' | 'hash' = 'all', limit = 100) => request<DuplicateResults>(`/api/duplicates?${new URLSearchParams({ rule, limit: String(limit) })}`),
  maintenanceReport: (limit = 50, offset = 0) => request<MaintenanceReport>(`/api/maintenance/report?${new URLSearchParams({ limit: String(limit), offset: String(offset) })}`),
  play: (dataId: number) =>
    request<{ started: boolean; path: string }>(`/api/videos/${dataId}/play`, {
      method: 'POST',
    }),
  setUserState: (movieId: number, value: { favorite?: boolean; rating?: number; clearRating?: boolean }) =>
    request<MutationResult>(`/api/videos/${movieId}/state`, { method: 'PATCH', body: JSON.stringify(value) }),
  setBatchFavorite: (movieIds: number[], favorite: boolean) => request<MutationResult>('/api/videos/batch/favorite', { method: 'POST', body: JSON.stringify({ movieIds, favorite }) }),
  setBatchRating: (movieIds: number[], rating?: number, clearRating = false) => request<MutationResult>('/api/videos/batch/rating', { method: 'POST', body: JSON.stringify({ movieIds, rating: rating ?? null, clearRating }) }),
  createBatchSync: (movieIds: number[]) => request<{ count: number; message: string }>('/api/videos/batch/sync', { method: 'POST', body: JSON.stringify(movieIds) }),
  previewSafeDelete: (value: SafeDeletePreviewCommand) => request<SafeDeletePreview>('/api/delete/preview', { method: 'POST', body: JSON.stringify(value) }),
  executeSafeDelete: (preview: SafeDeletePreviewCommand, confirmationToken: string, confirmOriginalMedia = false, confirmCount?: number) =>
    request<SafeDeleteLaunchResult>('/api/delete/execute', { method: 'POST', body: JSON.stringify({ ...preview, confirmationToken, confirmOriginalMedia, confirmCount }) }),
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
