import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import NewReleasesRoundedIcon from '@mui/icons-material/NewReleasesRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import { Box, Card, CardContent, Checkbox, Chip, IconButton, Rating, Tooltip, Typography } from '@mui/material'
import { alpha } from '@mui/material/styles'
import { useEffect, useState, type ReactNode } from 'react'
import type { MediaItem } from '@/types/media'

const isRecent = (value: string) => {
  if (!value) return false
  const time = new Date(value).getTime()
  return Number.isFinite(time) && Date.now() - time < 30 * 24 * 60 * 60 * 1000
}

export function MediaCardGrid({ children, minWidth = 148 }: { children: ReactNode; minWidth?: number }) {
  return <Box sx={{ display: 'grid', gridTemplateColumns: `repeat(auto-fill, minmax(${minWidth}px, 1fr))`, gap: { xs: 1.25, md: 1.5 } }}>{children}</Box>
}

export function MediaCard({ item, onPlay, onOpen, selected, onSelect, onRatingClick }: { item: MediaItem; onPlay: (item: MediaItem) => void; onOpen?: (item: MediaItem) => void; selected?: boolean; onSelect?: (item: MediaItem, selected: boolean) => void; onRatingClick?: (item: MediaItem) => void }) {
  const [coverFailed, setCoverFailed] = useState(false)
  useEffect(() => setCoverFailed(false), [item.coverUrl])
  const recent = isRecent(item.importedAt)
  const displayTitle = item.title && !item.title.includes('\uFFFD') ? item.title : ''
  const primaryText = item.code || displayTitle || `影片 ${item.dataId}`
  const metadataTone = item.metadataStatus?.state === 'complete' ? 'success.main' : item.metadataStatus?.state === 'unscraped' ? 'error.main' : 'warning.main'
  const metadataTip = item.metadataStatus?.missingItems?.length ? `缺少：${item.metadataStatus.missingItems.join('、')}` : item.metadataStatus?.label
  return (
    <Card role={onOpen ? 'button' : undefined} tabIndex={onOpen ? 0 : undefined} onKeyDown={(event) => { if (onOpen && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); onOpen(item) } }}
      onClick={() => onOpen?.(item)} sx={{ overflow: 'hidden', minWidth: 0, cursor: onOpen ? 'pointer' : 'default', position: 'relative', borderColor: selected ? 'primary.main' : undefined,
        transition: 'transform .22s cubic-bezier(.2,.8,.2,1), border-color .22s ease, box-shadow .22s ease',
        '&:hover': onOpen ? { transform: 'translateY(-5px)', borderColor: 'primary.main', boxShadow: (theme) => `0 14px 32px ${alpha(theme.palette.common.black, theme.palette.mode === 'dark' ? .32 : .14)}` } : undefined,
        '&:hover .media-play': { opacity: 1, transform: 'translate(-50%,-50%) scale(1)' }, '&:hover .media-image': { transform: 'scale(1.025)' },
        '@media (prefers-reduced-motion: reduce)': { transition: 'none', '& .media-image, & .media-play': { transition: 'none' } } }}>
      <Box sx={{ position: 'relative', aspectRatio: '2 / 3', bgcolor: 'action.hover', overflow: 'hidden' }}>
        {item.coverUrl && !coverFailed ? (
          <Box className="media-image" component="img" src={item.coverUrl} alt={primaryText} loading="lazy"
            onError={() => setCoverFailed(true)}
            sx={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block', transition: 'transform .35s cubic-bezier(.2,.8,.2,1)' }} />
        ) : (
          <Box sx={{ height: '100%', display: 'grid', placeItems: 'center', color: 'text.disabled', px: 1.5, textAlign: 'center' }}>{coverFailed ? '图片损坏或不可用' : '暂无海报'}</Box>
        )}
        <Box sx={{ position: 'absolute', inset: 0, background: 'linear-gradient(180deg, rgba(5,8,14,.08) 45%, rgba(5,8,14,.78) 100%)', pointerEvents: 'none' }}/>
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
        {item.metadataStatus && <Tooltip title={metadataTip}>
          <Box sx={{ position: 'absolute', right: 8, bottom: 8, width: 28, height: 28, borderRadius: 1.25, display: 'grid', placeItems: 'center', bgcolor: 'rgba(10,13,20,.82)', color: metadataTone, fontWeight: 900, border: 1, borderColor: 'rgba(255,255,255,.18)' }}>
            {item.metadataStatus.icon}
          </Box>
        </Tooltip>}
      </Box>
      <CardContent sx={{ p: 1.4, '&:last-child': { pb: 1.4 } }}>
        <Tooltip title={primaryText} placement="top"><Typography noWrap sx={{ fontWeight: 800, letterSpacing: '.01em' }}>{primaryText}</Typography></Tooltip>
        {displayTitle && displayTitle !== item.code && <Typography variant="caption" color="text.secondary" noWrap sx={{ display: 'block', mt: .25 }}>{displayTitle}</Typography>}
        <Typography variant="body2" color="text.secondary" noWrap sx={{ mt: 0.7 }}>
          {item.importedAt?.slice(0, 10) || '日期未知'}
        </Typography>
        <Box onClick={(event) => { if (onRatingClick) { event.stopPropagation(); onRatingClick(item) } }} sx={{ display: 'inline-flex' }}>
          <Rating size="small" value={Math.max(0, Math.min(5, item.grade))} readOnly sx={{ mt: 0.55, display: 'flex' }} />
        </Box>
      </CardContent>
    </Card>
  )
}
