import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import { Alert, Box, Button, CircularProgress, Snackbar } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { MediaCard } from '@/components/MediaCard'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { MediaItem } from '@/types/media'

export default function MediaPage() {
  const [items, setItems] = useState<MediaItem[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const load = useCallback(() => {
    setLoading(true); setError('')
    bridge.videos(24).then(setItems).catch((reason: Error) => setError(reason.message)).finally(() => setLoading(false))
  }, [])
  useEffect(load, [load])

  const play = (item: MediaItem) => {
    bridge.play(item.dataId)
      .then(() => setNotice(`已交给系统播放器：${item.code}`))
      .catch((reason: Error) => setNotice(`播放失败：${reason.message}`))
  }

  return (
    <Box>
      <PageHeader title="全部影片" description="通过只读 C# Bridge 加载现有 SQLite 数据。"
        action={<Button startIcon={<RefreshRoundedIcon />} variant="outlined" onClick={load}>刷新</Button>} />
      {error && <Alert severity="error">{error}</Alert>}
      {loading ? <Box sx={{ display: 'grid', placeItems: 'center', minHeight: 300 }}><CircularProgress /></Box> :
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(142px, 1fr))', gap: 1.5 }}>
          {items.map((item) => <MediaCard key={item.dataId} item={item} onPlay={play} />)}
        </Box>}
      <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice} />
    </Box>
  )
}

