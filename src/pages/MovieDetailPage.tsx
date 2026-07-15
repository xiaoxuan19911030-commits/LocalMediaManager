import AccessTimeRoundedIcon from '@mui/icons-material/AccessTimeRounded'
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded'
import CalendarMonthRoundedIcon from '@mui/icons-material/CalendarMonthRounded'
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded'
import { Alert, Box, Button, Card, Chip, CircularProgress, Divider, IconButton, Paper, Rating, Snackbar, Stack, Tooltip, Typography } from '@mui/material'
import { alpha } from '@mui/material/styles'
import type { ReactNode } from 'react'
import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { SurfaceSection } from '@/components/ProductComponents'
import { bridge } from '@/services/bridge'
import type { MovieDetail, NamedItem } from '@/types/media'

const formatDuration = (seconds: number) => {
  if (!seconds) return '时长未知'
  const hours = Math.floor(seconds / 3600); const minutes = Math.round((seconds % 3600) / 60)
  return hours ? `${hours} 小时 ${minutes} 分` : `${minutes} 分钟`
}
const formatSize = (bytes: number) => bytes ? `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB` : '大小未知'
const date = (value?: string) => value?.slice(0, 10) || '未知'

function Fact({ icon, label }: { icon: ReactNode; label: string }) {
  return <Box sx={{ display: 'flex', alignItems: 'center', gap: .75, color: 'text.secondary' }}><Box sx={{ display: 'flex', color: 'primary.main' }}>{icon}</Box><Typography variant="body2">{label}</Typography></Box>
}

function Relation({ label, items }: { label: string; items: NamedItem[] }) {
  const readable = items.filter((item) => item.name && !item.name.includes('\uFFFD'))
  return <Box><Typography variant="overline" color="text.secondary" sx={{ fontWeight: 750 }}>{label}</Typography>
    <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', mt: .5 }}>
      {readable.length ? readable.map((item) => <Chip key={item.id} label={item.name} size="small" variant="outlined"/>) : <Typography variant="body2" color="text.disabled">暂无</Typography>}
    </Stack></Box>
}

