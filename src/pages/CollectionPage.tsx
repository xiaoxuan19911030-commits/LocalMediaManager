import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded'
import { Alert, Box, CircularProgress, Pagination, Snackbar, Stack } from '@mui/material'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { MediaCard, MediaCardGrid } from '@/components/MediaCard'
import { EmptyState } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { MediaItem } from '@/types/media'

const pageSize = 48
export default function CollectionPage({ kind }: { kind: 'favorites' | 'history' }) {
  const favorite = kind === 'favorites'; const navigate = useNavigate(); const [page, setPage] = useState(1); const [items, setItems] = useState<MediaItem[]>(); const [total, setTotal] = useState(0); const [error, setError] = useState(''); const [notice, setNotice] = useState('')
  useEffect(() => { setItems(undefined); setError(''); bridge.collection(kind, pageSize, (page - 1) * pageSize).then((result) => { setItems(result.items); setTotal(result.total) }).catch((reason: Error) => setError(reason.message)) }, [kind, page])
  const play = (item: MediaItem) => bridge.play(item.dataId).then(() => setNotice(`正在打开：${item.code}`)).catch((reason: Error) => setNotice(reason.message))
  return <Box><PageHeader title={favorite ? '我的收藏' : '最近播放'} description={favorite ? `共 ${total} 部已收藏影片；当前为只读兼容视图。` : `共 ${total} 部有播放记录的影片。`} action={favorite ? <FavoriteRoundedIcon color="error"/> : <HistoryRoundedIcon color="primary"/>}/>
    {error && <Alert severity="error">{error}</Alert>}{items === undefined && !error ? <Box sx={{ minHeight: 320, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : items?.length ? <MediaCardGrid>{items.map((item) => <MediaCard key={item.dataId} item={item} onPlay={play} onOpen={() => navigate(`/movies/${item.dataId}`)}/>)}</MediaCardGrid> : <EmptyState title={favorite ? '暂无收藏' : '暂无播放历史'} description="数据存在时会通过 Bridge 显示在这里。"/>}
    {total > pageSize && <Stack sx={{ pt: 3, alignItems: 'center' }}><Pagination count={Math.ceil(total / pageSize)} page={page} onChange={(_, value) => setPage(value)} color="primary"/></Stack>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3000} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
