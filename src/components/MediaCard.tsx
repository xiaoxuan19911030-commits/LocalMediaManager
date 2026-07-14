import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import { Box, Card, CardContent, Chip, IconButton, Rating, Tooltip, Typography } from '@mui/material'
import type { MediaItem } from '@/types/media'

export function MediaCard({ item, onPlay }: { item: MediaItem; onPlay: (item: MediaItem) => void }) {
  return (
    <Card sx={{ overflow: 'hidden', minWidth: 0 }}>
      <Box sx={{ position: 'relative', aspectRatio: '2 / 3', bgcolor: 'action.hover' }}>
        {item.coverUrl ? (
          <Box component="img" src={item.coverUrl} alt={item.code} loading="lazy"
            sx={{ width: '100%', height: '100%', objectFit: 'cover', display: 'block' }} />
        ) : (
          <Box sx={{ height: '100%', display: 'grid', placeItems: 'center', color: 'text.disabled' }}>暂无海报</Box>
        )}
        <Tooltip title="播放">
          <IconButton onClick={() => onPlay(item)} color="primary"
            sx={{ position: 'absolute', right: 8, bottom: 8, bgcolor: 'rgba(15,17,23,.82)', '&:hover': { bgcolor: 'rgba(15,17,23,.95)' } }}>
            <PlayArrowRoundedIcon />
          </IconButton>
        </Tooltip>
      </Box>
      <CardContent sx={{ p: 1.5, '&:last-child': { pb: 1.5 } }}>
        <Box sx={{ display: 'flex', gap: 1, alignItems: 'center', minWidth: 0 }}>
          <Typography noWrap sx={{ flex: 1, fontWeight: 750 }}>{item.code || item.title}</Typography>
          {item.favorite && <Chip size="small" color="error" icon={<FavoriteRoundedIcon />} label="收藏" />}
        </Box>
        <Typography variant="body2" color="text.secondary" noWrap sx={{ mt: 0.75 }}>
          {item.importedAt?.slice(0, 10) || '日期未知'}
        </Typography>
        <Rating size="small" value={Math.max(0, Math.min(5, item.grade))} readOnly sx={{ mt: 0.75 }} />
      </CardContent>
    </Card>
  )
}
