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
}
