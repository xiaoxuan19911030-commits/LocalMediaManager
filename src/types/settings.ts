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

export interface ProviderConnectionResult { success: boolean; provider: string; message: string; elapsedMilliseconds: number }
export interface NfoSettings { exportPolicy: 'SkipExisting' | 'SeparateFile'; outputDirectory: string; fillEmptyOnly: boolean; includeImages: boolean }
export interface PlaybackSettings { playerPath: string; useSystemDefault: boolean }
export interface RatingRetentionSettings { enabled: boolean }
export interface AppearanceSettings { themeMode: 'light' | 'dark' }
export interface MediaStorageSettings {
  rootPath: string
  postersDirectory: string
  thumbnailsDirectory: string
  fanartDirectory: string
  previewsDirectory: string
  screenshotsDirectory: string
  gifDirectory: string
  nfoDirectory: string
  movieFolderTemplate: string
  fileNameTemplate: string
  usingFallbackDefault?: boolean
}
export interface UnifiedSettings {
  metaTube: MetaTubeSettings
  nfo: NfoSettings
  playback: PlaybackSettings
  ratingRetention: RatingRetentionSettings
  appearance: AppearanceSettings
  mediaStorage: MediaStorageSettings
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
