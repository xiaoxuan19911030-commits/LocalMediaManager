export interface BuildInfo {
  version: string
  commit: string
  buildTime: string
}

export const buildInfo: BuildInfo = {
  version: import.meta.env.VITE_LMM_VERSION || 'unknown',
  commit: import.meta.env.VITE_LMM_COMMIT || 'unknown',
  buildTime: import.meta.env.VITE_LMM_BUILD_TIME || 'unknown',
}

export const shortBuildLabel = `${buildInfo.version} · ${buildInfo.commit}`
