import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Divider, IconButton, Rating, Snackbar, Stack, Tooltip, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { bridge } from '@/services/bridge'
import type { MovieDetail, NamedItem } from '@/types/media'

const formatDuration = (seconds: number) => seconds ? `${Math.floor(seconds / 3600)} 小时 ${Math.round((seconds % 3600) / 60)} 分` : '未知'
const formatSize = (bytes: number) => bytes ? `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB` : '未知'
const names = (items: NamedItem[]) => items.length ? items.map((item) => item.name).join('、') : '暂无'

export default function MovieDetailPage() {
  const { id } = useParams(); const navigate = useNavigate()
  const [movie, setMovie] = useState<MovieDetail>(); const [error, setError] = useState(''); const [notice, setNotice] = useState('')
  useEffect(() => { const movieId = Number(id); if (!Number.isFinite(movieId)) { setError('无效影片编号'); return }
    bridge.movie(movieId).then(setMovie).catch((reason: Error) => setError(reason.message)) }, [id])
  const play = () => movie && bridge.play(movie.id).then(() => setNotice(`正在打开：${movie.code || movie.title}`)).catch((reason: Error) => setNotice(reason.message))
  return <Box>
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5, mb: 2.5 }}>
      <Tooltip title="返回"><IconButton onClick={() => navigate(-1)} sx={{ border: 1, borderColor: 'divider' }}><ArrowBackRoundedIcon/></IconButton></Tooltip>
      <Box><Typography variant="h4" sx={{ fontWeight: 850 }}>影片详情</Typography><Typography color="text.secondary">完整元数据、文件和关联信息</Typography></Box>
    </Box>
    {error && <Alert severity="error">{error}</Alert>}
    {!movie && !error ? <Box sx={{ minHeight: 360, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : movie && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '300px minmax(0,1fr)' }, gap: 2.5 }}>
      <Card sx={{ overflow: 'hidden', alignSelf: 'start' }}>
        <Box sx={{ aspectRatio: '2/3', bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}>
          {movie.coverUrl ? <Box component="img" src={movie.coverUrl} alt={movie.code || movie.title} sx={{ width: '100%', height: '100%', objectFit: 'cover' }}/> : <Typography color="text.disabled">暂无海报</Typography>}
        </Box>
        <CardContent><Button fullWidth size="large" variant="contained" startIcon={<PlayArrowRoundedIcon/>} onClick={play}>播放</Button></CardContent>
      </Card>
      <Stack spacing={2}>
        <Card><CardContent>
          <Box sx={{ display: 'flex', alignItems: 'start', justifyContent: 'space-between', gap: 2 }}>
            <Box sx={{ minWidth: 0 }}><Typography variant="h4" sx={{ fontWeight: 850 }}>{movie.code || movie.title || `影片 ${movie.id}`}</Typography>
              {movie.title && movie.title !== movie.code && <Typography color="text.secondary" sx={{ mt: .5 }}>{movie.title}</Typography>}</Box>
            {movie.favorite && <Chip color="error" icon={<FavoriteRoundedIcon/>} label="已收藏"/>}
          </Box>
          <Box sx={{ display: 'flex', gap: 2, alignItems: 'center', mt: 2, flexWrap: 'wrap' }}><Rating value={movie.userRating} readOnly/><Typography variant="body2" color="text.secondary">发行 {movie.releaseDate?.slice(0,10) || '未知'} · {formatDuration(movie.durationSeconds)} · 播放 {movie.playCount} 次</Typography></Box>
          <Divider sx={{ my: 2 }}/><Typography sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.8 }}>{movie.description || '暂无影片简介。'}</Typography>
        </CardContent></Card>
        <Card><CardContent><Typography variant="h6" sx={{ fontWeight: 800, mb: 1.5 }}>关联信息</Typography>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'repeat(2,minmax(0,1fr))' }, gap: 1.5 }}>
            {[['演员',names(movie.actors)],['标签',names(movie.tags)],['类型',names(movie.genres)],['制作商',names(movie.studios)],['系列',names(movie.series)],['导入日期',movie.importedAt?.slice(0,10)||'未知']].map(([label,value]) => <Box key={label}><Typography variant="caption" color="text.secondary">{label}</Typography><Typography>{value}</Typography></Box>)}
          </Box>
        </CardContent></Card>
        <Card><CardContent><Typography variant="h6" sx={{ fontWeight: 800, mb: 1.5 }}>媒体文件</Typography>
          <Stack spacing={1}>{movie.mediaFiles.map((file) => <Box key={file.id} sx={{ p: 1.5, borderRadius: 2, bgcolor: 'action.hover', display: 'flex', gap: 1.5, alignItems: 'center' }}>
            <FolderRoundedIcon color={file.existsState === 'Missing' ? 'error' : 'primary'}/><Box sx={{ minWidth: 0, flex: 1 }}><Typography noWrap title={file.path}>{file.fileName}</Typography><Typography variant="caption" color="text.secondary">{file.sourceType} · {formatSize(file.fileSize)} · {file.existsState}</Typography></Box>{file.primary && <Chip size="small" label="主文件"/>}
          </Box>)}</Stack>
        </CardContent></Card>
      </Stack>
    </Box>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
