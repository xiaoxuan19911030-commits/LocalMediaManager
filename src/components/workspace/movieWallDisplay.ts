export type MovieWallPosterOrientation = 'portrait' | 'landscape'
export type MovieWallPosterSize = 'small' | 'medium' | 'large'
export type MovieImageSource = 'poster' | 'thumbnail' | 'fanart'
export type MovieWallDefaultViewMode = 'grid' | 'list'

export interface MovieWallDisplaySettings {
  posterOrientation: MovieWallPosterOrientation
  posterSize: MovieWallPosterSize
  wallImageSource: MovieImageSource
  detailImageSource: MovieImageSource
  defaultViewMode: MovieWallDefaultViewMode
  coverCropMode: 'AutoFace' | 'Left' | 'Center' | 'Right'
}

export const defaultMovieWallDisplay: MovieWallDisplaySettings = {
  posterOrientation: 'portrait',
  posterSize: 'medium',
  wallImageSource: 'poster',
  detailImageSource: 'fanart',
  defaultViewMode: 'grid',
  coverCropMode: 'AutoFace',
}

export const movieWallAspectRatio: Record<MovieWallPosterOrientation, string> = {
  portrait: '2 / 3',
  landscape: '16 / 9',
}

export const movieWallMinWidth: Record<MovieWallPosterOrientation, Record<MovieWallPosterSize, number>> = {
  portrait: {
    small: 150,
    medium: 178,
    large: 220,
  },
  landscape: {
    small: 220,
    medium: 280,
    large: 340,
  },
}

export function normalizeMovieWallDisplay(value?: Partial<MovieWallDisplaySettings>): MovieWallDisplaySettings {
  const posterOrientation = value?.posterOrientation === 'landscape' ? 'landscape' : 'portrait'
  const posterSize = value?.posterSize === 'small' || value?.posterSize === 'large' ? value.posterSize : 'medium'
  const wallImageSource = value?.wallImageSource === 'thumbnail' || value?.wallImageSource === 'fanart' ? value.wallImageSource : 'poster'
  const detailImageSource = value?.detailImageSource === 'poster' || value?.detailImageSource === 'thumbnail' ? value.detailImageSource : 'fanart'
  const defaultViewMode = value?.defaultViewMode === 'list' ? 'list' : 'grid'
  const coverCropMode = value?.coverCropMode === 'Left' || value?.coverCropMode === 'Center' || value?.coverCropMode === 'Right' ? value.coverCropMode : 'AutoFace'
  return { posterOrientation, posterSize, wallImageSource, detailImageSource, defaultViewMode, coverCropMode }
}
