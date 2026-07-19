import AccessTimeRoundedIcon from '@mui/icons-material/AccessTimeRounded'
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded'
import CalendarMonthRoundedIcon from '@mui/icons-material/CalendarMonthRounded'
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import ContentCutRoundedIcon from '@mui/icons-material/ContentCutRounded'
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
import LockRoundedIcon from '@mui/icons-material/LockRounded'
import LockOpenRoundedIcon from '@mui/icons-material/LockOpenRounded'
import MoreVertRoundedIcon from '@mui/icons-material/MoreVertRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import ZoomInRoundedIcon from '@mui/icons-material/ZoomInRounded'
import ZoomOutRoundedIcon from '@mui/icons-material/ZoomOutRounded'
import { Alert, Autocomplete, Box, Button, Card, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Divider, IconButton, ListItemIcon, Menu, MenuItem, Paper, Rating, Snackbar, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { alpha } from '@mui/material/styles'
import type { ReactNode } from 'react'
import { useEffect, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router'
import { SurfaceSection } from '@/components/ProductComponents'
import { SafeDeleteDialog } from '@/components/SafeDeleteDialog'
import { SmartImage, clearImageMemoryCache } from '@/components/SmartImage'
import { bridge } from '@/services/bridge'
import type { ImageAsset, ImageCenterStatus, ImageDeletePreview, MovieDetail, NamedItem, NfoPreview, OrganizerPreview, SafeDeletePreview, SafeDeletePreviewCommand } from '@/types/media'
import { copyMovieInformation } from '@/utils/movieCopyInfo'

const formatDuration = (seconds: number) => {
  if (!seconds) return '时长未知'
  const hours = Math.floor(seconds / 3600); const minutes = Math.round((seconds % 3600) / 60)
  return hours ? `${hours} 小时 ${minutes} 分` : `${minutes} 分钟`
}
const formatSize = (bytes: number) => bytes ? `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB` : '大小未知'
const formatImageSize = (bytes: number) => bytes ? bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.ceil(bytes / 1024)} KB` : '大小待校验'
const date = (value?: string) => value?.slice(0, 10) || '未知'
const imageTypes = ['Poster', 'GeneratedCard', 'Thumbnail', 'Fanart', 'Preview', 'Screenshot', 'GIF']
const imageLabel = (type: string) => ({ Poster: '封面', GeneratedCard: '卡图裁切', Thumbnail: '缩略图', Fanart: '背景图', Preview: '预览图', Screenshot: '截图', GIF: 'GIF' }[type] ?? type)
const imageStatusLabel = (status?: string) => status === 'Normal' ? '图片正常' : status === 'Failed' ? '读取失败' : '暂无图片'
const canGenerateImage = (type: string) => ['Poster', 'Preview', 'Screenshot', 'GIF'].includes(type)

function Fact({ icon, label }: { icon: ReactNode; label: string }) {
  return <Box sx={{ display: 'flex', alignItems: 'center', gap: .75, color: 'text.secondary' }}><Box sx={{ display: 'flex', color: 'primary.main' }}>{icon}</Box><Typography variant="body2">{label}</Typography></Box>
}

function Relation({ label, items }: { label: string; items: NamedItem[] }) {
  const readable = (items ?? []).filter((item) => item.name && !item.name.includes('\uFFFD'))
  return <Box><Typography variant="overline" color="text.secondary" sx={{ fontWeight: 750 }}>{label}</Typography>
    <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', mt: .5 }}>
      {readable.length ? readable.map((item) => <Chip key={item.id} label={item.name} size="small" variant="outlined"/>) : <Typography variant="body2" color="text.disabled">暂无</Typography>}
    </Stack></Box>
}

function MetadataCheckRow({ label, complete }: { label: string; complete: boolean }) {
  return <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1, p: 1, borderRadius: 1.5, bgcolor: 'action.hover' }}>
    <Typography variant="body2" sx={{ fontWeight: 700 }}>{label}</Typography>
    <Chip size="small" color={complete ? 'success' : 'warning'} label={complete ? '正常' : '缺失'}/>
  </Box>
}

export default function MovieDetailPage() {
  const { id } = useParams(); const navigate = useNavigate(); const location = useLocation()
  const [movie, setMovie] = useState<MovieDetail>(); const [error, setError] = useState(''); const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false); const [tagDialog, setTagDialog] = useState(false); const [actorDialog, setActorDialog] = useState(false)
  const [tagOptions, setTagOptions] = useState<NamedItem[]>([]); const [selectedTags, setSelectedTags] = useState<NamedItem[]>([])
  const [tagSearch, setTagSearch] = useState('')
  const [actorOptions, setActorOptions] = useState<NamedItem[]>([]); const [selectedActors, setSelectedActors] = useState<NamedItem[]>([]); const [actorSearch, setActorSearch] = useState('')
  const [imageAssets, setImageAssets] = useState<ImageAsset[]>([]); const [posterFailed, setPosterFailed] = useState(false)
  const [imageStatus, setImageStatus] = useState<ImageCenterStatus>(); const [viewer, setViewer] = useState<ImageAsset>(); const [zoom, setZoom] = useState(1)
  const [replaceTarget, setReplaceTarget] = useState<ImageAsset>(); const [replacePath, setReplacePath] = useState('')
  const [cropTarget, setCropTarget] = useState<ImageAsset>(); const [cropAnchor, setCropAnchor] = useState<'left' | 'center' | 'right'>('center'); const [cropAspect, setCropAspect] = useState<'landscape' | 'portrait'>('landscape')
  const [imageDeletePreview, setImageDeletePreview] = useState<ImageDeletePreview>()
  const [neighbors, setNeighbors] = useState<{ previousId?: number; nextId?: number }>({})
  const [deletePreview, setDeletePreview] = useState<SafeDeletePreview>()
  const [deleteCommand, setDeleteCommand] = useState<SafeDeletePreviewCommand>()
  const [nfoPreview, setNfoPreview] = useState<NfoPreview>(); const [nfoMode, setNfoMode] = useState<'import' | 'export'>('export')
  const [organizerOpen, setOrganizerOpen] = useState(false); const [organizerPreview, setOrganizerPreview] = useState<OrganizerPreview>(); const [organizerTemplate, setOrganizerTemplate] = useState('{Code}'); const [organizerDestination, setOrganizerDestination] = useState('')
  const [moreAnchor, setMoreAnchor] = useState<HTMLElement | null>(null)
  const context = (location.state as { context?: { search?: string; sort?: string } } | null)?.context
  const loadMovie = (movieId: number) => Promise.all([bridge.movie(movieId).then(setMovie), bridge.movieImages(movieId).then(setImageAssets), bridge.movieImageStatus(movieId).then(setImageStatus)])
  useEffect(() => { const movieId = Number(id); if (!Number.isFinite(movieId)) { setError('无效影片编号'); return }
    setMovie(undefined); setImageAssets([]); setPosterFailed(false); setError(''); loadMovie(movieId).catch((reason: Error) => setError(reason.message)); bridge.neighbors(movieId, context?.search, context?.sort).then(setNeighbors).catch(() => setNeighbors({})) }, [id])
  useEffect(() => { if (!actorDialog) return; const timer = window.setTimeout(() => bridge.entities('actors', actorSearch, 'name', 48, 0).then((result) => setActorOptions([...selectedActors, ...result.items.filter((item) => !selectedActors.some((selected) => selected.id === item.id))])).catch((reason: Error) => setNotice(reason.message)), 200); return () => window.clearTimeout(timer) }, [actorDialog, actorSearch, selectedActors])
  const play = () => movie && bridge.play(movie.id).then(() => setNotice(`正在打开：${movie.code || movie.title}`)).catch((reason: Error) => setNotice(reason.message))
  const mutate = (action: Promise<unknown>) => { if (!movie) return; setBusy(true); action.then(() => loadMovie(movie.id)).then(() => setNotice('已保存')).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false)) }
  const copyInfo = () => {
    if (!movie) return
    setMoreAnchor(null)
    copyMovieInformation(movie, (value) => navigator.clipboard.writeText(value)).then(() => setNotice('已复制影片信息')).catch(() => setNotice('复制失败'))
  }
  const openTags = () => { if (!movie) return; setSelectedTags(movie.tags ?? []); setTagSearch(''); setTagDialog(true); bridge.entities('tags', '', 'name', 96, 0).then((result) => setTagOptions(result.items)).catch((reason: Error) => setNotice(reason.message)) }
  const saveTags = () => { if (!movie) return; const current = new Set((movie.tags ?? []).map((item) => item.id)); const selected = new Set(selectedTags.map((item) => item.id)); mutate(bridge.updateMovieTags(movie.id, [...selected].filter((tag) => !current.has(tag)), [...current].filter((tag) => !selected.has(tag)))); setTagDialog(false) }
  const openActors = () => { if (!movie) return; setSelectedActors(movie.actors ?? []); setActorOptions(movie.actors ?? []); setActorDialog(true) }
  const saveActors = () => { if (!movie) return; mutate(bridge.setMovieActors(movie.id, selectedActors.map((item) => item.id))); setActorDialog(false) }
  const go = (movieId?: number) => movieId && navigate(`/movies/${movieId}`, { state: { context }, replace: true })
  const previewDelete = () => {
    if (!movie) return
    const command = { movieIds: [movie.id], mode: 'metadata', deleteDatabaseInfo: true } satisfies SafeDeletePreviewCommand
    setDeleteCommand(command)
    bridge.previewSafeDelete(command).then(setDeletePreview).catch((reason: Error) => setNotice(reason.message))
  }
  const closeDeleteDialog = () => { setDeletePreview(undefined); setDeleteCommand(undefined) }
  const handleDeleteLaunched = (result: { message: string }) => {
    closeDeleteDialog()
    navigate('/media', { replace: true })
    window.setTimeout(() => setNotice(`${result.message} 可在任务中心查看结果。`), 0)
  }
  const syncMetadata = () => movie && bridge.syncMovie(movie.id).then(result => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message))
  const rescrapeMetadata = () => movie && bridge.rescrapeMovie(movie.id).then(result => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message))
  const refreshImages = () => { clearImageMemoryCache(); if (movie) loadMovie(movie.id).then(() => setNotice('图片状态已刷新')).catch((reason: Error) => setNotice(reason.message)) }
  const rebuildCache = () => bridge.rebuildImageCache().then(result => setNotice(`${result.message}（${result.totalItems} 部影片）`)).catch((reason: Error) => setNotice(reason.message))
  const refreshStatus = () => movie && loadMovie(movie.id).then(() => setNotice('状态已刷新')).catch((reason: Error) => setNotice(reason.message))
  const openMovieFolder = () => {
    const path = movie?.mediaFiles[0]?.path
    if (!path) { setNotice('没有可打开的文件目录'); return }
    bridge.revealFile(path).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))
  }
  const openImageFolder = () => {
    const directory = imageAssets.find(asset => asset.directory)?.directory
    if (!directory) { openMovieFolder(); return }
    bridge.openDirectory(directory).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))
  }
  const setImageLock = (asset: ImageAsset) => asset.id > 0 && bridge.setImageLock(asset.id, !asset.locked).then(result => { setNotice(result.message); return movie ? loadMovie(movie.id) : undefined }).catch((reason: Error) => setNotice(reason.message))
  const openAssetDirectory = (asset: ImageAsset) => asset.id > 0 ? bridge.openImageDirectory(asset.id).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message)) : setNotice('暂无图片目录')
  const revealAsset = (asset: ImageAsset) => asset.id > 0 ? bridge.revealImage(asset.id).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message)) : setNotice('暂无图片文件')
  const generateImage = (type: string) => movie && bridge.generateMovieImage(movie.id, type).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message))
  const openReplace = (asset: ImageAsset) => { setReplaceTarget(asset); setReplacePath('') }
  const confirmReplace = () => {
    if (!movie || !replaceTarget) return
    setBusy(true)
    bridge.replaceMovieImage(movie.id, replaceTarget.type, replacePath).then((result) => { setNotice(result.message); setReplaceTarget(undefined); clearImageMemoryCache(); return loadMovie(movie.id) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false))
  }
  const previewDeleteImage = (asset: ImageAsset) => asset.id > 0 ? bridge.previewDeleteImage(asset.id).then(setImageDeletePreview).catch((reason: Error) => setNotice(reason.message)) : setNotice('暂无图片可删除')
  const openCrop = (asset: ImageAsset) => { setCropTarget(asset); setCropAnchor('center'); setCropAspect('landscape') }
  const confirmCrop = () => {
    if (!movie || !cropTarget) return
    setBusy(true)
    bridge.cropMovieCard(movie.id, { sourceImageId: cropTarget.id, aspectRatio: cropAspect === 'landscape' ? 16 / 9 : 2 / 3, anchor: cropAnchor }).then((result) => {
      setNotice(result.message); setCropTarget(undefined); clearImageMemoryCache(); return loadMovie(movie.id)
    }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false))
  }
  const confirmDeleteImage = () => {
    if (!movie || !imageDeletePreview) return
    setBusy(true)
    bridge.deleteImage(imageDeletePreview.imageId, imageDeletePreview.confirmationToken).then((result) => { setNotice(result.message); setImageDeletePreview(undefined); clearImageMemoryCache(); return loadMovie(movie.id) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false))
  }
  const previewNfo = (mode: 'import' | 'export') => { if (!movie) return; setBusy(true); setNfoMode(mode); (mode === 'import' ? bridge.previewNfoImport(movie.id) : bridge.previewNfoExport(movie.id)).then(setNfoPreview).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false)) }
  const confirmNfo = (separateWhenLocked = false) => { if (!movie || !nfoPreview) return; setBusy(true); const action = nfoMode === 'import' ? bridge.importNfo(movie.id, nfoPreview.confirmationToken) : bridge.exportNfo(movie.id, nfoPreview.confirmationToken, separateWhenLocked); action.then(result => { setNfoPreview(undefined); setNotice(result.message); return loadMovie(movie.id) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false)) }
  const dryRunOrganizer = () => { if (!movie) return; setBusy(true); bridge.organizerDryRun([movie.id], organizerTemplate, organizerDestination).then(setOrganizerPreview).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false)) }
  const executeOrganizer = () => { if (!organizerPreview) return; setBusy(true); bridge.executeOrganizer(organizerPreview.taskId, organizerPreview.confirmationToken).then(result => { setOrganizerOpen(false); setOrganizerPreview(undefined); setNotice(`${result.message} 可在任务中心查看进度。`) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false)) }
  const imageResources = imageTypes.map((type) => imageAssets.find((asset) => asset.type === type) ?? {
    id: 0, type, ownership: 'Missing', locked: false, derived: false, primary: false, validationStatus: 'Missing',
    width: 0, height: 0, fileSize: 0,
  } satisfies ImageAsset)
  const heroPosterUrl = imageAssets.find(asset => asset.type === 'Poster' && asset.primary && asset.url)?.url
    ?? imageAssets.find(asset => asset.type === 'Poster' && asset.url)?.url
    ?? movie?.coverUrl
  useEffect(() => { setPosterFailed(false) }, [heroPosterUrl])

  return <Box sx={{ '@keyframes detailIn': { from: { opacity: 0, transform: 'translateY(10px)' }, to: { opacity: 1, transform: 'translateY(0)' } } }}>
    <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, mb: 2 }}>
      <Tooltip title="返回"><IconButton onClick={() => navigate(-1)} sx={{ border: 1, borderColor: 'divider' }}><ArrowBackRoundedIcon/></IconButton></Tooltip>
      <Box><Typography variant="h5" sx={{ fontWeight: 850 }}>影片信息</Typography><Typography variant="body2" color="text.secondary">媒体、元数据与文件状态</Typography></Box>
      <Box sx={{ ml: 'auto' }}><Tooltip title="更多"><span><IconButton disabled={!movie} onClick={(event) => setMoreAnchor(event.currentTarget)} sx={{ border: 1, borderColor: 'divider' }}><MoreVertRoundedIcon/></IconButton></span></Tooltip></Box>
    </Box>
    <Menu anchorEl={moreAnchor} open={Boolean(moreAnchor)} onClose={() => setMoreAnchor(null)}>
      <MenuItem disabled={!movie} onClick={copyInfo}><ListItemIcon><ContentCopyRoundedIcon fontSize="small"/></ListItemIcon>复制影片信息</MenuItem>
    </Menu>
    {error && <Alert severity="error">{error}</Alert>}
    {!movie && !error ? <Box sx={{ minHeight: 420, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : movie && <Stack spacing={2.25} sx={{ animation: 'detailIn .32s ease both', '@media (prefers-reduced-motion: reduce)': { animation: 'none' } }}>
      <Paper variant="outlined" sx={{ position: 'relative', overflow: 'hidden', borderRadius: 3.5, p: { xs: 2, md: 3 } }}>
        <Box sx={{ position: 'absolute', inset: 0, pointerEvents: 'none', background: (theme) => `radial-gradient(circle at 82% 10%, ${alpha(theme.palette.primary.main, .18)}, transparent 42%), linear-gradient(135deg, ${alpha(theme.palette.background.paper, .7)}, ${theme.palette.background.paper})` }}/>
        <Box sx={{ position: 'relative', display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(340px, 42%) minmax(0,1fr)', xl: 'minmax(440px, 520px) minmax(0,1fr)' }, gap: { xs: 2, md: 3 } }}>
          <Card sx={{ overflow: 'hidden', width: '100%', maxWidth: { xs: 420, md: 'none' }, mx: { xs: 'auto', md: 0 }, alignSelf: 'start', boxShadow: (theme) => `0 18px 42px ${alpha(theme.palette.common.black, theme.palette.mode === 'dark' ? .36 : .18)}` }}>
            <Box sx={{ aspectRatio: '2/3', bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}>
              {heroPosterUrl && !posterFailed ? <SmartImage src={heroPosterUrl} alt={movie.code || movie.title || ''} eager onError={() => setPosterFailed(true)}/> : <Typography color="text.disabled" sx={{ px: 2, textAlign: 'center' }}>{posterFailed ? '图片损坏或不可用' : '暂无海报'}</Typography>}
            </Box>
          </Card>
          <Box sx={{ minWidth: 0, display: 'flex', flexDirection: 'column', py: { sm: 1 } }}>
            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mb: 1.25 }}>
              {movie.favorite && <Chip color="error" icon={<FavoriteRoundedIcon/>} label="已收藏"/>}
              {movie.scraped && <Chip color="success" icon={<CheckCircleRoundedIcon/>} label="元数据完整" variant="outlined"/>}
              {(movie.tags ?? []).slice(0, 3).map((item) => <Chip key={item.id} label={item.name} variant="outlined"/>)}
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

      <SurfaceSection title="元数据状态" description={movie.metadataStatus?.missingItems?.length ? `缺少：${movie.metadataStatus.missingItems.join('、')}` : '当前影片元数据已完整'}>
        <Stack spacing={1.5}>
          <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
            <Chip color={movie.metadataStatus?.state === 'complete' ? 'success' : movie.metadataStatus?.state === 'unscraped' ? 'error' : 'warning'} label={`${movie.metadataStatus?.icon ?? '✕'} ${movie.metadataStatus?.label ?? '未刮削'}`}/>
            <Button size="small" startIcon={<SyncRoundedIcon/>} onClick={syncMetadata}>同步信息</Button>
            <Button size="small" startIcon={<SyncRoundedIcon/>} onClick={rescrapeMetadata}>重新刮削</Button>
            <Button size="small" startIcon={<FolderRoundedIcon/>} onClick={openMovieFolder}>打开影片目录</Button>
            <Button size="small" onClick={refreshStatus}>刷新状态</Button>
          </Stack>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2,minmax(0,1fr))', md: 'repeat(3,minmax(0,1fr))' }, gap: 1 }}>
            {(movie.metadataStatus?.checks ?? []).map(item => <MetadataCheckRow key={item.key} label={item.label} complete={item.complete}/>)}
          </Box>
        </Stack>
      </SurfaceSection>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0,1.25fr) minmax(320px,.75fr)' }, gap: 2.25, alignItems: 'start' }}>
        <Stack spacing={2.25}>
          <SurfaceSection title="影片信息" description="整理后的媒体关联与元数据">
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2,minmax(0,1fr))' }, gap: 2.25 }}>
              <Relation label="演员" items={movie.actors}/><Relation label="导演" items={movie.directors}/><Relation label="标签" items={movie.genres}/><Relation label="制作商" items={movie.studios}/><Relation label="系列" items={movie.series}/><Relation label="自定义标签" items={movie.tags}/>
              <Box><Typography variant="overline" color="text.secondary" sx={{ fontWeight: 750 }}>导入日期</Typography><Typography sx={{ mt: .5 }}>{date(movie.importedAt)}</Typography></Box>
            </Box>
            {movie.description && <><Divider sx={{ my: 2.25 }}/><Typography variant="subtitle2" sx={{ fontWeight: 800, mb: .75 }}>内容简介</Typography><Typography color="text.secondary" sx={{ whiteSpace: 'pre-wrap', lineHeight: 1.85 }}>{movie.description}</Typography></>}
          </SurfaceSection>
          <SurfaceSection title="图片资源" description="封面、卡图裁切、缩略图、背景图、预览图、截图和 GIF 使用同一套查看、生成、替换、删除与缓存刷新工作流">
            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mb: 1.5 }}>
              <Chip size="small" color={imageStatus?.missingImages || imageStatus?.failedImages ? 'warning' : 'success'} label={`正常 ${imageStatus?.normalImages ?? 0} / ${imageStatus?.totalImages ?? 0}`}/>
              {Boolean(imageStatus?.invalidCacheEntries) && <Chip size="small" color="warning" label={`缓存失效 ${imageStatus?.invalidCacheEntries}`}/>}
              <Button size="small" startIcon={<RefreshRoundedIcon/>} onClick={refreshImages}>刷新图片</Button>
              <Button size="small" onClick={rebuildCache}>重新生成缓存</Button>
              <Button size="small" startIcon={<FolderRoundedIcon/>} onClick={openImageFolder}>打开图片目录</Button>
            </Stack>
            <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(1,minmax(0,1fr))', sm: 'repeat(2,minmax(0,1fr))', xl: 'repeat(3,minmax(0,1fr))' }, gap: 1.5 }}>
              {imageResources.map(asset => { const status = imageStatus?.assets.find(item => item.id === asset.id || item.type === asset.type); const available = asset.id > 0 && Boolean(asset.url); return <Card key={`${asset.type}-${asset.id}`} variant="outlined" sx={{ overflow: 'hidden' }}>
                <Box sx={{ aspectRatio: '3/2', bgcolor: 'action.hover', display: 'grid', placeItems: 'center', overflow: 'hidden' }}>
                  {available ? <Box onClick={() => { setViewer(asset); setZoom(1) }} sx={{ width: '100%', height: '100%', cursor: 'zoom-in' }}><SmartImage src={asset.url} alt={asset.type}/></Box> : <Typography variant="body2" color="text.disabled">暂无图片</Typography>}
                </Box>
                <Box sx={{ p: 1.25 }}><Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}><Chip size="small" label={imageLabel(asset.type)}/>{asset.primary && <Chip size="small" color="primary" label="主图"/>}{asset.locked && <Chip size="small" color="warning" label="用户锁定"/>}<Chip size="small" color={status?.status === 'Normal' ? 'success' : status?.status === 'Failed' ? 'error' : 'warning'} label={imageStatusLabel(status?.status)}/></Stack>
                  <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: .75 }}>{asset.width && asset.height ? `${asset.width}×${asset.height}` : '尺寸待校验'} · {formatImageSize(asset.fileSize)}</Typography>
                  <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: .25 }}>来源：{asset.provider || asset.ownership || '本地'} · 更新：{date(asset.downloadedAt)}</Typography>
                  <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', mt: 1 }}>
                    <Button size="small" disabled={!available} onClick={() => { setViewer(asset); setZoom(1) }}>查看</Button>
                    <Button size="small" disabled={asset.id <= 0} onClick={() => openAssetDirectory(asset)}>目录</Button>
                    <Button size="small" disabled={asset.id <= 0} onClick={() => revealAsset(asset)}>文件</Button>
                    <Button size="small" disabled={!canGenerateImage(asset.type)} onClick={() => generateImage(asset.type)}>重新生成</Button>
                    <Button size="small" onClick={() => openReplace(asset)}>替换</Button>
                    <Button size="small" disabled={!available || asset.type === 'GIF'} startIcon={<ContentCutRoundedIcon/>} onClick={() => openCrop(asset)}>裁切</Button>
                    <Button size="small" disabled={asset.id <= 0} color={asset.locked ? 'warning' : 'inherit'} startIcon={asset.locked ? <LockOpenRoundedIcon/> : <LockRoundedIcon/>} onClick={() => void setImageLock(asset)}>{asset.locked ? '解锁' : '锁定'}</Button>
                    <Button size="small" disabled={asset.id <= 0} color="error" onClick={() => previewDeleteImage(asset)}>删除</Button>
                  </Stack>
                </Box>
              </Card>})}
            </Box>
          </SurfaceSection>
        </Stack>
        <SurfaceSection title="媒体文件" description={`${movie.mediaFiles.length} 个关联文件`}>
          <Stack spacing={1}>{movie.mediaFiles.length ? movie.mediaFiles.map((file) => <Box key={file.id} sx={{ p: 1.5, borderRadius: 2, bgcolor: 'action.hover', display: 'flex', gap: 1.25, alignItems: 'center', border: 1, borderColor: 'divider' }}>
            <FolderRoundedIcon color={file.existsState === 'Missing' ? 'error' : 'primary'}/><Box sx={{ minWidth: 0, flex: 1 }}><Typography noWrap title={file.path} sx={{ fontWeight: 700 }}>{file.fileName}</Typography><Typography variant="caption" color="text.secondary">{file.extension || '文件'} · {formatSize(file.fileSize)} · {file.existsState === 'Missing' ? '文件缺失' : '文件可用'}</Typography></Box>{file.primary && <Chip size="small" label="主文件" color="primary"/>}
          </Box>) : <Typography variant="body2" color="text.secondary">暂无关联媒体文件。</Typography>}</Stack>
          <Divider sx={{ my: 2 }}/><Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mb: 2 }}><Button variant="outlined" disabled={busy} onClick={() => previewNfo('import')}>导入 NFO</Button><Button variant="outlined" disabled={busy} onClick={() => previewNfo('export')}>导出 NFO</Button><Button variant="outlined" disabled={busy} onClick={() => { setOrganizerOpen(true); setOrganizerPreview(undefined) }}>整理文件</Button></Stack><Stack spacing={1}><Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}><Typography variant="body2" color="text.secondary">刮削状态</Typography><Typography variant="body2">{movie.scrapeStatus || '未知'}</Typography></Box><Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}><Typography variant="body2" color="text.secondary">更新时间</Typography><Typography variant="body2">{date(movie.updatedAt)}</Typography></Box></Stack>
        </SurfaceSection>
      </Box>
      <Stack direction="row" spacing={1.25} sx={{ justifyContent: 'space-between' }}><Button startIcon={<NavigateBeforeRoundedIcon/>} disabled={!neighbors.previousId} onClick={() => go(neighbors.previousId)}>上一部</Button><Button endIcon={<NavigateNextRoundedIcon/>} disabled={!neighbors.nextId} onClick={() => go(neighbors.nextId)}>下一部</Button></Stack>
    </Stack>}
    <Dialog open={tagDialog} onClose={() => setTagDialog(false)} fullWidth maxWidth="sm">
      <DialogTitle>编辑影片标签</DialogTitle>
      <DialogContent>
        <Typography variant="overline" color="text.secondary" sx={{ fontWeight: 750 }}>已选标签</Typography>
        <Paper variant="outlined" sx={{ minHeight: 64, mt: .5, p: 1.25, borderRadius: 2.5 }}>
          <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap' }}>
            {selectedTags.length ? selectedTags.map((tag) => <Chip key={tag.id} label={tag.name} color="primary" clickable onClick={() => setSelectedTags((value) => value.filter((item) => item.id !== tag.id))} onDelete={() => setSelectedTags((value) => value.filter((item) => item.id !== tag.id))}/>) : <Typography variant="body2" color="text.disabled">尚未选择标签</Typography>}
          </Stack>
        </Paper>
        <Typography variant="overline" color="text.secondary" sx={{ display: 'block', fontWeight: 750, mt: 2 }}>标签池</Typography>
        <TextField size="small" fullWidth value={tagSearch} onChange={(event) => setTagSearch(event.target.value)} placeholder="搜索标签" sx={{ mt: .5 }}/>
        <Paper variant="outlined" sx={{ minHeight: 110, maxHeight: 260, overflowY: 'auto', mt: .5, p: 1.25, borderRadius: 2.5 }}>
          <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap' }}>
            {tagOptions.filter((tag) => !selectedTags.some((selected) => selected.id === tag.id) && tag.name.toLocaleLowerCase().includes(tagSearch.trim().toLocaleLowerCase())).map((tag) => <Chip key={tag.id} label={tag.name} variant="outlined" clickable onClick={() => setSelectedTags((value) => [...value, tag])}/>)}
          </Stack>
        </Paper>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>点击标签池添加；点击已选标签上的 × 移除。用户手工标签不会被同步覆盖。</Typography>
      </DialogContent>
      <DialogActions><Button onClick={() => setTagDialog(false)}>取消</Button><Button variant="contained" onClick={saveTags}>保存</Button></DialogActions>
    </Dialog>
    <Dialog open={actorDialog} onClose={() => setActorDialog(false)} fullWidth maxWidth="sm"><DialogTitle>编辑演员关系</DialogTitle><DialogContent><Autocomplete multiple filterOptions={(options) => options} options={actorOptions} value={selectedActors} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onInputChange={(_, value) => setActorSearch(value)} onChange={(_, value) => setSelectedActors(value)} renderInput={(params) => <TextField {...params} autoFocus label="搜索并选择演员" margin="normal"/>}/></DialogContent><DialogActions><Button onClick={() => setActorDialog(false)}>取消</Button><Button variant="contained" onClick={saveActors}>保存</Button></DialogActions></Dialog>
    <SafeDeleteDialog preview={deletePreview} command={deleteCommand} onClose={closeDeleteDialog} onLaunched={handleDeleteLaunched}/>
    <Dialog open={Boolean(nfoPreview)} onClose={() => setNfoPreview(undefined)} maxWidth="sm" fullWidth><DialogTitle>{nfoMode === 'import' ? '导入 NFO 预览' : '导出 NFO 预览'}</DialogTitle><DialogContent><DialogContentText sx={{ overflowWrap: 'anywhere' }}>{nfoPreview?.path}</DialogContentText>{nfoPreview?.changes.length ? <Alert severity="info" sx={{ mt: 2 }}>将处理：{nfoPreview.changes.join('、')}</Alert> : null}{nfoPreview?.conflicts.length ? <Alert severity="warning" sx={{ mt: 1 }}>冲突字段保持原值：{nfoPreview.conflicts.join('、')}</Alert> : null}{nfoPreview?.warnings.map((warning) => <Alert key={warning} severity="warning" sx={{ mt: 1 }}>{warning}</Alert>)}</DialogContent><DialogActions><Button onClick={() => setNfoPreview(undefined)}>取消</Button>{nfoMode === 'export' && nfoPreview && !nfoPreview.canApply && <Button variant="outlined" onClick={() => confirmNfo(true)}>另存为 .lmm.nfo</Button>}<Button variant="contained" disabled={busy || Boolean(nfoPreview && !nfoPreview.canApply)} onClick={() => confirmNfo()}>{nfoMode === 'import' ? '确认导入' : '确认导出'}</Button></DialogActions></Dialog>
    <Dialog open={organizerOpen} onClose={() => !busy && setOrganizerOpen(false)} maxWidth="md" fullWidth><DialogTitle>整理文件</DialogTitle><DialogContent><Stack spacing={2} sx={{ mt: 1 }}><TextField label="文件名模板" value={organizerTemplate} onChange={event => { setOrganizerTemplate(event.target.value); setOrganizerPreview(undefined) }} helperText="支持 {Code}、{Title}、{Year}、{Actors}"/><TextField label="目标目录" value={organizerDestination} onChange={event => { setOrganizerDestination(event.target.value); setOrganizerPreview(undefined) }} helperText="留空时只在原目录重命名；不会覆盖任何已有目标。"/>{organizerPreview && <><Alert severity={organizerPreview.conflictItems ? 'error' : 'success'}>Dry Run：{organizerPreview.validItems} 项可执行，{organizerPreview.conflictItems} 项冲突。尚未修改文件。</Alert>{organizerPreview.items.map(item => <Paper key={item.mediaFileId} variant="outlined" sx={{ p: 1.5 }}><Typography variant="caption" color="text.secondary">原路径</Typography><Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{item.sourcePath}</Typography><Typography variant="caption" color="text.secondary">目标路径</Typography><Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{item.destinationPath}</Typography>{item.conflict && <Alert severity="error" sx={{ mt: 1 }}>{item.conflict}</Alert>}</Paper>)}</>}</Stack></DialogContent><DialogActions><Button onClick={() => setOrganizerOpen(false)}>取消</Button><Button variant="outlined" disabled={busy} onClick={dryRunOrganizer}>Dry Run</Button><Button variant="contained" disabled={busy || !organizerPreview || organizerPreview.conflictItems > 0} onClick={executeOrganizer}>确认并进入任务</Button></DialogActions></Dialog>
    <Dialog open={Boolean(replaceTarget)} onClose={() => !busy && setReplaceTarget(undefined)} maxWidth="sm" fullWidth>
      <DialogTitle>替换{replaceTarget ? imageLabel(replaceTarget.type) : '图片'}</DialogTitle>
      <DialogContent>
        <DialogContentText>请输入本地图片完整路径。Bridge 会校验图片、复制到受控图片目录、锁定该图片并刷新缓存。</DialogContentText>
        <TextField autoFocus fullWidth margin="normal" label="本地图片路径" value={replacePath} onChange={(event) => setReplacePath(event.target.value)} placeholder="D:\Images\cover.jpg"/>
      </DialogContent>
      <DialogActions><Button onClick={() => setReplaceTarget(undefined)} disabled={busy}>取消</Button><Button variant="contained" disabled={busy || !replacePath.trim()} onClick={confirmReplace}>确认替换</Button></DialogActions>
    </Dialog>
    <Dialog open={Boolean(cropTarget)} onClose={() => !busy && setCropTarget(undefined)} maxWidth="md" fullWidth>
      <DialogTitle>裁切卡图</DialogTitle>
      <DialogContent>
        <Stack spacing={2}>
          <Typography variant="body2" color="text.secondary">从当前图片裁切一张影片墙卡图，保存到 WallCrops，不覆盖源图。</Typography>
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
            <Button variant={cropAspect === 'landscape' ? 'contained' : 'outlined'} onClick={() => setCropAspect('landscape')}>横版 16:9</Button>
            <Button variant={cropAspect === 'portrait' ? 'contained' : 'outlined'} onClick={() => setCropAspect('portrait')}>竖版 2:3</Button>
          </Stack>
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
            <Button variant={cropAnchor === 'left' ? 'contained' : 'outlined'} onClick={() => setCropAnchor('left')}>左侧</Button>
            <Button variant={cropAnchor === 'center' ? 'contained' : 'outlined'} onClick={() => setCropAnchor('center')}>居中</Button>
            <Button variant={cropAnchor === 'right' ? 'contained' : 'outlined'} onClick={() => setCropAnchor('right')}>右侧</Button>
          </Stack>
          <Paper variant="outlined" sx={{ overflow: 'hidden', bgcolor: 'action.hover', maxHeight: 560, display: 'grid', placeItems: 'center' }}>
            {cropTarget?.url && <Box component="img" src={cropTarget.url} alt={cropTarget.type} sx={{ width: '100%', aspectRatio: cropAspect === 'landscape' ? '16/9' : '2/3', objectFit: 'cover', objectPosition: cropAnchor, display: 'block' }}/>}
          </Paper>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={() => setCropTarget(undefined)} disabled={busy}>取消</Button><Button variant="contained" disabled={busy || !cropTarget?.url} startIcon={<ContentCutRoundedIcon/>} onClick={confirmCrop}>保存裁切</Button></DialogActions>
    </Dialog>
    <Dialog open={Boolean(imageDeletePreview)} onClose={() => !busy && setImageDeletePreview(undefined)} maxWidth="sm" fullWidth>
      <DialogTitle>删除{imageDeletePreview ? imageLabel(imageDeletePreview.type) : '图片'}？</DialogTitle>
      <DialogContent><DialogContentText sx={{ overflowWrap: 'anywhere' }}>{imageDeletePreview?.path || imageDeletePreview?.fileName}</DialogContentText>{imageDeletePreview?.warnings.map((warning) => <Alert key={warning} severity="warning" sx={{ mt: 1 }}>{warning}</Alert>)}</DialogContent>
      <DialogActions><Button onClick={() => setImageDeletePreview(undefined)} disabled={busy}>取消</Button><Button variant="contained" color="error" disabled={busy} onClick={confirmDeleteImage}>确认删除</Button></DialogActions>
    </Dialog>
    <Dialog open={Boolean(viewer)} onClose={() => setViewer(undefined)} maxWidth="lg" fullWidth>
      <DialogTitle>{viewer?.type}</DialogTitle>
      <DialogContent onWheel={event => { event.preventDefault(); setZoom(value => Math.max(.4, Math.min(4, value + (event.deltaY < 0 ? .15 : -.15)))) }} sx={{ height: '72vh', display: 'grid', placeItems: 'center', overflow: 'auto', bgcolor: 'background.default' }}>
        {viewer?.url && <Box component="img" src={viewer.url} alt={viewer.type} sx={{ maxWidth: zoom === 1 ? '100%' : 'none', maxHeight: zoom === 1 ? '100%' : 'none', transform: `scale(${zoom})`, transformOrigin: 'center', transition: 'transform .12s ease' }}/>}
      </DialogContent>
      <DialogActions><Button startIcon={<ZoomOutRoundedIcon/>} onClick={() => setZoom(value => Math.max(.4, value - .25))}>缩小</Button><Button startIcon={<ZoomInRoundedIcon/>} onClick={() => setZoom(value => Math.min(4, value + .25))}>放大</Button><Button onClick={() => setZoom(1)}>适应窗口</Button><Button onClick={() => setZoom(2)}>原图</Button><Button onClick={() => setViewer(undefined)}>关闭</Button></DialogActions>
    </Dialog>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
