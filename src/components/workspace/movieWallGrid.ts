import type { MovieWallPosterOrientation, MovieWallPosterSize } from '@/components/workspace/movieWallDisplay'

type CardWidthRule = { min: number }

const cardWidthRules: Record<MovieWallPosterOrientation, Record<MovieWallPosterSize, CardWidthRule>> = {
  portrait: {
    small: { min: 120 },
    medium: { min: 175 },
    large: { min: 180 },
  },
  landscape: {
    small: { min: 180 },
    medium: { min: 240 },
    large: { min: 300 },
  },
}

export function movieWallGridTemplate(orientation: MovieWallPosterOrientation, size: MovieWallPosterSize) {
  const { min } = cardWidthRules[orientation][size]
  return `repeat(auto-fill, minmax(min(100%, ${min}px), 1fr))`
}
