import AddRoundedIcon from '@mui/icons-material/AddRounded'
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded'
import CameraAltRoundedIcon from '@mui/icons-material/CameraAltRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import FavoriteBorderRoundedIcon from '@mui/icons-material/FavoriteBorderRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import InfoOutlinedIcon from '@mui/icons-material/InfoOutlined'
import NavigateBeforeRoundedIcon from '@mui/icons-material/NavigateBeforeRounded'
import NavigateNextRoundedIcon from '@mui/icons-material/NavigateNextRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import SellRoundedIcon from '@mui/icons-material/SellRounded'
import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import ZoomInRoundedIcon from '@mui/icons-material/ZoomInRounded'
import ZoomOutRoundedIcon from '@mui/icons-material/ZoomOutRounded'
import {
  Alert,
  Autocomplete,
  Avatar,
  Box,
  Button,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Divider,
  Paper,
  Rating,
  Snackbar,
  Stack,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material'
import type { ReactNode, WheelEvent } from 'react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router'
import { SafeDeleteDialog } from '@/components/SafeDeleteDialog'
import { SmartImage, clearImageMemoryCache } from '@/components/SmartImage'
import { BRIDGE_ORIGIN, bridge } from '@/services/bridge'
import type { ImageAsset, ImageCenterStatus, MovieDetail, NamedItem, NfoPreview, OrganizerPreview, SafeDeletePreview, SafeDeletePreviewCommand } from '@/types/media'

const date = (value?: string) => value?.slice(0, 10) || '未知'
const formatSize = (bytes: number) => bytes ? `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB` : '未知'
const formatDuration = (seconds: number) => {
  if (!seconds) return '未知'
  const hours = Math.floor(seconds / 3600)
  const minutes = Math.round((seconds % 3600) / 60)
  return hours ? `${hours} 小时 ${minutes} 分钟` : `${minutes} 分钟`
}

function horizontalWheel(event: WheelEvent<HTMLElement>) {
  if (Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return
  event.currentTarget.scrollLeft += event.deltaY
  event.preventDefault()
}

const horizontalScrollSx = {
  scrollbarWidth: 'none',
  msOverflowStyle: 'none',
  '&::-webkit-scrollbar': { display: 'none' },
}

function SectionTitle({ icon, title }: { icon: ReactNode; title: string }) {
  return <Stack direction="row" spacing={.75} sx={{ alignItems: 'center', mb: .75 }}>
    <Box sx={{ color: 'primary.main', display: 'flex' }}>{icon}</Box>
    <Typography variant="subtitle1" sx={{ fontWeight: 850, lineHeight: 1.2 }}>{title}</Typography>
  </Stack>
}

function LinkText({ children, onClick }: { children: ReactNode; onClick: () => void }) {
  return <Typography component="button" type="button" onClick={onClick} sx={{
    appearance: 'none',
    border: 1,
    borderColor: 'divider',
    borderRadius: 1,
    bgcolor: 'action.hover',
    color: 'primary.main',
    cursor: 'pointer',
    font: 'inherit',
    fontWeight: 750,
    lineHeight: 1.35,
    maxWidth: '100%',
    px: 1,
    py: .35,
    textAlign: 'left',
    overflowWrap: 'anywhere',
    transition: theme => theme.transitions.create(['background-color', 'border-color', 'color'], { duration: theme.transitions.duration.shortest }),
    '&:hover': {
      bgcolor: 'action.selected',
      borderColor: 'primary.main',
      color: 'primary.light',
    },
  }}>{children}</Typography>
}

function DetailField({ label, children }: { label: string; children: ReactNode }) {
  return <Box sx={{
    display: 'grid',
    gridTemplateColumns: { xs: '82px minmax(0,1fr)', sm: '96px minmax(0,1fr)' },
    columnGap: 1.5,
    alignItems: 'start',
    py: .85,
    borderBottom: 1,
    borderColor: 'divider',
  }}>
    <Typography variant="body2" color="text.secondary" sx={{ fontWeight: 800, lineHeight: 1.7 }}>{label}</Typography>
    <Box sx={{ minHeight: 24, minWidth: 0 }}>{children}</Box>
  </Box>
}

function SourceLink({ value, onOpen }: { value?: string; onOpen: (value: string) => void }) {
  if (!value) return <Typography variant="body2" color="text.disabled">暂无</Typography>
  return <Typography
    component="button"
    type="button"
    onClick={() => onOpen(value)}
    variant="body2"
    color="primary"
    sx={{
      appearance: 'none',
      border: 0,
      bgcolor: 'transparent',
      cursor: 'pointer',
      display: 'inline-block',
      lineHeight: 1.7,
      maxWidth: '100%',
      overflowWrap: 'anywhere',
      p: 0,
      textAlign: 'left',
      textDecoration: 'none',
      '&:hover': { textDecoration: 'underline' },
    }}
  >
    {value}
  </Typography>
}

function InlineEntityList({ items, empty = '暂无', onOpen }: { items: NamedItem[]; empty?: string; onOpen: (item: NamedItem) => void }) {
  const readable = (items ?? []).filter((item) => item.name && !item.name.includes('\uFFFD'))
  if (!readable.length) return <Typography variant="body2" color="text.disabled">{empty}</Typography>
  return <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
    {readable.map((item) => <LinkText key={item.id} onClick={() => onOpen(item)}>{item.name}</LinkText>)}
  </Stack>
}

export default function MovieDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const context = (location.state as { context?: { search?: string; sort?: string } } | null)?.context

  const [movie, setMovie] = useState<MovieDetail>()
  const [imageAssets, setImageAssets] = useState<ImageAsset[]>([])
  const [imageStatus, setImageStatus] = useState<ImageCenterStatus>()
  const [neighbors, setNeighbors] = useState<{ previousId?: number; nextId?: number }>({})
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [posterFailed, setPosterFailed] = useState(false)
  const [descriptionExpanded, setDescriptionExpanded] = useState(false)
  const [imageTab, setImageTab] = useState<'stills' | 'screenshots'>('stills')
  const [heroOverrideUrl, setHeroOverrideUrl] = useState<string>()
  const [numberDialog, setNumberDialog] = useState(false)
  const [manualNumber, setManualNumber] = useState('')

  const [tagDialog, setTagDialog] = useState(false)
  const [tagOptions, setTagOptions] = useState<NamedItem[]>([])
  const [selectedTags, setSelectedTags] = useState<NamedItem[]>([])
  const [tagSearch, setTagSearch] = useState('')
  const [actorDialog, setActorDialog] = useState(false)
  const [actorOptions, setActorOptions] = useState<NamedItem[]>([])
  const [selectedActors, setSelectedActors] = useState<NamedItem[]>([])
  const [actorSearch, setActorSearch] = useState('')

  const [deletePreview, setDeletePreview] = useState<SafeDeletePreview>()
  const [deleteCommand, setDeleteCommand] = useState<SafeDeletePreviewCommand>()
  const [nfoPreview, setNfoPreview] = useState<NfoPreview>()
  const [nfoMode, setNfoMode] = useState<'import' | 'export'>('export')
  const [organizerOpen, setOrganizerOpen] = useState(false)
  const [organizerPreview, setOrganizerPreview] = useState<OrganizerPreview>()
  const [organizerTemplate, setOrganizerTemplate] = useState('{Code}')
  const [organizerDestination, setOrganizerDestination] = useState('')

  const [viewerItems, setViewerItems] = useState<ImageAsset[]>([])
  const [viewerIndex, setViewerIndex] = useState(0)
  const [zoom, setZoom] = useState(1)
  const viewerImageRef = useRef<HTMLImageElement | null>(null)
  const currentViewer = viewerItems[viewerIndex]

  const loadMovie = (movieId: number) => Promise.all([
    bridge.movie(movieId).then(setMovie),
    bridge.movieImages(movieId).then(setImageAssets),
    bridge.movieImageStatus(movieId).then(setImageStatus),
  ])

  useEffect(() => {
    const movieId = Number(id)
    if (!Number.isFinite(movieId)) {
      setError('无效影片编号')
      return
    }
    setMovie(undefined)
    setImageAssets([])
    setPosterFailed(false)
    setHeroOverrideUrl(undefined)
    setDescriptionExpanded(false)
    setError('')
    loadMovie(movieId).catch((reason: Error) => setError(reason.message))
    bridge.neighbors(movieId, context?.search, context?.sort).then(setNeighbors).catch(() => setNeighbors({}))
  }, [id])

  useEffect(() => {
    if (!actorDialog) return
    const timer = window.setTimeout(() => {
      bridge.entities('actors', actorSearch, 'name', 48, 0)
        .then((result) => setActorOptions([...selectedActors, ...result.items.filter((item) => !selectedActors.some((selected) => selected.id === item.id))]))
        .catch((reason: Error) => setNotice(reason.message))
    }, 200)
    return () => window.clearTimeout(timer)
  }, [actorDialog, actorSearch, selectedActors])

  useEffect(() => {
    if (!currentViewer) return
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        if (document.fullscreenElement) void document.exitFullscreen()
        else setViewerItems([])
      } else if (event.key === 'ArrowLeft') {
        setViewerIndex((value) => Math.max(0, value - 1))
      } else if (event.key === 'ArrowRight') {
        setViewerIndex((value) => Math.min(viewerItems.length - 1, value + 1))
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [currentViewer, viewerItems.length])

  const stills = useMemo(() => imageAssets.filter(asset => asset.type === 'Preview' && asset.url), [imageAssets])
  const screenshots = useMemo(() => imageAssets.filter(asset => asset.type === 'Screenshot' && asset.url), [imageAssets])

  useEffect(() => {
    setImageTab(stills.length ? 'stills' : screenshots.length ? 'screenshots' : 'stills')
  }, [screenshots.length, stills.length])

  const heroImageUrl = imageAssets.find(asset => asset.type === 'Fanart' && asset.primary && asset.url)?.url
    ?? imageAssets.find(asset => asset.type === 'Fanart' && asset.url)?.url
    ?? imageAssets.find(asset => ['Landscape', 'Banner'].includes(asset.type) && asset.url)?.url
    ?? imageAssets.find(asset => asset.type === 'Preview' && asset.url)?.url
    ?? imageAssets.find(asset => asset.type === 'Poster' && asset.primary && asset.url)?.url
    ?? imageAssets.find(asset => asset.type === 'Poster' && asset.url)?.url
    ?? movie?.coverUrl
  const displayedHeroImageUrl = heroOverrideUrl ?? heroImageUrl
  useEffect(() => { setPosterFailed(false) }, [displayedHeroImageUrl])

  const displayedImages = imageTab === 'stills' ? stills : screenshots
  useEffect(() => {
    if (heroOverrideUrl && !displayedImages.some((asset) => asset.url === heroOverrideUrl)) setHeroOverrideUrl(undefined)
  }, [displayedImages, heroOverrideUrl])
  const primaryFile = movie?.mediaFiles.find(file => file.primary) ?? movie?.mediaFiles[0]
  const metadataChecks = movie?.metadataStatus?.checks ?? []
  const metadataComplete = (matcher: (value: string) => boolean) => metadataChecks.some(item => matcher(`${item.key} ${item.label}`.toLowerCase()) && item.complete)
  const hasImageType = (type: string) => imageAssets.some(asset => asset.type === type && asset.url)
  const statusRows = movie ? [
    { label: '元数据', complete: movie.scraped || movie.metadataStatus?.state === 'complete' },
    { label: 'Poster', complete: hasImageType('Poster') || Boolean(movie.coverUrl) },
    { label: 'Fanart', complete: hasImageType('Fanart') },
    { label: '演员', complete: movie.actors.length > 0 || metadataComplete(value => value.includes('actor') || value.includes('演员')) },
    { label: '剧照', complete: stills.length > 0 },
    { label: '截图', complete: screenshots.length > 0 },
    { label: 'NFO', complete: Boolean(movie.nfoPath) || metadataComplete(value => value.includes('nfo')) },
  ] : []

  const openFilteredWall = (param: string, nameParam: string, item: NamedItem) => {
    navigate(`/media?${new URLSearchParams({ [param]: String(item.id), [nameParam]: item.name })}`)
  }
  const go = (movieId?: number) => movieId && navigate(`/movies/${movieId}`, { state: { context }, replace: true })
  const play = () => movie && bridge.play(movie.id).then(() => setNotice(`正在打开：${movie.code || movie.title}`)).catch((reason: Error) => setNotice(reason.message))
  const mutate = (action: Promise<unknown>) => {
    if (!movie) return
    setBusy(true)
    action.then(() => loadMovie(movie.id)).then(() => setNotice('已保存')).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false))
  }
  const syncMetadata = () => movie && bridge.syncMovie(movie.id).then(result => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message))
  const reidentifyNumber = () => movie && mutate(bridge.reidentifyMovieNumber(movie.id))
  const saveManualNumber = () => {
    if (!movie || !manualNumber.trim()) return
    setBusy(true)
    bridge.updateMovieNumber(movie.id, manualNumber.trim())
      .then(result => loadMovie(movie.id).then(() => setNotice(result.message)))
      .then(() => setNumberDialog(false))
      .catch((reason: Error) => setNotice(reason.message))
      .finally(() => setBusy(false))
  }
  const refreshStatus = () => movie && loadMovie(movie.id).then(() => setNotice('状态已刷新')).catch((reason: Error) => setNotice(reason.message))
  const refreshImages = () => {
    clearImageMemoryCache()
    if (movie) loadMovie(movie.id).then(() => setNotice('图片状态已刷新')).catch((reason: Error) => setNotice(reason.message))
  }
  const rebuildCache = () => bridge.rebuildImageCache().then(result => setNotice(`${result.message}，${result.totalItems} 部影片`)).catch((reason: Error) => setNotice(reason.message))
  const openMovieFolder = () => {
    const path = primaryFile?.path
    if (!path) {
      setNotice('没有可打开的文件目录')
      return
    }
    bridge.revealFile(path).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))
  }
  const openImageFolder = () => {
    const directory = imageAssets.find(asset => asset.directory)?.directory
    if (!directory) {
      openMovieFolder()
      return
    }
    bridge.openDirectory(directory).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))
  }
  const openTags = () => {
    if (!movie) return
    setSelectedTags(movie.tags ?? [])
    setTagSearch('')
    setTagDialog(true)
    bridge.entities('tags', '', 'name', 1000, 0).then((result) => setTagOptions(result.items)).catch((reason: Error) => setNotice(reason.message))
  }
  const saveTags = () => {
    if (!movie) return
    const current = new Set((movie.tags ?? []).map((item) => item.id))
    const selected = new Set(selectedTags.map((item) => item.id))
    mutate(bridge.updateMovieTags(movie.id, [...selected].filter((tag) => !current.has(tag)), [...current].filter((tag) => !selected.has(tag))))
    setTagDialog(false)
  }
  const toggleSelectedTag = (tag: NamedItem) => {
    setSelectedTags((value) => value.some((item) => item.id === tag.id) ? value.filter((item) => item.id !== tag.id) : [...value, tag])
  }
  const openActors = () => {
    if (!movie) return
    setSelectedActors(movie.actors ?? [])
    setActorOptions(movie.actors ?? [])
    setActorDialog(true)
  }
  const saveActors = () => {
    if (!movie) return
    mutate(bridge.setMovieActors(movie.id, selectedActors.map((item) => item.id)))
    setActorDialog(false)
  }
  const previewDelete = () => {
    if (!movie) return
    const command = { movieIds: [movie.id], mode: 'metadata', deleteDatabaseInfo: true } satisfies SafeDeletePreviewCommand
    setDeleteCommand(command)
    bridge.previewSafeDelete(command).then(setDeletePreview).catch((reason: Error) => setNotice(reason.message))
  }
  const closeDeleteDialog = () => {
    setDeletePreview(undefined)
    setDeleteCommand(undefined)
  }
  const handleDeleteLaunched = (result: { message: string }) => {
    closeDeleteDialog()
    navigate('/media', { replace: true })
    window.setTimeout(() => setNotice(`${result.message} 可在任务中心查看结果。`), 0)
  }
  const previewNfo = (mode: 'import' | 'export') => {
    if (!movie) return
    setBusy(true)
    setNfoMode(mode)
    ;(mode === 'import' ? bridge.previewNfoImport(movie.id) : bridge.previewNfoExport(movie.id))
      .then(setNfoPreview)
      .catch((reason: Error) => setNotice(reason.message))
      .finally(() => setBusy(false))
  }
  const confirmNfo = (separateWhenLocked = false) => {
    if (!movie || !nfoPreview) return
    setBusy(true)
    const action = nfoMode === 'import'
      ? bridge.importNfo(movie.id, nfoPreview.confirmationToken)
      : bridge.exportNfo(movie.id, nfoPreview.confirmationToken, separateWhenLocked)
    action.then(result => {
      setNfoPreview(undefined)
      setNotice(result.message)
      return loadMovie(movie.id)
    }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false))
  }
  const dryRunOrganizer = () => {
    if (!movie) return
    setBusy(true)
    bridge.organizerDryRun([movie.id], organizerTemplate, organizerDestination).then(setOrganizerPreview).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false))
  }
  const executeOrganizer = () => {
    if (!organizerPreview) return
    setBusy(true)
    bridge.executeOrganizer(organizerPreview.taskId, organizerPreview.confirmationToken).then(result => {
      setOrganizerOpen(false)
      setOrganizerPreview(undefined)
      setNotice(`${result.message} 可在任务中心查看进度。`)
    }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false))
  }
  const openViewer = (items: ImageAsset[], index: number) => {
    setViewerItems(items)
    setViewerIndex(index)
    setZoom(1)
  }
  const enterFullscreen = () => {
    if (viewerImageRef.current && !document.fullscreenElement) void viewerImageRef.current.requestFullscreen()
  }
  const onImageTabChange = (_: unknown, value: 'stills' | 'screenshots' | null) => {
    if (!value) return
    setImageTab(value)
    if (value === 'stills' && stills.length === 0) setNotice('暂无剧照')
    if (value === 'screenshots' && screenshots.length === 0) setNotice('暂无截图')
  }
  const normalizedTagSearch = tagSearch.trim().toLocaleLowerCase()
  const filteredTagOptions = useMemo(() => tagOptions.filter((tag) => tag.name.toLocaleLowerCase().includes(normalizedTagSearch)), [normalizedTagSearch, tagOptions])
  const selectedTagIds = useMemo(() => new Set(selectedTags.map((tag) => tag.id)), [selectedTags])
  const heroColumns = { xs: '1fr', lg: 'minmax(420px, 1.2fr) minmax(440px, 1fr)' }

  return <Box sx={{ '@keyframes detailIn': { from: { opacity: 0, transform: 'translateY(10px)' }, to: { opacity: 1, transform: 'translateY(0)' } } }}>
    {error && <Alert severity="error">{error}</Alert>}
    {!movie && !error ? <Box sx={{ minHeight: 420, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : movie && <Stack spacing={1.75} sx={{ animation: 'detailIn .32s ease both', '@media (prefers-reduced-motion: reduce)': { animation: 'none' } }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
        <Button color="inherit" startIcon={<ArrowBackRoundedIcon/>} onClick={() => navigate(-1)}>返回</Button>
        <Box sx={{ flex: '1 1 auto' }}/>
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: heroColumns, border: 1, borderColor: 'divider', borderRadius: 1, overflow: 'hidden' }}>
        <Box sx={{ height: 'clamp(720px, calc(72vh + 200px), 920px)', bgcolor: 'background.default', display: 'grid', placeItems: 'center', overflow: 'hidden' }}>
          {displayedHeroImageUrl && !posterFailed ? <SmartImage src={displayedHeroImageUrl} alt={movie.code || movie.title || ''} fit="contain" eager bgcolor="background.default" onError={() => setPosterFailed(true)}/> : <Typography color="text.disabled">{posterFailed ? '图片损坏或不可用' : '暂无图片'}</Typography>}
        </Box>
        <Stack spacing={2} sx={{ minWidth: 0, p: { xs: 2, md: 3 }, borderLeft: { lg: 1 }, borderColor: 'divider', height: '100%' }}>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="h5" sx={{ fontWeight: 850 }}>{movie.code || '番号未知'}</Typography>
            <Typography variant="body2" color="text.secondary" sx={{ mt: .75, lineHeight: 1.7, overflowWrap: 'anywhere' }}>{movie.title || movie.originalTitle || movie.code || `影片 ${movie.id}`}</Typography>
          </Box>
          <DetailField label="发行日期"><Typography variant="body2">{date(movie.releaseDate)}</Typography></DetailField>
          <DetailField label="时长"><Typography variant="body2">{formatDuration(movie.durationSeconds)}</Typography></DetailField>
          <DetailField label="文件大小"><Typography variant="body2">{formatSize(primaryFile?.fileSize ?? 0)}</Typography></DetailField>
          {movie.numberRecognition && <>
            <DetailField label="原始文件名"><Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{movie.numberRecognition.originalFileName}</Typography></DetailField>
            <DetailField label="识别番号"><Typography variant="body2">{movie.numberRecognition.detectedNumber || '未识别'}</Typography></DetailField>
            <DetailField label="标准番号"><Typography variant="body2">{movie.numberRecognition.normalizedNumber || '未识别'}</Typography></DetailField>
            <DetailField label="识别规则"><Typography variant="body2">{movie.numberRecognition.matchedRule || '无匹配规则'}</Typography></DetailField>
            <DetailField label="识别置信度"><Typography variant="body2">{Math.round(movie.numberRecognition.confidence * 100)}%</Typography></DetailField>
          </>}
          <DetailField label="厂商"><InlineEntityList items={movie.studios} onOpen={(item) => openFilteredWall('studioId', 'studioName', item)}/></DetailField>
          <DetailField label="导演"><InlineEntityList items={movie.directors} onOpen={(item) => openFilteredWall('directorId', 'directorName', item)}/></DetailField>
          <DetailField label="简介">
            <Typography
              component={movie.description ? 'button' : 'p'}
              type={movie.description ? 'button' : undefined}
              onClick={() => movie.description && setDescriptionExpanded(value => !value)}
              color="text.secondary"
              sx={{
                appearance: 'none',
                border: 0,
                bgcolor: 'transparent',
                color: 'text.secondary',
                cursor: movie.description ? 'pointer' : 'default',
                font: 'inherit',
                lineHeight: 1.7,
                p: 0,
                textAlign: 'left',
                whiteSpace: descriptionExpanded ? 'pre-wrap' : 'nowrap',
                overflow: 'hidden',
                textOverflow: descriptionExpanded ? 'clip' : 'ellipsis',
                width: '100%',
              }}
            >{movie.description || '暂无简介'}</Typography>
          </DetailField>
          <DetailField label="系列"><InlineEntityList items={movie.series} onOpen={(item) => openFilteredWall('seriesId', 'seriesName', item)}/></DetailField>
          <DetailField label="标签"><InlineEntityList items={movie.genres} onOpen={(item) => openFilteredWall('genreId', 'genreName', item)}/></DetailField>
          <DetailField label="来源页"><SourceLink value={movie.sourceUrl} onOpen={(url) => bridge.openUrl(url).catch((reason: Error) => setNotice(reason.message))}/></DetailField>
          <DetailField label="评分">
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
              <Rating value={movie.userRatingSet ? movie.userRating : 0} precision={.5} disabled={busy} onChange={(_, value) => mutate(bridge.setUserState(movie.id, value === null ? { clearRating: true } : { rating: value }))}/>
              <Typography variant="body2" color="text.secondary">{movie.userRatingSet ? movie.userRating.toFixed(1) : '未评分'}</Typography>
            </Stack>
          </DetailField>
          <DetailField label="我的标签">
            <Stack spacing={.75}>
              <InlineEntityList items={movie.tags} onOpen={(item) => openFilteredWall('customTagId', 'customTagName', item)}/>
              <Button size="small" variant="text" startIcon={<AddRoundedIcon/>} onClick={openTags} sx={{ alignSelf: 'flex-start', px: 0 }}>添加标签</Button>
            </Stack>
          </DetailField>
          <Box sx={{ mt: 'auto', pt: 2 }}>
            <Stack direction="row" spacing={1.25} useFlexGap sx={{ flexWrap: 'wrap' }}>
              <Button variant="outlined" color={movie.favorite ? 'error' : 'primary'} startIcon={movie.favorite ? <FavoriteRoundedIcon/> : <FavoriteBorderRoundedIcon/>} disabled={busy} onClick={() => mutate(bridge.setUserState(movie.id, { favorite: !movie.favorite }))}>{movie.favorite ? '已收藏' : '收藏'}</Button>
              <Button variant="contained" startIcon={<PlayArrowRoundedIcon/>} onClick={play}>播放影片</Button>
              <Button variant="outlined" startIcon={<SyncRoundedIcon/>} onClick={syncMetadata}>重新同步</Button>
              {movie.numberRecognition && <Button variant="outlined" startIcon={<RefreshRoundedIcon/>} disabled={busy} onClick={reidentifyNumber}>重新识别</Button>}
              {movie.numberRecognition && <Button variant="outlined" disabled={busy} onClick={() => { setManualNumber(movie.code || ''); setNumberDialog(true) }}>手动修改番号</Button>}
            </Stack>
          </Box>
        </Stack>
      </Box>

      <Divider sx={{ my: -.25 }}/>

      <Box>
        <Box onWheel={horizontalWheel} sx={{ display: 'flex', gap: 2, overflowX: 'auto', pb: .25, scrollBehavior: 'smooth', ...horizontalScrollSx }}>
          {movie.actors.length ? movie.actors.map(actor => <Box key={actor.id} onClick={() => navigate(`/actors/${actor.id}`)} sx={{ flex: '0 0 84px', textAlign: 'center', cursor: 'pointer', color: 'primary.main', '&:hover': { color: 'primary.light' } }}>
            <Avatar src={`${BRIDGE_ORIGIN}/api/actors/${actor.id}/image`} alt={actor.name} sx={{ width: 64, height: 64, mx: 'auto', mb: .75, bgcolor: 'action.hover', color: 'text.secondary', border: 1, borderColor: 'divider' }}>{actor.name.slice(0, 1)}</Avatar>
            <Typography variant="body2" noWrap sx={{ fontWeight: 750 }}>{actor.name}</Typography>
          </Box>) : <Typography variant="body2" color="text.secondary">暂无演员信息</Typography>}
        </Box>
      </Box>

      <Divider sx={{ my: -.25 }}/>

      <Box>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'flex-start', mb: .5 }}>
          <ToggleButtonGroup exclusive size="small" value={imageTab} onChange={onImageTabChange} sx={{ alignSelf: 'flex-start', '& .MuiToggleButton-root': { minHeight: 31, px: 1.25, py: 0.25 } }}>
            <ToggleButton value="stills">剧照</ToggleButton>
            <ToggleButton value="screenshots">截图</ToggleButton>
          </ToggleButtonGroup>
        </Stack>
        <Box onWheel={horizontalWheel} sx={{ display: 'flex', gap: 1.25, overflowX: 'auto', pb: .5, minHeight: 185, scrollBehavior: 'smooth', ...horizontalScrollSx }}>
          {displayedImages.length ? displayedImages.map((asset, index) => <Box key={`${asset.type}-${asset.id}`} onClick={() => { setHeroOverrideUrl(asset.url); setPosterFailed(false) }} sx={{ flex: '0 0 189px', height: 180, bgcolor: 'background.default', border: 1, borderColor: 'divider', borderRadius: 1, overflow: 'hidden', cursor: 'pointer' }}>
            <SmartImage src={asset.url} alt={asset.type}/>
          </Box>) : <Box sx={{ width: '100%', minHeight: 128, display: 'grid', placeItems: 'center', border: 1, borderColor: 'divider', borderRadius: 1, bgcolor: 'action.hover' }}>
            <Typography color="text.secondary">{imageTab === 'stills' ? '暂无剧照' : '暂无截图'}</Typography>
          </Box>}
        </Box>
      </Box>

      <Divider/>

      <Box>
        <SectionTitle icon={<FolderRoundedIcon/>} title="文件信息"/>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '160px minmax(0,1fr)' }, gap: 1 }}>
          <Typography color="text.secondary">文件名：</Typography><Typography sx={{ overflowWrap: 'anywhere' }}>{primaryFile?.fileName || '暂无'}</Typography>
          <Typography color="text.secondary">分辨率：</Typography><Typography>未读取</Typography>
          <Typography color="text.secondary">视频编码：</Typography><Typography>未读取</Typography>
          <Typography color="text.secondary">音频编码：</Typography><Typography>未读取</Typography>
          <Typography color="text.secondary">大小：</Typography><Typography>{formatSize(primaryFile?.fileSize ?? 0)}</Typography>
          <Typography color="text.secondary">时长：</Typography><Typography>{formatDuration(movie.durationSeconds)}</Typography>
          <Typography color="text.secondary">路径：</Typography><Typography sx={{ overflowWrap: 'anywhere' }}>{primaryFile?.path || '暂无'}</Typography>
        </Box>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mt: 2 }}>
          <Button variant="outlined" startIcon={<FolderRoundedIcon/>} onClick={openMovieFolder}>打开目录</Button>
          <Button variant="outlined" disabled={busy} onClick={() => previewNfo('import')}>导入 NFO</Button>
          <Button variant="outlined" disabled={busy} onClick={() => previewNfo('export')}>导出 NFO</Button>
          <Button variant="outlined" disabled={busy} onClick={() => { setOrganizerOpen(true); setOrganizerPreview(undefined) }}>整理文件</Button>
          <Button variant="outlined" startIcon={<SellRoundedIcon/>} onClick={openTags}>编辑我的标签</Button>
          <Button variant="outlined" onClick={openActors}>编辑演员</Button>
          <Button variant="text" color="error" startIcon={<DeleteOutlineRoundedIcon/>} onClick={previewDelete}>从资料库移除</Button>
        </Stack>
      </Box>

      <Divider/>

      <Box>
        <SectionTitle icon={<InfoOutlinedIcon/>} title="元数据状态"/>
        <Stack spacing={1} sx={{ maxWidth: 360 }}>
          {statusRows.map(item => <Box key={item.label} sx={{ display: 'flex', justifyContent: 'space-between', gap: 2 }}>
            <Typography>{item.label}：</Typography>
            <Typography color={item.complete ? 'success.main' : 'warning.main'} sx={{ fontWeight: 850 }}>{item.complete ? '✓' : '缺失'}</Typography>
          </Box>)}
        </Stack>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', mt: 2 }}>
          <Button size="small" startIcon={<RefreshRoundedIcon/>} onClick={refreshStatus}>刷新状态</Button>
          <Button size="small" startIcon={<RefreshRoundedIcon/>} onClick={refreshImages}>刷新图片</Button>
          <Button size="small" onClick={rebuildCache}>重新生成缓存</Button>
          <Button size="small" startIcon={<CameraAltRoundedIcon/>} onClick={openImageFolder}>打开图片目录</Button>
        </Stack>
      </Box>

      <Stack direction="row" spacing={1.25} sx={{ justifyContent: 'space-between' }}>
        <Button startIcon={<NavigateBeforeRoundedIcon/>} disabled={!neighbors.previousId} onClick={() => go(neighbors.previousId)}>上一部</Button>
        <Button endIcon={<NavigateNextRoundedIcon/>} disabled={!neighbors.nextId} onClick={() => go(neighbors.nextId)}>下一部</Button>
      </Stack>
    </Stack>}

    <Dialog open={numberDialog} onClose={() => !busy && setNumberDialog(false)} fullWidth maxWidth="xs">
      <DialogTitle>手动修改番号</DialogTitle>
      <DialogContent><TextField autoFocus fullWidth margin="normal" label="番号" value={manualNumber} onChange={event => setManualNumber(event.target.value)} helperText="只更新数据库标准番号，不修改原始文件名或路径。"/></DialogContent>
      <DialogActions><Button onClick={() => setNumberDialog(false)}>取消</Button><Button variant="contained" disabled={busy || !manualNumber.trim()} onClick={saveManualNumber}>保存番号</Button></DialogActions>
    </Dialog>

    <Dialog open={tagDialog} onClose={() => setTagDialog(false)} fullWidth maxWidth="sm">
      <DialogTitle>选择我的标签</DialogTitle>
      <DialogContent>
        <TextField size="small" fullWidth value={tagSearch} onChange={(event) => setTagSearch(event.target.value)} placeholder="搜索标签" sx={{ mt: 1 }}/>
        <Box sx={{ mt: 1.5 }}>
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', fontWeight: 800, mb: .75 }}>已选择 {selectedTags.length} 个</Typography>
          {selectedTags.length ? <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1 }}>
            {selectedTags.map((tag) => <Button key={tag.id} size="small" variant="contained" onClick={() => toggleSelectedTag(tag)} sx={{ borderRadius: 999, minWidth: 0, px: 1.25, py: .35, textTransform: 'none' }}>
              {tag.name}
              <Box component="span" aria-label={`取消选择 ${tag.name}`} onClick={(event) => { event.stopPropagation(); toggleSelectedTag(tag) }} sx={{ ml: .75, fontWeight: 900, lineHeight: 1 }}>×</Box>
            </Button>)}
          </Box> : <Typography variant="body2" color="text.disabled">还没有选择标签</Typography>}
        </Box>
        <Paper variant="outlined" sx={{ minHeight: 220, maxHeight: 360, overflowY: 'auto', mt: 1.5, p: 1.25, borderRadius: 2 }}>
          <Box sx={{ display: 'flex', flexWrap: 'wrap', gap: 1, alignContent: 'flex-start' }}>
            {filteredTagOptions.map((tag) => {
              const selected = selectedTagIds.has(tag.id)
              return <Button key={tag.id} size="small" variant={selected ? 'contained' : 'outlined'} color={selected ? 'primary' : 'inherit'} onClick={() => toggleSelectedTag(tag)} sx={{ borderRadius: 999, minWidth: 0, px: 1.25, py: .45, textTransform: 'none', borderColor: selected ? 'primary.main' : 'divider', bgcolor: selected ? 'primary.main' : 'transparent', color: selected ? 'primary.contrastText' : 'text.primary', '&:hover': { bgcolor: selected ? 'primary.dark' : 'action.hover', borderColor: 'primary.main' } }}>
                {selected ? `✓ ${tag.name}` : tag.name}
              </Button>
            })}
            {!tagOptions.length && <Typography variant="body2" color="text.disabled">暂无自定义标签</Typography>}
            {tagOptions.length > 0 && !filteredTagOptions.length && <Typography variant="body2" color="text.disabled">没有匹配的标签</Typography>}
          </Box>
        </Paper>
      </DialogContent>
      <DialogActions><Button onClick={() => setTagDialog(false)}>取消</Button><Button variant="contained" onClick={saveTags}>保存</Button></DialogActions>
    </Dialog>

    <Dialog open={actorDialog} onClose={() => setActorDialog(false)} fullWidth maxWidth="sm">
      <DialogTitle>编辑演员关系</DialogTitle>
      <DialogContent>
        <Autocomplete multiple filterOptions={(options) => options} options={actorOptions} value={selectedActors} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onInputChange={(_, value) => setActorSearch(value)} onChange={(_, value) => setSelectedActors(value)} renderInput={(params) => <TextField {...params} autoFocus label="搜索并选择演员" margin="normal"/>}/>
      </DialogContent>
      <DialogActions><Button onClick={() => setActorDialog(false)}>取消</Button><Button variant="contained" onClick={saveActors}>保存</Button></DialogActions>
    </Dialog>

    <SafeDeleteDialog preview={deletePreview} command={deleteCommand} onClose={closeDeleteDialog} onLaunched={handleDeleteLaunched}/>

    <Dialog open={Boolean(nfoPreview)} onClose={() => setNfoPreview(undefined)} maxWidth="sm" fullWidth>
      <DialogTitle>{nfoMode === 'import' ? '导入 NFO 预览' : '导出 NFO 预览'}</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ overflowWrap: 'anywhere' }}>{nfoPreview?.path}</DialogContentText>
        {nfoPreview?.changes.length ? <Alert severity="info" sx={{ mt: 2 }}>将处理：{nfoPreview.changes.join('、')}</Alert> : null}
        {nfoPreview?.conflicts.length ? <Alert severity="warning" sx={{ mt: 1 }}>冲突字段保持原值：{nfoPreview.conflicts.join('、')}</Alert> : null}
        {nfoPreview?.warnings.map((warning) => <Alert key={warning} severity="warning" sx={{ mt: 1 }}>{warning}</Alert>)}
      </DialogContent>
      <DialogActions>
        <Button onClick={() => setNfoPreview(undefined)}>取消</Button>
        {nfoMode === 'export' && nfoPreview && !nfoPreview.canApply && <Button variant="outlined" onClick={() => confirmNfo(true)}>另存为 .lmm.nfo</Button>}
        <Button variant="contained" disabled={busy || Boolean(nfoPreview && !nfoPreview.canApply)} onClick={() => confirmNfo()}>{nfoMode === 'import' ? '确认导入' : '确认导出'}</Button>
      </DialogActions>
    </Dialog>

    <Dialog open={organizerOpen} onClose={() => !busy && setOrganizerOpen(false)} maxWidth="md" fullWidth>
      <DialogTitle>整理文件</DialogTitle>
      <DialogContent>
        <Stack spacing={2} sx={{ mt: 1 }}>
          <TextField label="文件名模板" value={organizerTemplate} onChange={event => { setOrganizerTemplate(event.target.value); setOrganizerPreview(undefined) }} helperText="支持 {Code}、{Title}、{Year}、{Actors}"/>
          <TextField label="目标目录" value={organizerDestination} onChange={event => { setOrganizerDestination(event.target.value); setOrganizerPreview(undefined) }} helperText="留空时只在原目录重命名；不会覆盖任何已有目标。"/>
          {organizerPreview && <>
            <Alert severity={organizerPreview.conflictItems ? 'error' : 'success'}>Dry Run：{organizerPreview.validItems} 项可执行，{organizerPreview.conflictItems} 项冲突。尚未修改文件。</Alert>
            {organizerPreview.items.map(item => <Paper key={item.mediaFileId} variant="outlined" sx={{ p: 1.5 }}>
              <Typography variant="caption" color="text.secondary">原路径</Typography>
              <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{item.sourcePath}</Typography>
              <Typography variant="caption" color="text.secondary">目标路径</Typography>
              <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{item.destinationPath}</Typography>
              {item.conflict && <Alert severity="error" sx={{ mt: 1 }}>{item.conflict}</Alert>}
            </Paper>)}
          </>}
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={() => setOrganizerOpen(false)}>取消</Button><Button variant="outlined" disabled={busy} onClick={dryRunOrganizer}>Dry Run</Button><Button variant="contained" disabled={busy || !organizerPreview || organizerPreview.conflictItems > 0} onClick={executeOrganizer}>确认并进入任务</Button></DialogActions>
    </Dialog>

    <Dialog open={Boolean(currentViewer)} onClose={() => setViewerItems([])} maxWidth="lg" fullWidth>
      <DialogTitle>{imageTab === 'stills' ? '剧照' : '截图'} {viewerItems.length ? `${viewerIndex + 1} / ${viewerItems.length}` : ''}</DialogTitle>
      <DialogContent onWheel={event => { event.preventDefault(); setZoom(value => Math.max(.4, Math.min(4, value + (event.deltaY < 0 ? .15 : -.15)))) }} sx={{ height: '72vh', display: 'grid', placeItems: 'center', overflow: 'auto', bgcolor: 'background.default' }}>
        {currentViewer?.url && <Box component="img" ref={viewerImageRef} src={currentViewer.url} alt={currentViewer.type} onDoubleClick={enterFullscreen} sx={{ maxWidth: zoom === 1 ? '100%' : 'none', maxHeight: zoom === 1 ? '100%' : 'none', transform: `scale(${zoom})`, transformOrigin: 'center', transition: 'transform .12s ease' }}/>}
      </DialogContent>
      <DialogActions>
        <Button startIcon={<NavigateBeforeRoundedIcon/>} disabled={viewerIndex <= 0} onClick={() => setViewerIndex(value => Math.max(0, value - 1))}>上一张</Button>
        <Button endIcon={<NavigateNextRoundedIcon/>} disabled={viewerIndex >= viewerItems.length - 1} onClick={() => setViewerIndex(value => Math.min(viewerItems.length - 1, value + 1))}>下一张</Button>
        <Button startIcon={<ZoomOutRoundedIcon/>} onClick={() => setZoom(value => Math.max(.4, value - .25))}>缩小</Button>
        <Button startIcon={<ZoomInRoundedIcon/>} onClick={() => setZoom(value => Math.min(4, value + .25))}>放大</Button>
        <Button onClick={() => setZoom(1)}>适应窗口</Button>
        <Button onClick={enterFullscreen}>全屏</Button>
        <Button onClick={() => setViewerItems([])}>关闭</Button>
      </DialogActions>
    </Dialog>

    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
