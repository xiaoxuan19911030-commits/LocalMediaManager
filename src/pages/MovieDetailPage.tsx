import AccessTimeRoundedIcon from '@mui/icons-material/AccessTimeRounded'
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded'
import CalendarMonthRoundedIcon from '@mui/icons-material/CalendarMonthRounded'
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import FavoriteBorderRoundedIcon from '@mui/icons-material/FavoriteBorderRounded'
import NavigateBeforeRoundedIcon from '@mui/icons-material/NavigateBeforeRounded'
import NavigateNextRoundedIcon from '@mui/icons-material/NavigateNextRounded'
import SellRoundedIcon from '@mui/icons-material/SellRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import VisibilityRoundedIcon from '@mui/icons-material/VisibilityRounded'
import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import { Alert, Autocomplete, Box, Button, Card, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Divider, IconButton, Paper, Rating, Snackbar, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { alpha } from '@mui/material/styles'
import type { ReactNode } from 'react'
import { useEffect, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router'
import { SurfaceSection } from '@/components/ProductComponents'
import { bridge } from '@/services/bridge'
import type { MovieDeletePreview, MovieDetail, NamedItem } from '@/types/media'

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
  const { id } = useParams(); const navigate = useNavigate(); const location = useLocation()
  const [movie, setMovie] = useState<MovieDetail>(); const [error, setError] = useState(''); const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false); const [tagDialog, setTagDialog] = useState(false); const [actorDialog, setActorDialog] = useState(false)
  const [tagOptions, setTagOptions] = useState<NamedItem[]>([]); const [selectedTags, setSelectedTags] = useState<NamedItem[]>([])
  const [actorOptions, setActorOptions] = useState<NamedItem[]>([]); const [selectedActors, setSelectedActors] = useState<NamedItem[]>([]); const [actorSearch, setActorSearch] = useState('')
  const [neighbors, setNeighbors] = useState<{ previousId?: number; nextId?: number }>({})
  const [deletePreview, setDeletePreview] = useState<MovieDeletePreview>()
  const context = (location.state as { context?: { search?: string; sort?: string } } | null)?.context
  const loadMovie = (movieId: number) => bridge.movie(movieId).then(setMovie)
  useEffect(() => { const movieId = Number(id); if (!Number.isFinite(movieId)) { setError('无效影片编号'); return }
    setMovie(undefined); setError(''); loadMovie(movieId).catch((reason: Error) => setError(reason.message)); bridge.neighbors(movieId, context?.search, context?.sort).then(setNeighbors).catch(() => setNeighbors({})) }, [id])
  useEffect(() => { if (!actorDialog) return; const timer = window.setTimeout(() => bridge.entities('actors', actorSearch, 'name', 48, 0).then((result) => setActorOptions([...selectedActors, ...result.items.filter((item) => !selectedActors.some((selected) => selected.id === item.id))])).catch((reason: Error) => setNotice(reason.message)), 200); return () => window.clearTimeout(timer) }, [actorDialog, actorSearch, selectedActors])
  const play = () => movie && bridge.play(movie.id).then(() => setNotice(`正在打开：${movie.code || movie.title}`)).catch((reason: Error) => setNotice(reason.message))
  const mutate = (action: Promise<unknown>) => { if (!movie) return; setBusy(true); action.then(() => loadMovie(movie.id)).then(() => setNotice('已保存')).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false)) }
  const openTags = () => { if (!movie) return; setSelectedTags(movie.tags); setTagDialog(true); bridge.entities('tags', '', 'name', 96, 0).then((result) => setTagOptions(result.items)).catch((reason: Error) => setNotice(reason.message)) }
  const saveTags = () => { if (!movie) return; const current = new Set(movie.tags.map((item) => item.id)); const selected = new Set(selectedTags.map((item) => item.id)); mutate(bridge.updateMovieTags(movie.id, [...selected].filter((tag) => !current.has(tag)), [...current].filter((tag) => !selected.has(tag)))); setTagDialog(false) }
  const openActors = () => { if (!movie) return; setSelectedActors(movie.actors); setActorOptions(movie.actors); setActorDialog(true) }
  const saveActors = () => { if (!movie) return; mutate(bridge.setMovieActors(movie.id, selectedActors.map((item) => item.id))); setActorDialog(false) }
  const go = (movieId?: number) => movieId && navigate(`/movies/${movieId}`, { state: { context }, replace: true })
  const previewDelete = () => movie && bridge.previewDeleteMovie(movie.id).then(setDeletePreview).catch((reason: Error) => setNotice(reason.message))
  const confirmDelete = () => deletePreview && bridge.deleteMovie(deletePreview.movieId, deletePreview.confirmationToken).then((result) => { setDeletePreview(undefined); navigate('/media', { replace: true }); window.setTimeout(() => setNotice(result.message), 0) }).catch((reason: Error) => setNotice(reason.message))
  const syncMetadata = () => movie && bridge.syncMovie(movie.id).then(result => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message))

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
            <Stack direction="row" spacing={1.25} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mt: 2 }}><Rating value={movie.userRatingSet ? movie.userRating : null} precision={.5} disabled={busy} onChange={(_, value) => value !== null && mutate(bridge.setUserState(movie.id, { rating: value }))}/><Typography variant="body2" color="text.secondary">个人评分 {movie.userRatingSet ? movie.userRating.toFixed(1) : '未评分'}</Typography>{movie.userRatingSet && <Button size="small" color="inherit" onClick={() => mutate(bridge.setUserState(movie.id, { clearRating: true }))}>清除评分</Button>}</Stack>
            <Typography color="text.secondary" sx={{ mt: 2.25, lineHeight: 1.8, whiteSpace: 'pre-wrap', maxWidth: 960, display: '-webkit-box', WebkitLineClamp: 5, WebkitBoxOrient: 'vertical', overflow: 'hidden' }}>{movie.description || '暂无影片简介。'}</Typography>
            <Stack direction="row" spacing={1.25} useFlexGap sx={{ mt: 'auto', pt: 3, flexWrap: 'wrap' }}><Button size="large" variant="contained" startIcon={<PlayArrowRoundedIcon/>} onClick={play} sx={{ minWidth: 150 }}>播放影片</Button><Button size="large" variant="outlined" startIcon={<SyncRoundedIcon/>} onClick={syncMetadata}>同步元数据</Button><Button size="large" variant="outlined" color={movie.favorite ? 'error' : 'primary'} startIcon={movie.favorite ? <FavoriteRoundedIcon/> : <FavoriteBorderRoundedIcon/>} disabled={busy} onClick={() => mutate(bridge.setUserState(movie.id, { favorite: !movie.favorite }))}>{movie.favorite ? '取消收藏' : '收藏'}</Button><Button size="large" variant="outlined" startIcon={<SellRoundedIcon/>} onClick={openTags}>编辑标签</Button><Button size="large" variant="outlined" onClick={openActors}>编辑演员</Button><Button size="large" variant="text" color="error" startIcon={<DeleteOutlineRoundedIcon/>} onClick={previewDelete}>从资料库移除</Button></Stack>
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
      <Stack direction="row" spacing={1.25} sx={{ justifyContent: 'space-between' }}><Button startIcon={<NavigateBeforeRoundedIcon/>} disabled={!neighbors.previousId} onClick={() => go(neighbors.previousId)}>上一部</Button><Button endIcon={<NavigateNextRoundedIcon/>} disabled={!neighbors.nextId} onClick={() => go(neighbors.nextId)}>下一部</Button></Stack>
    </Stack>}
    <Dialog open={tagDialog} onClose={() => setTagDialog(false)} fullWidth maxWidth="sm"><DialogTitle>编辑影片标签</DialogTitle><DialogContent><Autocomplete multiple options={tagOptions} value={selectedTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setSelectedTags(value)} renderInput={(params) => <TextField {...params} autoFocus label="标签" margin="normal" helperText="用户手工标签不会被同步覆盖"/>}/></DialogContent><DialogActions><Button onClick={() => setTagDialog(false)}>取消</Button><Button variant="contained" onClick={saveTags}>保存</Button></DialogActions></Dialog>
    <Dialog open={actorDialog} onClose={() => setActorDialog(false)} fullWidth maxWidth="sm"><DialogTitle>编辑演员关系</DialogTitle><DialogContent><Autocomplete multiple filterOptions={(options) => options} options={actorOptions} value={selectedActors} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onInputChange={(_, value) => setActorSearch(value)} onChange={(_, value) => setSelectedActors(value)} renderInput={(params) => <TextField {...params} autoFocus label="搜索并选择演员" margin="normal"/>}/></DialogContent><DialogActions><Button onClick={() => setActorDialog(false)}>取消</Button><Button variant="contained" onClick={saveActors}>保存</Button></DialogActions></Dialog>
    <Dialog open={Boolean(deletePreview)} onClose={() => setDeletePreview(undefined)} maxWidth="sm" fullWidth><DialogTitle>从资料库移除影片？</DialogTitle><DialogContent><DialogContentText>将移除“{deletePreview?.code}”的数据库记录，但不会删除媒体文件。{deletePreview?.ratingWillBeRemembered ? '当前评分会按文件名记忆，重新导入同名文件时可恢复。' : '当前没有需要记忆的评分。'}</DialogContentText><Alert severity="warning" sx={{ mt: 2 }}>{deletePreview?.warnings.join(' ')}</Alert></DialogContent><DialogActions><Button onClick={() => setDeletePreview(undefined)}>取消</Button><Button variant="contained" color="error" onClick={confirmDelete}>确认移除记录</Button></DialogActions></Dialog>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
