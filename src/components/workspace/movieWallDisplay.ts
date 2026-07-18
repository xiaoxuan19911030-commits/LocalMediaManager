export type MovieWallPosterOrientation = 'portrait' | 'landscape'
export type MovieWallPosterSize = 'small' | 'medium' | 'large'

export interface MovieWallDisplaySettings {
  posterOrientation: MovieWallPosterOrientation
  posterSize: MovieWallPosterSize
}

export const defaultMovieWallDisplay: MovieWallDisplaySettings = {
  posterOrientation: 'portrait',
  posterSize: 'medium',
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
  return { posterOrientation, posterSize }
}
