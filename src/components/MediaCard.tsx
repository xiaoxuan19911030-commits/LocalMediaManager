import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import NewReleasesRoundedIcon from '@mui/icons-material/NewReleasesRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import { Box, Card, CardContent, Checkbox, Chip, IconButton, Rating, Tooltip, Typography } from '@mui/material'
import { alpha } from '@mui/material/styles'
import { useEffect, useLayoutEffect, useRef, useState, type MouseEvent, type ReactNode } from 'react'
import { SmartImage } from '@/components/SmartImage'
import { movieWallGridTemplate } from '@/components/workspace/movieWallGrid'
import { defaultMovieWallDisplay, movieWallAspectRatio, type MovieWallDisplaySettings } from '@/components/workspace/movieWallDisplay'
import type { MediaItem } from '@/types/media'
import { bridge } from '@/services/bridge'

const isRecent = (value: string) => {
  if (!value) return false
  const time = new Date(value).getTime()
  return Number.isFinite(time) && Date.now() - time < 30 * 24 * 60 * 60 * 1000
}

export function MediaCardGrid({ children, display = defaultMovieWallDisplay, onPageCapacityChange }: { children: ReactNode; display?: MovieWallDisplaySettings; onPageCapacityChange?: (capacity: number) => void }) {
  const gridRef = useRef<HTMLDivElement>(null)
  useLayoutEffect(() => {
    const grid = gridRef.current
    if (!grid || !onPageCapacityChange) return
    const updateCapacity = () => {
      const card = grid.firstElementChild as HTMLElement | null
      if (!card) return
      const styles = window.getComputedStyle(grid)
      const gap = Number.parseFloat(styles.columnGap) || 0
      const cardRect = card.getBoundingClientRect()
      if (cardRect.width <= 0 || cardRect.height <= 0) return
      const columns = Math.max(1, Math.round((grid.getBoundingClientRect().width + gap) / (cardRect.width + gap)))
      const rowHeight = cardRect.height + (Number.parseFloat(styles.rowGap) || gap)
      const availableHeight = Math.max(rowHeight, window.innerHeight - grid.getBoundingClientRect().top - 16)
      const rows = Math.max(1, Math.ceil(availableHeight / rowHeight))
      onPageCapacityChange(Math.min(96, columns * rows))
    }
    const observer = new ResizeObserver(updateCapacity)
    observer.observe(grid)
    window.addEventListener('resize', updateCapacity)
    updateCapacity()
    return () => { observer.disconnect(); window.removeEventListener('resize', updateCapacity) }
  }, [children, display.posterOrientation, display.posterSize, onPageCapacityChange])

  return <Box ref={gridRef} sx={{
    display: 'grid',
    gridTemplateColumns: movieWallGridTemplate(display.posterOrientation, display.posterSize),
    gap: { xs: 1.25, md: 1.5 },
    minWidth: 0,
    width: '100%',
  }}>{children}</Box>
}

