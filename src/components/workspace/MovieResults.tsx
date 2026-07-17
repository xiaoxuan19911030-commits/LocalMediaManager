import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import { Box, Button, Card, CardContent, Chip, Pagination, Rating, Stack, Tooltip, Typography } from '@mui/material'
import { useNavigate } from 'react-router'
import { MediaCard, MediaCardGrid } from '@/components/MediaCard'
import { EmptyState, SectionTitle } from '@/components/ProductComponents'
import type { MediaItem } from '@/types/media'
import type { WorkspaceViewMode } from '@/components/workspace/Workspace'

export function useMovieActions({
  onNotice,
  play,
}: {
  onNotice: (message: string) => void
  play: (id: number) => Promise<unknown>
}) {
  const navigate = useNavigate()
  return {
    openMovie: (item: MediaItem | { dataId?: number; movieId?: number }, context?: unknown) => {
      const id = 'dataId' in item ? item.dataId : item.movieId
      if (id) navigate(`/movies/${id}`, context ? { state: { context } } : undefined)
    },
    playMovie: (item: MediaItem) => play(item.dataId).then(() => onNotice(`正在打开：${item.code || item.title || item.dataId}`)).catch((reason: Error) => onNotice(reason.message)),
  }
}

export function MovieResultContainer({
  title,
  total,
  items,
  view = 'grid',
  page,
  pageSize,
  onPageChange,
  onPlay,
  onOpen,
  emptyTitle = '暂无影片',
  emptyDescription = '有可展示的影片后会显示在这里。',
  selectable,
  selectedIds = [],
  onSelect,
  onRatingClick,
}: {
  title?: string
  total?: number
  items: MediaItem[]
  view?: WorkspaceViewMode
  page?: number
  pageSize?: number
  onPageChange?: (page: number) => void
  onPlay: (item: MediaItem) => void
  onOpen: (item: MediaItem) => void
  emptyTitle?: string
  emptyDescription?: string
  selectable?: boolean
  selectedIds?: number[]
  onSelect?: (item: MediaItem, selected: boolean) => void
  onRatingClick?: (item: MediaItem) => void
}) {
  const pages = total && pageSize ? Math.ceil(total / pageSize) : 0
  return <Stack spacing={2}>
    {title && <SectionTitle title={total === undefined ? title : `${title}（${total}）`}/>}
    {items.length === 0 ? <EmptyState title={emptyTitle} description={emptyDescription}/> : view === 'list'
      ? <MovieList items={items} onPlay={onPlay} onOpen={onOpen}/>
      : <MediaCardGrid>{items.map(item => <MediaCard key={item.dataId} item={item} selected={selectedIds.includes(item.dataId)} onSelect={selectable ? onSelect : undefined} onRatingClick={onRatingClick} onPlay={onPlay} onOpen={onOpen}/>)}</MediaCardGrid>}
    {pages > 1 && page && onPageChange && <Box sx={{ display: 'flex', justifyContent: 'center', pt: 1 }}>
      <Pagination count={pages} page={page} onChange={(_, value) => onPageChange(value)} color="primary"/>
    </Box>}
  </Stack>
}

export function MovieList({ items, onPlay, onOpen }: { items: MediaItem[]; onPlay: (item: MediaItem) => void; onOpen: (item: MediaItem) => void }) {
  return <Stack spacing={1}>
    {items.map(item => {
      const title = item.title || item.code || `#${item.dataId}`
      return <Card key={item.dataId} variant="outlined"><CardContent sx={{ py: 1.25, '&:last-child': { pb: 1.25 } }}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(130px,.7fr) minmax(220px,1.3fr) 120px 120px auto' }, gap: 1.25, alignItems: 'center' }}>
          <Box sx={{ minWidth: 0 }}><Typography noWrap sx={{ fontWeight: 800 }}>{item.code || `#${item.dataId}`}</Typography><Typography variant="caption" color="text.secondary">ID {item.dataId}</Typography></Box>
          <Tooltip title={title}><Typography noWrap>{title}</Typography></Tooltip>
          <Rating size="small" value={Math.max(0, Math.min(5, item.grade || 0))} readOnly/>
          <Stack direction="row" spacing={.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
            {item.favorite && <Chip size="small" color="error" label="收藏"/>}
            {item.metadataStatus && <Chip size="small" color={item.metadataStatus.state === 'complete' ? 'success' : item.metadataStatus.state === 'unscraped' ? 'error' : 'warning'} label={item.metadataStatus.label}/>}
          </Stack>
          <Stack direction="row" spacing={.5} sx={{ justifyContent: 'flex-end' }}>
            <Button size="small" startIcon={<PlayArrowRoundedIcon/>} onClick={() => onPlay(item)}>播放</Button>
            <Button size="small" startIcon={<OpenInNewRoundedIcon/>} onClick={() => onOpen(item)}>详情</Button>
          </Stack>
        </Box>
        {item.path && <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: .75, overflowWrap: 'anywhere' }}>{item.path}</Typography>}
      </CardContent></Card>
    })}
  </Stack>
}

export function MovieEmptyState({ title = '暂无影片', description = '当前条件下没有可展示的影片。' }: { title?: string; description?: string }) {
  return <EmptyState title={title} description={description}/>
}
