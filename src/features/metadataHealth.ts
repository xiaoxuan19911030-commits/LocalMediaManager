export const metadataHealthFilters = {
  incomplete: '所有不完整影片', complete: '完整影片', 'missing-director': '缺导演', 'missing-studio': '缺厂商', 'missing-series': '缺系列',
  'missing-description': '缺简介', 'missing-duration': '缺时长', 'missing-actors': '缺演员', 'missing-tags': '缺官方标签',
  'missing-poster': '缺 Poster', 'missing-fanart': '缺 Fanart', 'missing-preview': '缺 Preview', 'missing-screenshot': '缺 Screenshot',
  'missing-nfo': '缺 NFO', 'missing-media': '原始媒体文件不存在',
} as const
export type MetadataHealthFilter = keyof typeof metadataHealthFilters
export const isMetadataHealthFilter = (value: string | null): value is MetadataHealthFilter => Boolean(value && value in metadataHealthFilters)
