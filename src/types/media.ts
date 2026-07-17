export interface LibrarySummary {
  videoCount: number
  databasePath: string
  readOnly: boolean
}

export interface MetadataCheck { key: string; label: string; complete: boolean }
export interface MetadataStatus {
  state: 'complete' | 'partial' | 'unscraped'
  icon: string
  label: string
  missingItems: string[]
  checks: MetadataCheck[]
}

export interface MediaItem {
  dataId: number
  code: string
  title: string
  path: string
  grade: number
  favorite: boolean
  releaseDate: string
  importedAt: string
  coverUrl?: string
  metadataStatus: MetadataStatus
}

export interface MediaPageResult {
  items: MediaItem[]
  total: number
  limit: number
  offset: number
}

export interface BridgeHealth {
  status: string
  databaseAvailable: boolean
  databasePath: string
  readOnly: boolean
  writeEnabled: boolean
}

export interface DashboardSummary {
  movieCount: number
  favoriteCount: number
  playedCount: number
  missingFileCount: number
  libraryCount: number
  activeTaskCount: number
  completeMetadataCount: number
  pendingMetadataCount: number
  unscrapedCount: number
  recentImports: MediaItem[]
  recentPlays: MediaItem[]
}

export interface SearchEntity {
  id: number
  name: string
  movieCount: number
}

export interface GlobalSearchResult {
  query: string
  movies: MediaItem[]
  actors: SearchEntity[]
  tags: SearchEntity[]
}

export interface LibraryFolder {
  id: number
  path: string
  enabled: boolean
  includeSubfolders: boolean
  scanMode: string
  lastScannedAt?: string
  excludePatterns: string[]
}

export interface MediaLibrary {
  id: number
  name: string
  description?: string
  enabled: boolean
  movieCount: number
  missingCount: number
  folders: LibraryFolder[]
}

export interface TaskItem {
  id: number
  type: string
  status: string
  name: string
  progress: number
  totalItems: number
  completedItems: number
  errorMessage?: string
  createdAt: string
  startedAt?: string
  completedAt?: string
  stage?: string
  provider?: string
  retryCount: number
  currentMovieId?: number
  resultSummary?: string
}
export interface TaskLogItem { id: number; level: string; message: string; createdAt: string }
export interface TaskMutationResult { taskId: number; status: string; message: string }

export interface LibraryFolderInput {
  path: string
  includeSubfolders: boolean
  enabled: boolean
  scanMode: 'normal' | 'watch' | 'manual'
  excludePatterns: string[]
}

export interface LibraryInput {
  name: string
  description?: string
  enabled: boolean
  folders: LibraryFolderInput[]
}

export interface LibraryMutationResult extends MutationResult { id: number }
export interface LibraryDeletePreview {
  libraryId: number
  name: string
  folderCount: number
  linkedFiles: number
  confirmationToken: string
  warnings: string[]
}
export interface ScanLaunchResult { taskId: number; status: string; message: string }

export interface NamedItem { id: number; name: string }
export interface MediaFileItem {
  id: number
  path: string
  fileName: string
  extension?: string
  fileSize: number
  sourceType: string
  existsState: string
  primary: boolean
}

export interface MovieDetail {
  id: number
  code?: string
  title?: string
  originalTitle?: string
  releaseDate?: string
  durationSeconds: number
  description?: string
  providerRating: number
  scraped: boolean
  scrapeStatus: string
  nfoPath?: string
  importedAt?: string
  updatedAt: string
  favorite: boolean
  userRating: number
  userRatingSet: boolean
  playCount: number
  lastPlayedAt?: string
  lastPositionSeconds: number
  notes?: string
  coverUrl?: string
  metadataStatus: MetadataStatus
  mediaFiles: MediaFileItem[]
  actors: NamedItem[]
  directors: NamedItem[]
  tags: NamedItem[]
  genres: NamedItem[]
  studios: NamedItem[]
  series: NamedItem[]
}