export default function MovieDetailPage() {
  const { id } = useParams(); const navigate = useNavigate()
  const [movie, setMovie] = useState<MovieDetail>(); const [error, setError] = useState(''); const [notice, setNotice] = useState('')
  useEffect(() => { const movieId = Number(id); if (!Number.isFinite(movieId)) { setError('无效影片编号'); return }
    setMovie(undefined); setError(''); bridge.movie(movieId).then(setMovie).catch((reason: Error) => setError(reason.message)) }, [id])
  const play = () => movie && bridge.play(movie.id).then(() => setNotice(`正在打开：${movie.code || movie.title}`)).catch((reason: Error) => setNotice(reason.message))

  return <Box sx={{ '@keyframes detailIn': { from: { opacity: 0, transform: 'translateY(10px)' }, to: { opacity: 1, transform: 'translateY(0)' } } }}>
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, mb: 2 }}>
      <Tooltip title="返回"><IconButton onClick={() => navigate(-1)} sx={{ border: 1, borderColor: 'divider' }}><ArrowBackRoundedIcon/></IconButton></Tooltip>
      <Box><Typography variant="h5" sx={{ fontWeight: 850 }}>影片信息</Typography><Typography variant="body2" color="text.secondary">媒体、元数据与文件状态</Typography></Box>
    </Box>
    {error && <Alert severity="error">{error}</Alert>}
    {!movie && !error ? <Box sx={{ minHeight: 420, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : movie && <Stack spacing={2.25} sx={{ animation: 'detailIn .32s ease both', '@media (prefers-reduced-motion: reduce)': { animation: 'none' } }}>
      <Paper variant="outlined" sx={{ position: 'relative', overflow: 'hidden', borderRadius: 3.5, p: { xs: 2, md: 3 } }}>
        <Box sx={{ position: 'absolute', inset: 0, pointerEvents: 'none', background: (theme) => `radial-gradient(circle at 82% 10%, ${alpha(theme.palette.primary.main, .18)}, transparent 42%), linear-gradient(135deg, ${alpha(theme.palette.background.paper, .7)}, ${theme.palette.background.paper})` }}/>
        <Box sx={{ position: 'relative', display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '210px minmax(0,1fr)', lg: '250px minmax(0,1fr)' }, gap: { xs: 2, md: 3 } }}>
          <Card sx={{ overflow: 'hidden', width: '100%', maxWidth: { xs: 260, sm: 'none' }, mx: { xs: 'auto', sm: 0 }, alignSelf: 'start', boxShadow: (theme) => `0 18px 42px ${alpha(theme.palette.common.black, theme.palette.mode === 'dark' ? .36 : .18)}` }}>
            <Box sx={{ aspectRatio: '2/3', bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}>
              {movie.coverUrl ? <Box component="img" src={movie.coverUrl} alt={movie.code || movie.title} sx={{ width: '100%', height: '100%', objectFit: 'cover' }}/> : <Typography color="text.disabled">暂无海报</Typography>}
            </Box>
          </Card>
          <Box sx={{ minWidth: 0, display: 'flex', flexDirection: 'column', py: { sm: 1 } }}>
            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mb: 1.25 }}>
              {movie.favorite && <Chip color="error" icon={<FavoriteRoundedIcon/>} label="已收藏"/>}
              {movie.scraped && <Chip color="success" icon={<CheckCircleRoundedIcon/>} label="元数据完整" variant="outlined"/>}
              {movie.tags.slice(0, 3).map((item) => <Chip key={item.id} label={item.name} variant="outlined"/>)}
            </Stack>
            <Typography variant="h3" sx={{ fontWeight: 900, lineHeight: 1.06, letterSpacing: '-.025em', overflowWrap: 'anywhere' }}>{movie.code || movie.title || `影片 ${movie.id}`}</Typography>
            {movie.title && movie.title !== movie.code && <Typography variant="h6" color="text.secondary" sx={{ mt: 1, maxWidth: 900, lineHeight: 1.45 }}>{movie.title}</Typography>}
            {movie.originalTitle && movie.originalTitle !== movie.title && <Typography variant="body2" color="text.disabled" sx={{ mt: .5 }}>{movie.originalTitle}</Typography>}
            <Stack direction="row" spacing={2.25} useFlexGap sx={{ flexWrap: 'wrap', mt: 2.25 }}>
              <Fact icon={<CalendarMonthRoundedIcon fontSize="small"/>} label={`发行 ${date(movie.releaseDate)}`}/>
              <Fact icon={<AccessTimeRoundedIcon fontSize="small"/>} label={formatDuration(movie.durationSeconds)}/>
              <Fact icon={<VisibilityRoundedIcon fontSize="small"/>} label={`播放 ${movie.playCount} 次`}/>
            </Stack>
            <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, mt: 2 }}><Rating value={movie.userRating} readOnly precision={.5}/><Typography variant="body2" color="text.secondary">个人评分 {movie.userRating ? movie.userRating.toFixed(1) : '未评分'}</Typography></Box>
            <Typography color="text.secondary" sx={{ mt: 2.25, lineHeight: 1.8, whiteSpace: 'pre-wrap', maxWidth: 960, display: '-webkit-box', WebkitLineClamp: 5, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>{movie.description || '暂无影片简介。'}</Typography>
            <Stack direction="row" spacing={1.25} sx={{ mt: 'auto', pt: 3 }}><Button size="large" variant="contained" startIcon={<PlayArrowRoundedIcon/>} onClick={play} sx={{ minWidth: 150 }}>播放影片</Button><Button size="large" variant="outlined" onClick={() => navigate('/media')}>返回影片墙</Button></Stack>
          </Box>
        </Box>
      </Paper>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0,1.25fr) minmax(320px,.75fr)' }, gap: 2.25, alignItems: 'start' }}>
        <Stack spacing={2.25}>
          <SurfaceSection title="影片信息" description="整理后的媒体关联与元数据">
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2,minmax(0,1fr))' }, gap: 2.25 }}>
              <Relation label="演员" items={movie.actors}/><Relation label="类型" items={movie.genres}/><Relation label="制作商" items={movie.studios}/><Relation label="系列" items={movie.series}/><Relation label="标签" items={movie.tags}/>
              <Box><Typography variant="overline" color="text.secondary" sx={{ fontWeight: 750 }}>导入日期</Typography><Typography sx={{ mt: .5 }}>{date(movie.importedAt)}</Typography></Box>
            </Box>
            {movie.description && <><Divider sx={{ my: 2.25 }}/><Typography variant="subtitle2" sx={{ fontWeight: 800, mb: .75 }}>内容简介</Typography><Typography color="text.secondary" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.85 }}>{movie.description}</Typography></>}
          </SurfaceSection>
        </Stack>
        <SurfaceSection title="媒体文件" description={`${movie.mediaFiles.length} 个关联文件`}>
          <Stack spacing={1}>{movie.mediaFiles.length ? movie.mediaFiles.map((file) => <Box key={file.id} sx={{ p: 1.5, borderRadius: 2, bgcolor: 'action.hover', display: 'flex', gap: 1.25, alignItems: 'center', border: 1, borderColor: 'divider' }}>
            <FolderRoundedIcon color={file.existsState === 'Missing' ? 'error' : 'primary'}/><Box sx={{ minWidth: 0, flex: 1 }}><Typography noWrap title={file.path} sx={{ fontWeight: 700 }}>{file.fileName}</Typography><Typography variant="caption" color="text.secondary">{file.extension || '文件'} · {formatSize(file.fileSize)} · {file.existsState === 'Missing' ? '文件缺失' : '文件可用'}</Typography></Box>{file.primary && <Chip size="small" label="主文件" color="primary"/>}
          </Box>) : <Typography variant="body2" color="text.secondary">暂无关联媒体文件。</Typography>}</Stack>
          <Divider sx={{ my: 2 }}/><Stack spacing={1}><Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}><Typography variant="body2" color="text.secondary">刮削状态</Typography><Typography variant="body2">{movie.scrapeStatus || '未知'}</Typography></Box><Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}><Typography variant="body2" color="text.secondary">更新时间</Typography><Typography variant="body2">{date(movie.updatedAt)}</Typography></Box></Stack>
        </SurfaceSection>
      </Box>
    </Stack>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
