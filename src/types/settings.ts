export interface SettingsSource {
  kind: string; path: string; exists: boolean; readOnly: boolean; size?: number
  lastModified?: string; sha256?: string; status: string; error?: string
}

export interface SettingField {
  key: string; category: string; section: string; label: string; source: string
  valueType: string; value: unknown; defaultValue: unknown; nullable: boolean
  requiresRestart: boolean; immediate: boolean; dangerous: boolean
  legacyCompatible: boolean; safeToWrite: boolean; sensitive: boolean
  readStatus: string; error?: string; mapped: boolean
}

export interface CrawlerServer {
  pluginId: string; url: string; enabled: boolean; available: number
  lastRefreshDate: string; cookies: string; headers: string
}

export interface SettingsSnapshot {
  readOnly: boolean; mode: string; readAt: string; sources: SettingsSource[]
  fields: SettingField[]; servers: CrawlerServer[]; mappedCount: number
  unmappedCount: number; errors: string[]
  metaTube: MetaTubeSettings
}

export interface MetaTubeSettings {
  enabled: boolean; baseUrl: string; timeoutSeconds: number; downloadImages: boolean
  writeNfo: boolean; autoExecute: boolean; nonDestructive: boolean
}
export interface MdcNgSettings {
  enabled: boolean; serviceUrl: string; apiUrl: string; timeoutSeconds: number
  apiKey: string; downloadImages: boolean; pathMappings?: MdcNgPathMapping[]
}
export interface MdcNgPathMapping { localPathPrefix: string; providerPathPrefix: string; enabled: boolean; order: number }
export interface JavBusSettings {
  enabled: boolean; priority: number; baseUrl: string; timeoutSeconds: number; retryCount: number
  cookie: string; downloadImages: boolean; fillMissingOnly: boolean; mirrorUrls?: string[]
}
export interface WebMetadataSettings {
  enabled: boolean; priority: number; baseUrl: string; timeoutSeconds: number; retryCount: number
  cookie: string; downloadImages: boolean; fillMissingOnly: boolean; mirrorUrls?: string[]
}
export interface ProviderNetworkSettings {
  proxyMode: 'System' | 'Direct' | 'Manual'
  proxyUrl: string
  username: string
  password: string
}

export interface ProviderConnectionResult { success: boolean; provider: string; message: string; elapsedMilliseconds: number }
export interface ProviderDiagnosticResult { provider: string; reachable: boolean; scope: string; recommendation: string; message: string; testedAt: string; elapsedMilliseconds: number; status: 'Available' | 'Partial' | 'Unavailable' | 'AuthenticationRequired' | 'RateLimited' | 'PathMappingMissing' | 'ConfigurationError'; lastSuccessfulAt?: string }
export interface NfoSettings { exportPolicy: 'SkipExisting' | 'SeparateFile'; outputDirectory: string; fillEmptyOnly: boolean; includeImages: boolean }
export interface PlaybackSettings { playerPath: string; useSystemDefault: boolean }
export interface RatingRetentionSettings { enabled: boolean }
export interface AppearanceSettings { themeMode: 'light' | 'dark' }
export interface MovieWallDisplaySettings {
  posterOrientation: 'portrait' | 'landscape'
  posterSize: 'small' | 'medium' | 'large'
  wallImageSource: 'poster' | 'thumbnail' | 'fanart'
  detailImageSource: 'poster' | 'thumbnail' | 'fanart'
  defaultViewMode: 'grid' | 'list'
  coverCropMode: 'AutoFace' | 'Left' | 'Center' | 'Right'
}
export interface GeneratedCoverSettings { enabled: boolean; checkOnStartup: boolean; backgroundGeneration: boolean; scope: 'All' | 'Recent'; maxConcurrentJobs: number }
export interface ScanSettings { minFileSizeMb: number }
export interface SearchSettings { defaultSort: string; defaultFilter: 'all' }
export interface DataBackupSettings { enabled: boolean; frequencyDays: 1 | 3 | 7; retentionCount: 5 | 10 | 20 }
export interface SystemSettings {
  language: 'system' | 'zh-CN'
  closeBehavior: 'exit' | 'minimizeToTray'
  startMinimizedToTray: boolean
  logRetentionDays: 0 | 7 | 14 | 30 | 90
  globalShortcutsEnabled: boolean
  autoCheckUpdates: boolean
  lastUpdateCheckAt?: string
}
export interface MediaStorageSettings {
  rootPath: string
  postersDirectory: string
  thumbnailsDirectory: string
  fanartDirectory: string
  previewsDirectory: string
  screenshotsDirectory: string
  wallCropsDirectory: string
  gifDirectory: string
  nfoDirectory: string
  movieFolderTemplate: string
  fileNameTemplate: string
  usingFallbackDefault?: boolean
}
export interface UnifiedSettings {
  mdcNg: MdcNgSettings
  metaTube: MetaTubeSettings
  javBus: JavBusSettings
  dmm: WebMetadataSettings
  javDb: WebMetadataSettings
  minnano: WebMetadataSettings
  wikipediaJp: WebMetadataSettings
  providerNetwork?: ProviderNetworkSettings
  nfo: NfoSettings
  playback: PlaybackSettings
  ratingRetention: RatingRetentionSettings
  appearance: AppearanceSettings
  mediaStorage: MediaStorageSettings
  movieWallDisplay: MovieWallDisplaySettings
  search: SearchSettings
  dataBackup: DataBackupSettings
  scan: ScanSettings
  system: SystemSettings
  rename: RenameSettings
}
export interface UnifiedSettingsSaveResult { settings: UnifiedSettings; changedFields: string[]; message: string }

