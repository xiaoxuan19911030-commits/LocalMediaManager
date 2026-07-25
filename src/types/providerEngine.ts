export type ProviderHealthStatus = 'Available' | 'Partial' | 'Offline' | 'AuthenticationRequired' | 'RateLimited' | 'ConfigurationError' | 'ParserBroken' | 'Unsupported'
export type ProviderFailureCategory = 'Network' | 'Timeout' | 'NotFound' | 'RateLimited' | 'Parser' | 'Cloudflare' | 'Authentication' | 'EmptyResult' | 'NoMatch' | 'Configuration' | 'Unsupported' | 'Unknown'

export interface ProviderDescriptor {
  name: string
  priority: number
  minimumDelayMilliseconds: number
  capabilities: string[]
  requiresAuthentication: boolean
  requiresPath: boolean
  requiresMedia: boolean
}

export interface ProviderBenchmarkSnapshot {
  provider: string
  requestCount: number
  successCount: number
  failureCount: number
  successRate: number
  parserSuccessRate: number
  averageElapsedMilliseconds: number
  lastRequestAt?: string
  lastSuccessAt?: string
  lastError?: string
  lastFailureCategory?: ProviderFailureCategory
}

export interface ProviderDashboardItem {
  descriptor: ProviderDescriptor
  enabled: boolean
  status: ProviderHealthStatus
  benchmark: ProviderBenchmarkSnapshot
}

export interface ProviderRawResponse { provider: string; format: 'json' | 'html' | 'xml'; content: string }
export interface ProviderStageTrace { stage: string; status: string; elapsedMilliseconds: number; message?: string }
export interface ProviderMetadataResult {
  provider: string
  externalId: string
  code: string
  title?: string
  description?: string
  director?: string
  studio?: string
  publisher?: string
  series?: string
  durationSeconds?: number
  releaseDate?: string
  webUrl?: string
  genres: string[]
  actors: string[]
  images: Array<{ type: string; url: string; provider?: string }>
  rating?: number
}

export interface ProviderPlaygroundResult {
  provider: string
  code: string
  status: ProviderHealthStatus
  httpStatus?: number
  elapsedMilliseconds: number
  parserSucceeded: boolean
  fieldCount: number
  capabilities: string[]
  rawResponses: ProviderRawResponse[]
  providerResult?: ProviderMetadataResult
  mergePreview: { contributedFields: string[]; result?: ProviderMetadataResult }
  diagnostics: ProviderStageTrace[]
  cache: { hit: boolean; key: string; expiresAt?: string }
  failureCategory?: ProviderFailureCategory
  error?: string
}
