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

export interface BridgeHealth {
  status: string
  databaseAvailable: boolean
  databasePath: string
  readOnly: boolean
}