export function MediaCard({ item, display = defaultMovieWallDisplay, onPlay, onOpen, selected, onSelect, onRatingClick, onContextMenu }: { item: MediaItem; display?: MovieWallDisplaySettings; onPlay: (item: MediaItem) => void; onOpen?: (item: MediaItem) => void; selected?: boolean; onSelect?: (item: MediaItem, selected: boolean) => void; onRatingClick?: (item: MediaItem, value: number | null) => void; onContextMenu?: (event: MouseEvent, item: MediaItem) => void }) {
  const [coverFailed, setCoverFailed] = useState(false)
  const [coverPosition, setCoverPosition] = useState('50% 50%')
  if (import.meta.env.DEV) console.debug('[MediaCard] Render Start', { id: item.dataId, code: item.code })
  const clickTimer = useRef<number | undefined>(undefined)
  useEffect(() => setCoverFailed(false), [item.coverUrl])
  useEffect(() => setCoverPosition('50% 50%'), [item.coverUrl])
  const resolveCoverFocus = () => bridge.coverCrop(item.dataId)
    .then(crop => setCoverPosition(`${(crop.focusX * 100).toFixed(2)}% ${(crop.focusY * 100).toFixed(2)}%`))
    .catch(() => undefined)
  useEffect(() => { void resolveCoverFocus() }, [item.coverUrl, item.dataId])
  useEffect(() => {
    const refreshCoverFocus = (event: Event) => {
      if ((event as CustomEvent<number>).detail === item.dataId) resolveCoverFocus()
    }
    window.addEventListener('lmm:cover-crop-updated', refreshCoverFocus)
    return () => window.removeEventListener('lmm:cover-crop-updated', refreshCoverFocus)
  }, [item.dataId])
  useEffect(() => () => { if (clickTimer.current) window.clearTimeout(clickTimer.current) }, [])
  const recent = isRecent(item.importedAt)
  const displayTitle = item.title && !item.title.includes('\uFFFD') ? item.title : ''
  const primaryText = item.code || displayTitle || `影片 ${item.dataId}`
  return (
    <Card role={onOpen ? 'button' : undefined} tabIndex={onOpen ? 0 : undefined} onContextMenu={(event) => onContextMenu?.(event, item)} onDoubleClick={(event) => { event.preventDefault(); event.stopPropagation(); if (clickTimer.current) window.clearTimeout(clickTimer.current); onPlay(item) }} onKeyDown={(event) => { if (onOpen && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); onOpen(item) } }}
      onClick={() => { if (!onOpen) return; if (clickTimer.current) window.clearTimeout(clickTimer.current); clickTimer.current = window.setTimeout(() => onOpen(item), 180) }} sx={{ overflow: 'hidden', minWidth: 0, cursor: onOpen ? 'pointer' : 'default', position: 'relative', borderColor: selected ? 'primary.main' : undefined,
        transition: 'transform .22s cubic-bezier(.2,.8,.2,1), border-color .22s ease, box-shadow .22s ease',
        '&:hover': onOpen ? { transform: 'translateY(-5px)', borderColor: 'primary.main', boxShadow: (theme) => `0 14px 32px ${alpha(theme.palette.common.black, theme.palette.mode === 'dark' ? .32 : .14)}` } : undefined,
        '&:hover .media-play': { opacity: 1, transform: 'translate(-50%,-50%) scale(1)' }, '&:hover .media-image': { transform: 'scale(1.025)' },
        '@media (prefers-reduced-motion: reduce)': { transition: 'none', '& .media-image, & .media-play': { transition: 'none' } } }}>
      <Box sx={{ position: 'relative', aspectRatio: movieWallAspectRatio[display.posterOrientation], bgcolor: 'action.hover', overflow: 'hidden' }}>
        {item.coverUrl && !coverFailed ? (
          <Box className="media-image" sx={{ position: 'absolute', inset: 0, transition: 'transform .35s cubic-bezier(.2,.8,.2,1)' }}>
            <SmartImage src={item.coverUrl} alt={primaryText} position={coverPosition} onError={() => setCoverFailed(true)}/>
          </Box>
        ) : (
          <Box sx={{ height: '100%', display: 'grid', placeItems: 'center', color: 'text.disabled', px: 1.5, textAlign: 'center', bgcolor: 'action.hover' }}>暂无海报</Box>
        )}
        <Box sx={{ position: 'absolute', top: 8, left: 8, right: 8, display: 'flex', gap: .75, alignItems: 'start', flexWrap: 'wrap' }}>
          {onSelect && (
            <Checkbox checked={Boolean(selected)} onClick={(event) => event.stopPropagation()} onChange={(_, checked) => onSelect(item, checked)} slotProps={{ input: { 'aria-label': `选择 ${primaryText}` } }} sx={{ p: .5, bgcolor: 'rgba(10,13,20,.72)', borderRadius: 1.5, color: 'common.white', '&.Mui-checked': { color: 'primary.light' } }}/>
          )}
          {recent && (
            <Chip size="small" color="success" icon={<NewReleasesRoundedIcon/>} label="新加入" sx={{ fontWeight: 750 }}/>
          )}
          {item.favorite && (
            <Chip size="small" color="error" icon={<FavoriteRoundedIcon/>} label="已收藏" sx={{ fontWeight: 750 }}/>
          )}
        </Box>
        <Tooltip title="播放">
          <IconButton className="media-play" onClick={(event) => { event.stopPropagation(); onPlay(item) }} color="primary"
            sx={{ position: 'absolute', left: '50%', top: '50%', opacity: { xs: 1, md: 0 }, transform: { xs: 'translate(-50%,-50%) scale(1)', md: 'translate(-50%,-50%) scale(.82)' },
              transition: 'opacity .2s ease, transform .2s ease', bgcolor: 'rgba(10,13,20,.86)', backdropFilter: 'blur(8px)', width: 46, height: 46,
              '&:hover': { bgcolor: 'primary.main', color: 'primary.contrastText' } }}>
            <PlayArrowRoundedIcon />
          </IconButton>
        </Tooltip>
      </Box>
      <CardContent sx={{ p: 1.1, '&:last-child': { pb: 1.1 } }}>
        <Typography noWrap align="center" sx={{ fontWeight: 800 }}>{item.code || `#${item.dataId}`}</Typography>
        <Tooltip title={displayTitle || primaryText} placement="top"><Typography variant="caption" color="text.secondary" noWrap align="center" sx={{ display: 'block', mt: .15 }}>{displayTitle || primaryText}</Typography></Tooltip>
        <Typography variant="body2" color="text.secondary" noWrap align="center" sx={{ mt: .6 }}>
          {item.importedAt?.slice(0, 10) || '日期未知'}
        </Typography>
        <Box onClick={(event) => event.stopPropagation()} onDoubleClick={(event) => event.stopPropagation()} sx={{ display: 'flex', justifyContent: 'center' }}>
          <Rating size="small" value={Math.max(0, Math.min(5, item.grade))} onChange={(_, value) => onRatingClick?.(item, value)} sx={{ mt: .5, display: 'flex' }} />
        </Box>
      </CardContent>
    </Card>
  )
}
