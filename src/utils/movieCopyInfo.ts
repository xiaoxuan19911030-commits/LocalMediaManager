export interface CopyInfoNamedItem {
  id?: number
  name?: string
}

export interface CopyInfoMediaFile {
  path?: string
  sourceType?: string
}

export interface CopyInfoMovie {
  id?: number
  code?: string
  title?: string
  originalTitle?: string
  releaseDate?: string
  description?: string
  userRating?: number
  userRatingSet?: boolean
  mediaFiles?: CopyInfoMediaFile[]
  actors?: CopyInfoNamedItem[]
  studios?: CopyInfoNamedItem[]
  series?: CopyInfoNamedItem[]
}

const UNKNOWN = '未知'
const EMPTY_DESCRIPTION = '暂无'

const text = (value?: string) => {
  const trimmed = value?.trim()
  return trimmed || UNKNOWN
}

const names = (items?: CopyInfoNamedItem[]) => {
  const values = (items ?? []).map((item) => item.name?.trim()).filter((name): name is string => Boolean(name))
  return values.length ? values.join('、') : UNKNOWN
}

const filePaths = (files?: CopyInfoMediaFile[]) => {
  const values = (files ?? []).map((file) => file.path?.trim()).filter((path): path is string => Boolean(path))
  return values.length ? values.join('\n') : UNKNOWN
}

const libraries = (files?: CopyInfoMediaFile[]) => {
  const values = Array.from(new Set((files ?? []).map((file) => file.sourceType?.trim()).filter((value): value is string => Boolean(value))))
  return values.length ? values.join('、') : UNKNOWN
}

export const formatCopyRating = (movie: CopyInfoMovie) => {
  if (!movie.userRatingSet) return '未评分'
  const rating = Math.max(0, Math.min(5, Number(movie.userRating ?? 0)))
  const filled = Math.round(rating)
  return `${'★'.repeat(filled)}${'☆'.repeat(5 - filled)}（${rating.toFixed(1)}/5）`
}

export function buildMovieCopyText(movie: CopyInfoMovie) {
  const title = text(movie.title || movie.originalTitle || movie.code || (movie.id ? `影片 ${movie.id}` : undefined))
  const description = movie.description?.trim() || EMPTY_DESCRIPTION
  return [
    ['标题', title],
    ['番号', text(movie.code)],
    ['演员', names(movie.actors)],
    ['厂商', names(movie.studios)],
    ['系列', names(movie.series)],
    ['发行日期', text(movie.releaseDate?.slice(0, 10))],
    ['评分', formatCopyRating(movie)],
    ['文件', filePaths(movie.mediaFiles)],
    ['媒体库', libraries(movie.mediaFiles)],
    ['简介', description],
  ].map(([label, value]) => `${label}：\n${value}`).join('\n\n')
}

export async function copyMovieInformation(movie: CopyInfoMovie, writeText: (value: string) => Promise<void>) {
  const value = buildMovieCopyText(movie)
  await writeText(value)
  return value
}
