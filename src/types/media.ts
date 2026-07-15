export interface LibrarySummary {
  videoCount: number
  databasePath: string
  readOnly: boolean
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
  mediaFiles: MediaFileItem[]
  actors: NamedItem[]
  tags: NamedItem[]
  genres: NamedItem[]
  studios: NamedItem[]
  series: NamedItem[]
}

export interface EntityCard { id: number; name: string; movieCount: number; imageUrl?: string }
export interface ActorDetail { id: number; name: string; alias?: string; gender?: number; birthDate?: string; description?: string }
export interface EntityPageResult { items: EntityCard[]; total: number; limit: number; offset: number }
export interface AdvancedSearchFilters {
  query: string; actorId?: number; tagId?: number; favorite?: boolean; ratingMin?: number
  metadata?: string; fileStatus?: string; libraryId?: number; sort?: string; limit?: number; offset?: number
}
export interface MetadataOverview {
  totalMovies: number; scrapedMovies: number; missingTitle: number; missingCover: number
  missingActors: number; missingTags: number; missingNfo: number; missingFiles: number
}
export interface DiagnosticItem { severity: 'error' | 'warning' | 'info'; code: string; title: string; detail: string; count: number }
export interface DiagnosticsResult { integrity: string; foreignKeyErrors: number; items: DiagnosticItem[] }
export interface MutationResult { changed: boolean; auditId: number; message: string }
export interface ImpactPreview { operation: string; entityId: number; name: string; affectedMovies: number; confirmationToken: string; warnings: string[] }
export interface ActorRepairPreview { candidateActors: number; affectedRelations: number; confirmationToken: string; warnings: string[] }
export interface NeighborResult { previousId?: number; nextId?: number }
export interface MovieDeletePreview { movieId: number; code: string; fileName: string; ratingWillBeRemembered: boolean; confirmationToken: string; warnings: string[] }
