import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import { Box, Button, Card, CardContent, Rating, Stack, Tooltip, Typography } from '@mui/material'
import { useEffect, useRef } from 'react'
import type { MouseEvent } from 'react'
import { useNavigate } from 'react-router'
import { MediaCard, MediaCardGrid } from '@/components/MediaCard'
import { EmptyState, SectionTitle } from '@/components/ProductComponents'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import type { MovieWallDisplaySettings } from '@/components/workspace/movieWallDisplay'
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
  display,
  onPlay,
  onOpen,
  emptyTitle = '暂无影片',
  emptyDescription = '有可展示的影片后会显示在这里。',
  selectable,
  selectedIds = [],
  onSelect,
  onRatingClick,
  onContextMenu,
}: {
  title?: string
  total?: number
  items: MediaItem[]
  view?: WorkspaceViewMode
  display: MovieWallDisplaySettings
  onPlay: (item: MediaItem) => void
  onOpen: (item: MediaItem) => void
  emptyTitle?: string
  emptyDescription?: string
  selectable?: boolean
  selectedIds?: number[]
  onSelect?: (item: MediaItem, selected: boolean) => void
  onRatingClick?: (item: MediaItem, value: number | null) => void
  onContextMenu?: (event: MouseEvent, item: MediaItem) => void
}) {
  useEffect(() => {
    if (import.meta.env.DEV) console.debug('[MovieWall] Received Count', { count: items.length, total })
  }, [items.length, total])
  return <Stack spacing={2}>
    {title && <SectionTitle title={total === undefined ? title : `${title}（${total}）`}/>}
    {items.length === 0 ? <EmptyState title={emptyTitle} description={emptyDescription}/> : view === 'list'
      ? <MovieList items={items} onPlay={onPlay} onOpen={onOpen} onContextMenu={onContextMenu} onRatingClick={onRatingClick}/>
      : <MediaCardGrid display={display}>{items.map(item => <MediaCard key={item.dataId} item={item} display={display} selected={selectedIds.includes(item.dataId)} onSelect={selectable ? onSelect : undefined} onRatingClick={onRatingClick} onContextMenu={onContextMenu} onPlay={onPlay} onOpen={onOpen}/>)}</MediaCardGrid>}
  </Stack>
}

export function MovieList({ items, onPlay, onOpen, onContextMenu, onRatingClick }: { items: MediaItem[]; onPlay: (item: MediaItem) => void; onOpen: (item: MediaItem) => void; onContextMenu?: (event: MouseEvent, item: MediaItem) => void; onRatingClick?: (item: MediaItem, value: number | null) => void }) {
  const clickTimer = useRef<number | undefined>(undefined)
  useEffect(() => () => { if (clickTimer.current) window.clearTimeout(clickTimer.current) }, [])
  const openDelayed = (item: MediaItem) => {
    if (clickTimer.current) window.clearTimeout(clickTimer.current)
    clickTimer.current = window.setTimeout(() => onOpen(item), 180)
  }
  const playNow = (item: MediaItem) => {
    if (clickTimer.current) window.clearTimeout(clickTimer.current)
    onPlay(item)
  }
  return <Stack spacing={1}>
    {items.map(item => {
      const title = item.title || item.code || `#${item.dataId}`
      return <Card key={item.dataId} variant="outlined" onClick={() => openDelayed(item)} onDoubleClick={(event) => { event.preventDefault(); event.stopPropagation(); playNow(item) }} onContextMenu={(event) => onContextMenu?.(event, item)}><CardContent sx={{ py: 1.25, '&:last-child': { pb: 1.25 } }}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(130px,.7fr) minmax(220px,1.3fr) 120px 120px auto' }, gap: 1.25, alignItems: 'center' }}>
          <Box sx={{ minWidth: 0 }}><Typography noWrap sx={{ fontWeight: 800 }}>{item.code || `#${item.dataId}`}</Typography><Typography variant="caption" color="text.secondary">ID {item.dataId}</Typography></Box>
          <Tooltip title={title}><Typography noWrap>{title}</Typography></Tooltip>
          <Box onClick={(event) => event.stopPropagation()} onDoubleClick={(event) => event.stopPropagation()}>
            <Rating size="small" value={Math.max(0, Math.min(5, item.grade || 0))} onChange={(_, value) => onRatingClick?.(item, value)}/>
          </Box>
          <Stack direction="row" spacing={.5} useFlexGap sx={{ flexWrap: 'wrap' }}>
            {item.favorite && <StatusBadge tone="error" label="收藏"/>}
          </Stack>
          <Stack direction="row" spacing={.5} sx={{ justifyContent: 'flex-end' }}>
            <Button size="small" startIcon={<PlayArrowRoundedIcon/>} onClick={(event) => { event.stopPropagation(); onPlay(item) }}>播放</Button>
            <Button size="small" startIcon={<OpenInNewRoundedIcon/>} onClick={(event) => { event.stopPropagation(); onOpen(item) }}>详情</Button>
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