export interface DataSafetyOverview {
  databasePath: string; databaseBytes: number; configDatabasePath: string
  backupDirectory: string; cacheDirectory: string; logDirectory: string; lastBackupAt?: string
}
export interface BackupCreateCommand { includeConfig: boolean; includeGeneratedCache: boolean }
export interface BackupResult { backupPath: string; bytes: number; createdAt: string; included: string[]; warnings: string[] }
export interface BackupValidation {
  valid: boolean; backupPath: string; bytes: number; createdAt: string; hasDatabase: boolean
  hasConfig: boolean; entries: string[]; errors: string[]
}
export interface RestorePlan { planPath: string; backupPath: string; mode: string; createdAt: string; steps: string[]; warnings: string[] }
export interface SettingsExport { exportedAt: string; product: string; version: string; settings: unknown }
export interface SettingsImportPreview { valid: boolean; version: string; categories: string[]; changes: string[]; warnings: string[] }
export interface DiagnosticCheck { key: string; label: string; status: 'success' | 'warning' | 'error' | 'info'; detail: string }
export interface SystemDiagnostic { checkedAt: string; checks: DiagnosticCheck[]; recentLogs: string[] }
export interface LogFile {
  name: string; path: string; bytes: number; lastWriteTime: string; active: boolean; eligible: boolean; reason: string
}
export interface LogCleanupPreview {
  logDirectory: string; retentionDays: number; fileCount: number; totalBytes: number; deletableCount: number
  deletableBytes: number; oldestLogTime?: string; activeLogs: string[]; files: LogFile[]; confirmationToken: string
}
export interface LogCleanupResult { deletedFiles: number; freedBytes: number; failedFiles: number; failures: string[]; message: string }
export interface UpdateCheckResult {
  currentVersion: string; status: 'up-to-date' | 'update-available' | 'network-error' | 'invalid-response' | 'current-newer'
  message: string; latestVersion?: string; releaseUrl?: string; releaseNotes?: string; checkedAt: string
}
export interface FfmpegToolStatus {
  found: boolean
  path?: string
  probePath?: string
  source: string
  version?: string
  probeVersion?: string
  pluginDirectory: string
  message: string
}
export interface FfmpegPluginSettings {
  executablePath: string
  threadCount: number
  autoScreenshotAfterLocalImport: boolean
  skipWhenScreenshotsExist: boolean
  candidateCount: number
  retainedCount: number
  maximumAttempts: number
  skipStartValue: number
  skipStartUnit: 'Percent' | 'Minutes'
  skipEndValue: number
  skipEndUnit: 'Percent' | 'Minutes'
  filterNoPerson: boolean
  filterBlackFrames: boolean
  filterDarkFrames: boolean
  filterBlurredFrames: boolean
  filterDuplicateFrames: boolean
}
export interface PersonDetectionStatus { available: boolean; unavailableReason?: string }
export interface RenameSettings {
  trimTitle: boolean
  renameAfterFavorite: boolean
  informationSeparator: string
  listSeparator: string
  template: string
}

export interface MdcNgToolStatus {
  serviceUrl: string
  apiUrl: string
  serviceReachable: boolean
  apiReachable: boolean
  version?: string
  lastCheckedAt: string
  message: string
}
