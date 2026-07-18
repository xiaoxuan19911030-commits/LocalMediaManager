import FavoriteBorderRoundedIcon from '@mui/icons-material/FavoriteBorderRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import StarRoundedIcon from '@mui/icons-material/StarRounded'
import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import { Autocomplete, Button, Collapse, Dialog, DialogActions, DialogContent, DialogTitle, Divider, InputAdornment, ListItemIcon, Menu, MenuItem, Snackbar, Stack, TextField, Typography } from '@mui/material'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent } from 'react'
import { useSearchParams } from 'react-router'
import { MovieResultContainer, useMovieActions } from '@/components/workspace/MovieResults'
import { SafeDeleteDialog } from '@/components/SafeDeleteDialog'
import { ViewModeToggle, WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { bridge } from '@/services/bridge'
import type { MediaItem, MediaLibrary, NamedItem, SafeDeletePreview, SafeDeletePreviewCommand } from '@/types/media'
import type { WorkspaceViewMode } from '@/components/workspace/Workspace'

const pageSize = 24
const mediaStateKey = 'lmm.mediaPage.filters'

const normalizeSearch = (value: string) => value.replace(/\u3000/g, ' ').replace(/\s+/g, ' ').trim()

interface SavedMediaState {
  page?: number
  search?: string
  query?: string
  sort?: string
  rating?: string
  tagId?: number
  metadataStatus?: string
  imageStatus?: string
  libraryId?: number
  categorySignature?: string
  moreOpen?: boolean
  view?: WorkspaceViewMode
  scrollY?: number
  restoreScroll?: boolean
  restoreSignature?: string
}

const readSavedState = (): SavedMediaState => {
  try {
    return JSON.parse(window.sessionStorage.getItem(mediaStateKey) || '{}') as SavedMediaState
  } catch {
    return {}
  }
}

const numericParam = (params: URLSearchParams, name: string) => {
  const value = Number(params.get(name) || 0)
  return Number.isFinite(value) && value > 0 ? value : undefined
}

export default function MediaPage() {
  const [params] = useSearchParams()
  const category = useMemo(() => {
    const actorId = numericParam(params, 'actorId')
    const directorId = numericParam(params, 'directorId')
    const movieTagId = numericParam(params, 'movieTagId')
    const customTagId = numericParam(params, 'customTagId')
    const seriesId = numericParam(params, 'seriesId')
    const label = actorId ? `演员：${params.get('actorName') || actorId}` :
      directorId ? `导演：${params.get('directorName') || directorId}` :
      movieTagId ? `影片标签：${params.get('movieTagName') || movieTagId}` :
      customTagId ? `自定义标签：${params.get('customTagName') || customTagId}` :
      seriesId ? `系列：${params.get('seriesName') || seriesId}` : ''
    return { actorId, directorId, movieTagId, customTagId, seriesId, label }
  }, [params])
  const categorySignature = useMemo(() => JSON.stringify({ actorId: category.actorId, directorId: category.directorId, movieTagId: category.movieTagId, customTagId: category.customTagId, seriesId: category.seriesId }), [category.actorId, category.customTagId, category.directorId, category.movieTagId, category.seriesId])
  const saved = useMemo(readSavedState, [])
  const canReuseSavedState = saved.categorySignature === categorySignature
  const [items, setItems] = useState<MediaItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(canReuseSavedState ? saved.page ?? 1 : 1)
  const [search, setSearch] = useState(canReuseSavedState ? saved.search ?? '' : '')
  const [query, setQuery] = useState(canReuseSavedState ? saved.query ?? '' : '')
  const [sort, setSort] = useState(canReuseSavedState ? saved.sort ?? 'newest' : 'newest')
  const [rating, setRating] = useState(canReuseSavedState && saved.rating && saved.rating !== '0' ? String(saved.rating) : 'all')
  const [tagId, setTagId] = useState(canReuseSavedState ? saved.tagId ?? 0 : 0)
  const [metadataStatus, setMetadataStatus] = useState(canReuseSavedState ? saved.metadataStatus ?? 'all' : 'all')
  const [imageStatus, setImageStatus] = useState(canReuseSavedState ? saved.imageStatus ?? 'all' : 'all')
  const [libraryId, setLibraryId] = useState(canReuseSavedState ? saved.libraryId ?? 0 : 0)
  const [moreOpen, setMoreOpen] = useState(canReuseSavedState ? saved.moreOpen ?? true : true)
  const [view, setView] = useState<WorkspaceViewMode>(canReuseSavedState ? saved.view ?? 'grid' : 'grid')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [batchRating, setBatchRating] = useState('5')
  const [ratingDialog, setRatingDialog] = useState(false)
  const [selected, setSelected] = useState<number[]>([])
  const [tagDialog, setTagDialog] = useState(false)
  const [tags, setTags] = useState<NamedItem[]>([])
  const [libraries, setLibraries] = useState<MediaLibrary[]>([])
  const [addTags, setAddTags] = useState<NamedItem[]>([])
  const [removeTags, setRemoveTags] = useState<NamedItem[]>([])
  const [contextMenu, setContextMenu] = useState<{ mouseX: number; mouseY: number; item: MediaItem }>()
  const [deletePreview, setDeletePreview] = useState<SafeDeletePreview>()
  const [deleteCommand, setDeleteCommand] = useState<SafeDeletePreviewCommand>()
  const [editMode, setEditMode] = useState(false)
  const [subMenu, setSubMenu] = useState<{ kind: 'edit' | 'image' | 'open'; anchor: HTMLElement }>()
  const loadSeq = useRef(0)
  const movieActions = useMovieActions({ onNotice: setNotice, play: bridge.play })
  const stateSignature = useMemo(() => JSON.stringify({ categorySignature, page, query, sort, rating, tagId, metadataStatus, imageStatus, libraryId, view }), [categorySignature, imageStatus, libraryId, metadataStatus, page, query, rating, sort, tagId, view])
  const shouldRestoreScroll = useRef(saved.restoreScroll === true && saved.restoreSignature === stateSignature)
  const pendingScrollY = useRef(typeof saved.scrollY === 'number' ? saved.scrollY : 0)
  const buildSavedState = useCallback((): SavedMediaState => ({ page, search, query, sort, rating, tagId, metadataStatus, imageStatus, libraryId, categorySignature, moreOpen, view }), [categorySignature, imageStatus, libraryId, metadataStatus, moreOpen, page, query, rating, search, sort, tagId, view])

  const load = useCallback(() => {
    const seq = ++loadSeq.current
    setLoading(true); setError('')
    const effectiveMetadataStatus = imageStatus === 'missing' ? 'missing-images' : imageStatus === 'normal' ? 'complete' : metadataStatus
    bridge.advancedSearch({
      query,
      actorId: category.actorId,
      directorId: category.directorId,
      movieTagId: category.movieTagId,
      customTagId: category.customTagId,
      seriesId: category.seriesId,
      ratingFilter: rating,
      tagId: tagId || undefined,
      metadataStatus: effectiveMetadataStatus,
      libraryId: libraryId || undefined,
      sort,
      limit: pageSize,
      offset: (page - 1) * pageSize,
    })
      .then((result) => { if (seq === loadSeq.current) { setItems(result.items); setTotal(result.total) } })
      .catch((reason: Error) => { if (seq === loadSeq.current) setError(reason.message) })
      .finally(() => { if (seq === loadSeq.current) setLoading(false) })
  }, [category.actorId, category.customTagId, category.directorId, category.movieTagId, category.seriesId, imageStatus, libraryId, metadataStatus, page, query, rating, sort, tagId])

  useEffect(load, [load])
  useEffect(() => () => { loadSeq.current += 1 }, [])
  useEffect(() => {
    bridge.libraries().then(setLibraries).catch(() => undefined)
    bridge.entities('tags', '', 'name', 96, 0).then((result) => setTags(result.items)).catch(() => undefined)
  }, [])
  useEffect(() => {
    const next = buildSavedState()
    if (shouldRestoreScroll.current && saved.restoreSignature === stateSignature) {
      window.sessionStorage.setItem(mediaStateKey, JSON.stringify({ ...next, scrollY: pendingScrollY.current, restoreScroll: true, restoreSignature: stateSignature }))
      return
    }
    shouldRestoreScroll.current = false
    window.sessionStorage.setItem(mediaStateKey, JSON.stringify(next))
  }, [buildSavedState, saved.restoreSignature, stateSignature])
  useLayoutEffect(() => {
    if (!shouldRestoreScroll.current || loading || error) return
    shouldRestoreScroll.current = false
    window.scrollTo(0, pendingScrollY.current)
    window.sessionStorage.setItem(mediaStateKey, JSON.stringify(buildSavedState()))
  }, [buildSavedState, error, items.length, loading, total])
  useEffect(() => {
    const next = normalizeSearch(search)
    if (next === query) return
    const timer = window.setTimeout(() => {
      setPage(1)
      setQuery(next)
    }, 300)
    return () => window.clearTimeout(timer)
  }, [query, search])

  const activeFilterCount = useMemo(() => [
    query,
    sort !== 'newest',
    rating !== 'all',
    tagId > 0,
    metadataStatus !== 'all',
    imageStatus !== 'all',
    libraryId > 0,
    category.actorId,
    category.directorId,
    category.movieTagId,
    category.customTagId,
    category.seriesId,
  ].filter(Boolean).length, [category.actorId, category.customTagId, category.directorId, category.movieTagId, category.seriesId, imageStatus, libraryId, metadataStatus, query, rating, sort, tagId])

  const submitSearch = () => { setPage(1); setQuery(normalizeSearch(search)) }
  const clearSearch = () => { setSearch(''); setQuery(''); setPage(1) }
  const clearFilters = () => {
    setSort('newest'); setRating('all')
    setTagId(0); setMetadataStatus('all'); setImageStatus('all'); setLibraryId(0); setPage(1)
  }
  const handleSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      submitSearch()
    } else if (event.key === 'Escape') {
      if (search) clearSearch()
      else event.currentTarget.blur()
    }
  }
  const batchFavorite = (value: boolean) => bridge.setBatchFavorite(selected, value).then((result) => { setNotice(result.message); setSelected([]); load() }).catch((reason: Error) => setNotice(reason.message))
  const openBatchTags = () => { setAddTags([]); setRemoveTags([]); setTagDialog(true); bridge.entities('tags', '', 'name', 96, 0).then((result) => setTags(result.items)).catch((reason: Error) => setNotice(reason.message)) }
  const saveBatchTags = () => bridge.updateBatchTags(selected, addTags.map((item) => item.id), removeTags.map((item) => item.id)).then((result) => { setNotice(result.message); setTagDialog(false); setSelected([]); load() }).catch((reason: Error) => setNotice(reason.message))
  const saveBatchRating = () => bridge.setBatchRating(selected, batchRating === '' ? undefined : Number(batchRating), batchRating === '').then((result) => { setNotice(result.message); setRatingDialog(false); setSelected([]); load() }).catch((reason: Error) => setNotice(reason.message))
  const createBatchSync = () => bridge.createBatchSync(selected).then((result) => { setNotice(result.message); setSelected([]) }).catch((reason: Error) => setNotice(reason.message))
  const openContextMenu = (event: MouseEvent, item: MediaItem) => { event.preventDefault(); setContextMenu({ mouseX: event.clientX + 2, mouseY: event.clientY - 6, item }) }
  const closeContextMenu = () => { setContextMenu(undefined); setSubMenu(undefined) }
  const syncContextMovie = () => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.syncMovie(item.dataId).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message)) }
  const generateContextImage = (type: string) => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.generateMovieImage(item.dataId, type).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message)) }
  const createBatchImageTasks = (type: string) => Promise.all(selected.map((id) => bridge.generateMovieImage(id, type))).then((results) => { setNotice(`已创建 ${results.length} 个${type === 'GIF' ? ' GIF' : '截图'}任务，可在任务中心查看进度。`); setSelected([]) }).catch((reason: Error) => setNotice(reason.message))
  const rememberScrollForDetail = useCallback(() => {
    window.sessionStorage.setItem(mediaStateKey, JSON.stringify({ ...buildSavedState(), scrollY: window.scrollY, restoreScroll: true, restoreSignature: stateSignature }))
  }, [buildSavedState, stateSignature])
  const openMovieFromWall = (item: MediaItem) => { rememberScrollForDetail(); movieActions.openMovie(item, { source: 'media', search: query, sort }) }
  const openContextMovie = () => { const item = contextMenu?.item; closeContextMenu(); if (item) openMovieFromWall(item) }
  const openContextLocation = () => { const item = contextMenu?.item; closeContextMenu(); if (item?.path) bridge.revealFile(item.path).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message)); else setNotice('没有可定位的影片文件') }
  const openSafeDelete = (movieIds: number[], mode: 'metadata' | 'media') => {
    closeContextMenu()
    const command = { movieIds, mode, deleteDatabaseInfo: true } satisfies SafeDeletePreviewCommand
    setDeleteCommand(command)
    bridge.previewSafeDelete(command).then(setDeletePreview).catch((reason: Error) => setNotice(reason.message))
  }
  const previewDeleteContextMovie = () => { const item = contextMenu?.item; if (item) openSafeDelete([item.dataId], 'metadata') }
  const previewDeleteContextMedia = () => { const item = contextMenu?.item; if (item) openSafeDelete([item.dataId], 'media') }
  const previewBatchDelete = (mode: 'metadata' | 'media') => selected.length && openSafeDelete(selected, mode)
  const closeDeleteDialog = () => { setDeletePreview(undefined); setDeleteCommand(undefined) }
  const handleDeleteLaunched = (result: { message: string }) => { setNotice(`${result.message} 可在任务中心查看结果。`); closeDeleteDialog(); setSelected([]); void load() }
  const enterEditMode = () => { setEditMode(true); setSelected([]) }
  const exitEditMode = () => { setEditMode(false); setSelected([]); closeContextMenu() }
  const toggleSelect = (item: MediaItem) => setSelected((current) => current.includes(item.dataId) ? current.filter((id) => id !== item.dataId) : [...current, item.dataId])
  const selectCurrentPage = () => setSelected(items.map((item) => item.dataId))

  const filters = <Stack component="form" onSubmit={(event) => { event.preventDefault(); submitSearch() }} spacing={1.25}>
    <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', md: 'center' }, flexWrap: 'wrap' }}>
      <TextField size="small" value={search} onChange={(event) => setSearch(event.target.value)} onKeyDown={handleSearchKeyDown} placeholder="搜索标题、番号、演员、标签，或输入“评分>=4 收藏”" sx={{ flex: '1 1 280px' }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon/></InputAdornment> } }}/>
      <TextField select size="small" label="排序" value={sort} onChange={(event) => { setPage(1); setSort(event.target.value) }} sx={{ width: 150 }}>
        <MenuItem value="newest">最新导入</MenuItem><MenuItem value="code">番号</MenuItem><MenuItem value="title">标题</MenuItem><MenuItem value="release">发行日期</MenuItem><MenuItem value="rating">个人评分</MenuItem>
      </TextField>
      <Button type="submit" variant="contained">搜索</Button>
      {!editMode ? <Button variant="outlined" startIcon={<EditRoundedIcon/>} onClick={enterEditMode}>编辑</Button> : <><Button variant="outlined" onClick={selectCurrentPage}>全选当前页</Button><Typography variant="body2" color="text.secondary">已选择 {selected.length} 部</Typography><Button variant="contained" onClick={exitEditMode}>完成</Button></>}
      <Button variant="outlined" onClick={() => void load()}>刷新</Button>
      <ViewModeToggle value={view} onChange={setView}/>
      <Button color="inherit" onClick={() => setMoreOpen((value) => !value)}>{moreOpen ? '收起筛选' : '更多筛选'}</Button>
    </Stack>
    <Collapse in={moreOpen} unmountOnExit={false}>
      <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', lg: 'center' }, flexWrap: 'wrap' }}>
        <TextField select size="small" label="评分" value={rating} onChange={(event) => { setPage(1); setRating(event.target.value) }} sx={{ minWidth: 130 }}>
          <MenuItem value="all">全部评分</MenuItem><MenuItem value="unrated">未评分</MenuItem>{[5, 4, 3, 2, 1].map((value) => <MenuItem key={value} value={String(value)}>{'★'.repeat(value)}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="自定义标签" value={tagId} onChange={(event) => { setPage(1); setTagId(Number(event.target.value)) }} sx={{ minWidth: 150 }}>
          <MenuItem value={0}>全部自定义标签</MenuItem>{tags.map((tag) => <MenuItem key={tag.id} value={tag.id}>{tag.name}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="元数据状态" value={metadataStatus} onChange={(event) => { setPage(1); setMetadataStatus(event.target.value) }} sx={{ minWidth: 150 }}>
          <MenuItem value="all">全部</MenuItem><MenuItem value="complete">已完整</MenuItem><MenuItem value="unscraped">未刮削</MenuItem><MenuItem value="missing-nfo">缺 NFO</MenuItem><MenuItem value="missing-actors">缺演员</MenuItem><MenuItem value="missing-tags">缺标签</MenuItem><MenuItem value="missing-description">缺简介</MenuItem>
        </TextField>
        <TextField select size="small" label="图片状态" value={imageStatus} onChange={(event) => { setPage(1); setImageStatus(event.target.value) }} sx={{ minWidth: 130 }}>
          <MenuItem value="all">全部图片</MenuItem><MenuItem value="missing">缺少图片</MenuItem><MenuItem value="normal">图片正常</MenuItem>
        </TextField>
        <TextField select size="small" label="媒体库" value={libraryId} onChange={(event) => { setPage(1); setLibraryId(Number(event.target.value)) }} sx={{ minWidth: 150 }}>
          <MenuItem value={0}>全部媒体库</MenuItem>{libraries.map((library) => <MenuItem key={library.id} value={library.id}>{library.name}</MenuItem>)}
        </TextField>
      </Stack>
    </Collapse>
  </Stack>

  const stats = <Stack spacing={1}>
    {category.label && <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}><StatusBadge tone="info" label={category.label}/></Stack>}
    {editMode && selected.length > 0 && <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', p: 1.25, border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: 'action.hover' }}>
    <Typography sx={{ fontWeight: 800 }}>已选择 {selected.length} 部</Typography>
    <Button size="small" startIcon={<FavoriteRoundedIcon/>} onClick={() => batchFavorite(true)}>收藏</Button>
    <Button size="small" startIcon={<FavoriteBorderRoundedIcon/>} onClick={() => batchFavorite(false)}>取消收藏</Button>
    <Button size="small" onClick={openBatchTags}>批量标签</Button>
    <TextField select size="small" label="评分" value={batchRating} onChange={(event) => setBatchRating(event.target.value)} sx={{ width: 116 }}>
      <MenuItem value="">清除</MenuItem>{[0, 1, 2, 3, 4, 5].map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}
    </TextField>
    <Button size="small" startIcon={<StarRoundedIcon/>} onClick={() => setRatingDialog(true)}>应用评分</Button>
    <Button size="small" startIcon={<SyncRoundedIcon/>} onClick={createBatchSync}>同步信息</Button>
    <Button size="small" color="inherit" onClick={() => setSelected([])}>取消选择</Button>
    </Stack>}
  </Stack>

  return <WorkspacePage title="影片墙" description={`共 ${total} 部影片，支持搜索、排序、筛选和分页浏览。`} stats={stats} filters={filters} activeFilterCount={activeFilterCount} onClearFilters={clearFilters} loading={loading} error={error}
    primaryActions={[refreshAction(load)]}>
    <MovieResultContainer items={items} total={total} page={page} pageSize={pageSize} onPageChange={setPage} view={view} selectable={editMode} selectedIds={selected} onSelect={(value, checked) => setSelected((current) => checked ? [...current, value.dataId] : current.filter((id) => id !== value.dataId))} onRatingClick={editMode && selected.length > 0 ? () => setRatingDialog(true) : undefined} onContextMenu={openContextMenu} onPlay={movieActions.playMovie} onOpen={(item) => editMode ? toggleSelect(item) : openMovieFromWall(item)} emptyTitle="暂无影片" emptyDescription="当前媒体库还没有可展示的影片。"/>
    <Menu open={Boolean(contextMenu)} onClose={closeContextMenu} anchorReference="anchorPosition" anchorPosition={contextMenu ? { top: contextMenu.mouseY, left: contextMenu.mouseX } : undefined}>
      {editMode && selected.length > 0 ? [
        <MenuItem key="batch-sync" onClick={() => { closeContextMenu(); void createBatchSync() }}><ListItemIcon><SyncRoundedIcon fontSize="small"/></ListItemIcon>全部同步信息</MenuItem>,
        <Divider key="batch-divider-1"/>,
        <MenuItem key="batch-screenshot" onClick={() => { closeContextMenu(); void createBatchImageTasks('Screenshot') }}>批量生成截图</MenuItem>,
        <MenuItem key="batch-gif" onClick={() => { closeContextMenu(); void createBatchImageTasks('GIF') }}>批量生成 GIF</MenuItem>,
        <Divider key="batch-divider-2"/>,
        <MenuItem key="batch-delete-info" onClick={() => previewBatchDelete('metadata')}>删除信息</MenuItem>,
        <MenuItem key="batch-delete-file" onClick={() => previewBatchDelete('media')}>删除影片</MenuItem>,
      ] : [
        <MenuItem key="sync" onClick={syncContextMovie}><ListItemIcon><SyncRoundedIcon fontSize="small"/></ListItemIcon>同步信息</MenuItem>,
        <Divider key="divider-1"/>,
        <MenuItem key="edit" onMouseEnter={(event) => setSubMenu({ kind: 'edit', anchor: event.currentTarget })} onClick={(event) => setSubMenu({ kind: 'edit', anchor: event.currentTarget })}><ListItemIcon><EditRoundedIcon fontSize="small"/></ListItemIcon>编辑</MenuItem>,
        <Divider key="divider-2"/>,
        <MenuItem key="image" onMouseEnter={(event) => setSubMenu({ kind: 'image', anchor: event.currentTarget })} onClick={(event) => setSubMenu({ kind: 'image', anchor: event.currentTarget })}>图片</MenuItem>,
        <Divider key="divider-3"/>,
        <MenuItem key="location" onMouseEnter={(event) => setSubMenu({ kind: 'open', anchor: event.currentTarget })} onClick={(event) => setSubMenu({ kind: 'open', anchor: event.currentTarget })}><ListItemIcon><FolderRoundedIcon fontSize="small"/></ListItemIcon>打开位置</MenuItem>,
        <Divider key="divider"/>,
        <MenuItem key="delete-info" onClick={previewDeleteContextMovie}><ListItemIcon><DeleteOutlineRoundedIcon fontSize="small"/></ListItemIcon>删除信息</MenuItem>,
        <MenuItem key="delete-file" onClick={previewDeleteContextMedia}>删除影片</MenuItem>,
      ]}
    </Menu>
    <Menu open={Boolean(subMenu)} anchorEl={subMenu?.anchor} onClose={() => setSubMenu(undefined)} anchorOrigin={{ vertical: 'top', horizontal: 'right' }} transformOrigin={{ vertical: 'top', horizontal: 'left' }}>
      {subMenu?.kind === 'edit' && [<MenuItem key="info" onClick={openContextMovie}>编辑信息</MenuItem>, <MenuItem key="recognize" disabled>重新识别主画面（开发中：缺少智能卡图 Runner）</MenuItem>, <MenuItem key="left" disabled>居左裁切（开发中：缺少裁切预览）</MenuItem>, <MenuItem key="center" disabled>居中裁切（开发中：缺少裁切预览）</MenuItem>, <MenuItem key="right" disabled>居右裁切（开发中：缺少裁切预览）</MenuItem>]}
      {subMenu?.kind === 'image' && [<MenuItem key="poster" onClick={() => generateContextImage('Poster')}>生成封面</MenuItem>, <MenuItem key="preview" onClick={() => generateContextImage('Preview')}>生成预览图</MenuItem>, <MenuItem key="screenshot" onClick={() => generateContextImage('Screenshot')}>生成截图</MenuItem>, <MenuItem key="gif" onClick={() => generateContextImage('GIF')}>生成 GIF</MenuItem>]}
      {subMenu?.kind === 'open' && [<MenuItem key="movie" disabled={!contextMenu?.item.path} onClick={openContextLocation}>影片{contextMenu?.item.path ? '' : '（无文件路径）'}</MenuItem>, <MenuItem key="poster" disabled>海报（请在详情页图片资源中打开）</MenuItem>, <MenuItem key="preview" disabled>预览图（请在详情页图片资源中打开）</MenuItem>, <MenuItem key="thumb" disabled>缩略图（请在详情页图片资源中打开）</MenuItem>, <MenuItem key="screenshot" disabled>截图（尚无可定位资源）</MenuItem>, <MenuItem key="gif" disabled>GIF（尚无可定位资源）</MenuItem>]}
    </Menu>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
    <Dialog open={ratingDialog} onClose={() => setRatingDialog(false)} fullWidth maxWidth="xs"><DialogTitle>批量评分</DialogTitle><DialogContent><TextField select fullWidth margin="normal" label="评分" value={batchRating} onChange={(event) => setBatchRating(event.target.value)}><MenuItem value="">清除评分</MenuItem>{[0, 1, 2, 3, 4, 5].map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField></DialogContent><DialogActions><Button onClick={() => setRatingDialog(false)}>取消</Button><Button variant="contained" onClick={saveBatchRating}>应用到 {selected.length} 部影片</Button></DialogActions></Dialog>
    <Dialog open={tagDialog} onClose={() => setTagDialog(false)} fullWidth maxWidth="sm"><DialogTitle>批量编辑标签</DialogTitle><DialogContent><Autocomplete multiple options={tags.filter((tag) => !removeTags.some((item) => item.id === tag.id))} value={addTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setAddTags(value)} renderInput={(params) => <TextField {...params} label="添加标签" margin="normal"/>}/><Autocomplete multiple options={tags.filter((tag) => !addTags.some((item) => item.id === tag.id))} value={removeTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setRemoveTags(value)} renderInput={(params) => <TextField {...params} label="解绑标签" margin="normal" helperText="只解除关系，不删除标签。"/>}/></DialogContent><DialogActions><Button onClick={() => setTagDialog(false)}>取消</Button><Button variant="contained" disabled={!addTags.length && !removeTags.length} onClick={saveBatchTags}>应用到 {selected.length} 部影片</Button></DialogActions></Dialog>
    <SafeDeleteDialog preview={deletePreview} command={deleteCommand} onClose={closeDeleteDialog} onLaunched={handleDeleteLaunched}/>
  </WorkspacePage>
}
