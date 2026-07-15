import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import { Alert, Box, Button, CircularProgress, InputAdornment, MenuItem, Pagination, Snackbar, TextField } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { MediaCard } from '@/components/MediaCard'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { MediaItem } from '@/types/media'

const pageSize = 24

export default function MediaPage() {
  const navigate = useNavigate()
  const [items, setItems] = useState<MediaItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState('newest')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    bridge.videos(pageSize, (page - 1) * pageSize, query, sort)
      .then((result) => { setItems(result.items); setTotal(result.total) })
      .catch((reason: Error) => setError(reason.message))
      .finally(() => setLoading(false))
  }, [page, query, sort])
  useEffect(load, [load])

  const submitSearch = () => { setPage(1); setQuery(search.trim()) }
  const play = (item: MediaItem) => {
    bridge.play(item.dataId)
      .then(() => setNotice(`已交给系统播放器：${item.code}`))
      .catch((reason: Error) => setNotice(`播放失败：${reason.message}`))
  }

  return (
    <Box>
      <PageHeader title="影片墙" description={`共 ${total} 部影片，支持搜索、排序和分页浏览。`}
        action={<Button startIcon={<RefreshRoundedIcon />} variant="outlined" onClick={load}>刷新</Button>} />
      <Box component="form" onSubmit={(event) => { event.preventDefault(); submitSearch() }}
        sx={{ display: 'flex', gap: 1.25, mb: 2, alignItems: 'center', flexWrap: 'wrap' }}>
        <TextField size="small" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="搜索番号或标题"
          sx={{ minWidth: 260 }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon /></InputAdornment> } }} />
        <TextField select size="small" value={sort} onChange={(event) => { setPage(1); setSort(event.target.value) }} sx={{ width: 150 }}>
          <MenuItem value="newest">最新导入</MenuItem><MenuItem value="code">番号</MenuItem>
          <MenuItem value="title">标题</MenuItem><MenuItem value="release">发行日期</MenuItem><MenuItem value="rating">个人评分</MenuItem>
        </TextField>
        <Button type="submit" variant="contained">搜索</Button>
      </Box>
      {error && <Alert severity="error">{error}</Alert>}
      {loading ? <Box sx={{ display: 'grid', placeItems: 'center', minHeight: 300 }}><CircularProgress /></Box> :
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(142px, 1fr))', gap: 1.5 }}>
          {items.map((item) => <MediaCard key={item.dataId} item={item} onPlay={play} onOpen={() => navigate(`/movies/${item.dataId}`)} />)}
        </Box>}
      {total > pageSize && <Box sx={{ display: 'flex', justifyContent: 'center', pt: 3 }}>
        <Pagination count={Math.ceil(total / pageSize)} page={page} onChange={(_, value) => setPage(value)} color="primary" />
      </Box>}
      <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice} />
    </Box>
  )
}
