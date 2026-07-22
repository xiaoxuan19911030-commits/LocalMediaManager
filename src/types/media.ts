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
  product?: string
  version?: string
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
  actorCount: number
  directorCount: number
  tagCount: number
  seriesCount: number
  studioCount: number
  maintenance: DashboardMaintenance
  metadataHealth: DashboardMetadataHealth
  recentActivity: DashboardActivity[]
  libraries: DashboardLibrary[]
  topTags: DashboardEntity[]
  topActors: DashboardEntity[]
  topDirectors: DashboardEntity[]
  topStudios: DashboardEntity[]
  topSeries: DashboardEntity[]
  recentImports: MediaItem[]
  recentPlays: MediaItem[]
}

export interface DashboardMaintenance {
  healthyMovies: number
  pendingMovies: number
  unscrapedMovies: number
  duplicateMovies: number
  missingImages: number
  missingNfo: number
  cacheProblems: number
}

export interface DashboardMetadataHealth {
  completeRate: number
  imageRate: number
  nfoRate: number
  actorRate: number
  tagRate: number
}

export interface DashboardActivity {
  type: string
  title: string
  detail: string
  createdAt?: string
  movieId?: number
}

export interface DashboardLibrary {
  id: number
  name: string
  movieCount: number
  fileBytes: number
  lastUpdatedAt?: string
}

export interface DashboardEntity {
  id: number
  name: string
  movieCount: number
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
export interface TaskCleanupResult { count: number; message: string }
export interface PlatformOpenResult { path: string; message: string }

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
  sourceUrl?: string
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
export interface ActorDetail { id: number; name: string; alias?: string; gender?: number; birthDate?: string; description?: string; heightCm?: number; cup?: string; birthPlace?: string; activityPeriod?: string }
export interface EntityPageResult { items: EntityCard[]; total: number; limit: number; offset: number }
export interface RandomMovieResult { item?: MediaItem; items: MediaItem[]; total: number; limit: number }
export interface AdvancedSearchFilters {
  query: string; actorId?: number; tagId?: number; directorId?: number; movieTagId?: number; customTagId?: number; genreId?: number; seriesId?: number; studioId?: number; favorite?: boolean; watched?: boolean; ratingMin?: number; ratingFilter?: string
  metadata?: string; fileStatus?: string; metadataStatus?: string; libraryId?: number; sort?: string; limit?: number; offset?: number; targetFields?: string[]
}
export interface MetadataOverview {
  totalMovies: number; scrapedMovies: number; completeMovies: number; pendingMovies: number; unscrapedMovies: number
  missingTitle: number; missingCover: number; missingFanart: number; missingPreview: number
  missingActors: number; missingTags: number; missingDescription: number; missingNfo: number; missingFiles: number
  missingScreenshots: number; missingGif: number; missingDirectors: number; missingSeries: number; missingStudios: number; missingCustomTags: number
}
export interface FilteredMovieSyncPreview { count: number }
export interface MetadataHealthField { key: string; missing: number; required: boolean }
export interface MetadataHealthSummary { totalMovies: number; completeMovies: number; incompleteMovies: number; completeRate: number; fields: MetadataHealthField[]; analysisDurationMs: number; analyzedAt: string }
export interface MetadataHealthAnalysisState { running: boolean; invalidated: boolean; stage: string; completedSteps: number; totalSteps: number; percent: number; elapsedMilliseconds: number; result?: MetadataHealthSummary; error?: string }
export interface MediaStorageAvailability { rootPath: string; available: boolean; error?: string }
export interface FilteredMovieSyncResult { count: number; message: string }
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
  recommendationReasons: string[]
  fileName: string
  fileSize: number
  resolutionWidth: number
  resolutionHeight: number
  favorite: boolean
  userRating: number
  userRatingSet: boolean
  libraryName: string
  sourceType: string
  metadataScore: number
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
export interface MaintenanceIssue { category: string; severity: string; title: string; detail: string; movieId?: number; path?: string; actorId?: number }
export interface ActorProfileData { birthDate?: string; heightCm?: number; cup?: string; birthPlace?: string; activityPeriod?: string; description?: string; aliases?: string[]; avatarUrl?: string }
export interface ActorProfileCandidate { source: string; matchedName: string; sourceUrl: string; confidence: number; profile: ActorProfileData }
export interface ActorProfilePreview { actorId: number; candidates: ActorProfileCandidate[]; warnings: string[] }
export interface ActorProfileCompleteResult { checked: number; updatedProfiles: number; downloadedAvatars: number; skipped: number; warnings: string[] }
export interface ActorProfileCompleteLaunchResult { taskId: number; status: string; totalItems: number; message: string }
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
export interface SafeDeletePathPreview { kind: string; path: string; exists: boolean; size: number; willDelete: boolean; status: string; reason?: string }
export interface SafeDeleteMoviePreview {
  movieId: number
  code: string
  title: string
  primaryPath?: string
  primaryExists: boolean
  estimatedBytes: number
  databaseInfo: string[]
  files: SafeDeletePathPreview[]
  warnings: string[]
}
export interface SafeDeletePreview {
  mode: 'metadata' | 'media'
  deleteDatabaseInfo: boolean
  movieCount: number
  estimatedBytes: number
  deletesOriginalMedia: boolean
  requiresStrongConfirmation: boolean
  confirmationToken: string
  warnings: string[]
  items: SafeDeleteMoviePreview[]
}
export interface SafeDeletePreviewCommand { movieIds: number[]; mode: 'metadata' | 'media'; deleteDatabaseInfo?: boolean }
export interface SafeDeleteLaunchResult { taskId: number; status: string; totalItems: number; message: string }
export interface DuplicateDeleteGroupCommand { groupKey: string; keepMovieId: number; candidateMovieIds: number[] }
export interface DuplicateMergePreview {
  groupKey: string
  keepMovieId: number
  deleteMovieIds: number[]
  favoriteWillMerge: boolean
  ratingToApply?: number
  ratingConflict: boolean
  ratingConflictDetail?: string
  tagsToMerge: string[]
  playCountToApply: number
  lastPlayedAtToApply?: string
  notesConflict: boolean
  notesConflictDetail?: string
  warnings: string[]
}
export interface DuplicateDeletePreview {
  safeDelete: SafeDeletePreview
  merges: DuplicateMergePreview[]
  canExecute: boolean
  confirmationToken: string
  warnings: string[]
  blockers: string[]
}

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
export interface ImageCropCommand { sourceImageId?: number; aspectRatio: number; anchor?: 'left' | 'center' | 'right' }
export interface ImageDeletePreview { imageId: number; type: string; fileName: string; path?: string; fileWillBeDeleted: boolean; confirmationToken: string; warnings: string[] }
export interface ImageTaskLaunchResult { taskId: number; status: string; type: string; message: string }
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