export interface EntityCard { id: number; name: string; movieCount: number; imageUrl?: string }
export interface ActorDetail { id: number; name: string; alias?: string; gender?: number; birthDate?: string; description?: string }
export interface EntityPageResult { items: EntityCard[]; total: number; limit: number; offset: number }
export interface AdvancedSearchFilters {
  query: string; actorId?: number; tagId?: number; favorite?: boolean; watched?: boolean; ratingMin?: number
  metadata?: string; fileStatus?: string; metadataStatus?: string; libraryId?: number; sort?: string; limit?: number; offset?: number
}
export interface MetadataOverview {
  totalMovies: number; scrapedMovies: number; completeMovies: number; pendingMovies: number; unscrapedMovies: number
  missingTitle: number; missingCover: number; missingFanart: number; missingPreview: number
  missingActors: number; missingTags: number; missingDescription: number; missingNfo: number; missingFiles: number
}
export interface DiagnosticItem { severity: 'error' | 'warning' | 'info'; code: string; title: string; detail: string; count: number }
export interface DiagnosticsResult { integrity: string; foreignKeyErrors: number; items: DiagnosticItem[] }
export interface DuplicateMovie {
  movieId: number
  code: string
  title: string
  filePath: string
  fileHash?: string
  importedAt: string
  recommendation: string
}
export interface DuplicateGroup {
  rule: 'code' | 'path' | 'hash'
  key: string
  count: number
  items: DuplicateMovie[]
}
export interface DuplicateResults {
  totalGroups: number
  totalMovies: number
  codeGroups: number
  pathGroups: number
  hashGroups: number
  groups: DuplicateGroup[]
}
export interface MaintenanceStats {
  totalMovies: number
  healthyMovies: number
  problemMovies: number
  duplicateMovies: number
  missingImages: number
  missingNfo: number
  missingMetadata: number
  orphanFiles: number
  emptyDirectories: number
  cacheProblems: number
}
export interface MaintenanceIssue { category: string; severity: string; title: string; detail: string; movieId?: number; path?: string }
export interface MaintenancePath { kind: string; path: string; reason: string }
export interface MaintenanceReport {
  stats: MaintenanceStats
  issues: MaintenanceIssue[]
  orphanFiles: MaintenancePath[]
  directories: MaintenancePath[]
  duplicates: DuplicateResults
  limit: number
  offset: number
}
export interface MutationResult { changed: boolean; auditId: number; message: string }
export interface ImpactPreview { operation: string; entityId: number; name: string; affectedMovies: number; confirmationToken: string; warnings: string[] }
export interface ActorRepairPreview { candidateActors: number; affectedRelations: number; confirmationToken: string; warnings: string[] }
export interface NeighborResult { previousId?: number; nextId?: number }
export interface MovieDeletePreview { movieId: number; code: string; fileName: string; ratingWillBeRemembered: boolean; confirmationToken: string; warnings: string[] }

export interface ImageAsset {
  id: number; type: string; url?: string; ownership: string; locked: boolean; derived: boolean
  primary: boolean; validationStatus: string; width: number; height: number; fileSize: number
  provider?: string; downloadedAt?: string; directory?: string
}
export interface ImageAssetStatus { id: number; type: string; status: 'Normal' | 'Missing' | 'Failed'; cacheStatus: 'Valid' | 'Invalid' | 'Unknown' | 'NotCached'; url?: string; message?: string }
export interface ImageCenterStatus {
  movieId: number; totalImages: number; normalImages: number; missingImages: number; invalidCacheEntries: number; failedImages: number
  assets: ImageAssetStatus[]
}
export interface ImageMutationResult { changed: boolean; message: string }
export interface ImageCachePreview {
  entries: number; existingEntries: number; missingEntries: number; bytes: number
  confirmationToken: string; warnings: string[]
}
export interface ImageCacheCleanupResult { deletedEntries: number; deletedBytes: number; failedEntries: number; message: string }
export interface ImageCacheRebuildLaunchResult { taskId: number; status: string; totalItems: number; message: string }
export interface NfoData {
  code: string; title?: string; originalTitle?: string; plot?: string; rating?: number; releaseDate?: string
  runtimeMinutes?: number; director?: string; studio?: string; publisher?: string; country?: string
  actors: string[]; tags: string[]; genres: string[]; series: string[]; imageReferences: string[]; source?: string; sourceId?: string
}
export interface NfoPreview {
  movieId: number; path: string; ownership: string; locked: boolean; exists: boolean; canApply: boolean
  confirmationToken: string; changes: string[]; conflicts: string[]; warnings: string[]; data: NfoData
}
export interface NfoMutationResult { changed: boolean; path: string; ownership: string; locked: boolean; message: string }
export interface OrganizerItem { movieId: number; mediaFileId: number; sourcePath: string; destinationPath: string; operation: string; valid: boolean; conflict?: string; fileSize: number }
export interface OrganizerPreview { taskId: number; status: string; confirmationToken: string; items: OrganizerItem[]; warnings: string[]; validItems: number; conflictItems: number }
export interface OrganizerLaunchResult { taskId: number; status: string; totalItems: number; message: string }
