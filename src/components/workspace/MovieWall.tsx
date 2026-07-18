import ExpandLessRoundedIcon from '@mui/icons-material/ExpandLessRounded'
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import { Button, Collapse, InputAdornment, MenuItem, Snackbar, Stack, TextField } from '@mui/material'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react'
import { MovieResultContainer, useMovieActions } from '@/components/workspace/MovieResults'
import { ViewModeToggle, WorkspacePage, refreshAction, type WorkspaceAction, type WorkspaceViewMode } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { AdvancedSearchFilters, MediaItem, MediaLibrary } from '@/types/media'

const normalizeSearch = (value: string) => value.replace(/\u3000/g, ' ').replace(/\s+/g, ' ').trim()
const fieldSx = { width: { xs: '100%', sm: 164 } }

export interface MovieWallDefaults {
  actorId?: number
  directorId?: number
  movieTagId?: number
  customTagId?: number
  seriesId?: number
  libraryId?: number
  favorite?: boolean
  watched?: boolean
}

interface SavedMovieWallState {
  page?: number
  search?: string
  query?: string
  sort?: string
  rating?: string
  metadataStatus?: string
  imageStatus?: string
  libraryId?: number
  moreOpen?: boolean
  view?: WorkspaceViewMode
  defaultsSignature?: string
  scrollY?: number
  restoreScroll?: boolean
  restoreSignature?: string
}

export interface MovieWallRenderContext {
  total: number
  items: MediaItem[]
  loading: boolean
  query: string
  reload: () => void
}

export function MovieWall({
  title,
  description,
  stateKey,
  defaults = {},
  defaultLabel,
  initialSearch = '',
  emptyTitle = '暂无影片',
  emptyDescription = '当前条件下没有可展示的影片。',
  pageSize = 24,
  selectable,
  selectedIds = [],
  onSelect,
  onRatingClick,
  onContextMenu,
  onOpenItem,
  renderStats,
  primaryActions,
  childrenAfterResults,
  reloadSignal = 0,
}: {
  title: string
  description?: (total: number) => string
  stateKey: string
  defaults?: MovieWallDefaults
  defaultLabel?: ReactNode
  initialSearch?: string
  emptyTitle?: string
  emptyDescription?: string
  pageSize?: number
  selectable?: boolean
  selectedIds?: number[]
  onSelect?: (item: MediaItem, selected: boolean) => void
  onRatingClick?: (item: MediaItem) => void
  onContextMenu?: (event: MouseEvent, item: MediaItem) => void
  onOpenItem?: (item: MediaItem, openDefault: () => void) => void
  renderStats?: (context: MovieWallRenderContext) => ReactNode
  primaryActions?: (context: MovieWallRenderContext) => WorkspaceAction[]
  childrenAfterResults?: (context: MovieWallRenderContext) => ReactNode
  reloadSignal?: number
}) {
  const saved = useMemo(() => readSavedState(stateKey), [stateKey])
  const defaultsSignature = useMemo(() => JSON.stringify(defaults), [defaults])
  const canReuseSavedState = saved.defaultsSignature === defaultsSignature
  const [items, setItems] = useState<MediaItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(canReuseSavedState ? saved.page ?? 1 : 1)
  const [search, setSearch] = useState(canReuseSavedState ? saved.search ?? initialSearch : initialSearch)
  const [query, setQuery] = useState(canReuseSavedState ? saved.query ?? normalizeSearch(initialSearch) : normalizeSearch(initialSearch))
  const [sort, setSort] = useState(canReuseSavedState ? saved.sort ?? 'newest' : 'newest')
  const [rating, setRating] = useState(canReuseSavedState ? saved.rating ?? 'all' : 'all')
  const [metadataStatus, setMetadataStatus] = useState(canReuseSavedState ? saved.metadataStatus ?? 'all' : 'all')
  const [imageStatus, setImageStatus] = useState(canReuseSavedState ? saved.imageStatus ?? 'all' : 'all')
  const [libraryId, setLibraryId] = useState(canReuseSavedState ? saved.libraryId ?? 0 : defaults.libraryId ?? 0)
  const [moreOpen, setMoreOpen] = useState(canReuseSavedState ? saved.moreOpen ?? false : false)
  const [view, setView] = useState<WorkspaceViewMode>(canReuseSavedState ? saved.view ?? 'grid' : 'grid')
  const [libraries, setLibraries] = useState<MediaLibrary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const loadSeq = useRef(0)
  const movieActions = useMovieActions({ onNotice: setNotice, play: bridge.play })
  const stateSignature = useMemo(() => JSON.stringify({ defaultsSignature, page, query, sort, rating, metadataStatus, imageStatus, libraryId, view }), [defaultsSignature, imageStatus, libraryId, metadataStatus, page, query, rating, sort, view])
  const shouldRestoreScroll = useRef(saved.restoreScroll === true && saved.restoreSignature === stateSignature)
  const pendingScrollY = useRef(typeof saved.scrollY === 'number' ? saved.scrollY : 0)
  const buildSavedState = useCallback((): SavedMovieWallState => ({ page, search, query, sort, rating, metadataStatus, imageStatus, libraryId, defaultsSignature, moreOpen, view }), [defaultsSignature, imageStatus, libraryId, metadataStatus, moreOpen, page, query, rating, search, sort, view])
  const filterDirty = rating !== 'all' || metadataStatus !== 'all' || imageStatus !== 'all' || libraryId !== (defaults.libraryId ?? 0)
  const activeFilterCount = (query ? 1 : 0) + (sort !== 'newest' ? 1 : 0) + (filterDirty ? 1 : 0)

  const load = useCallback(() => {
    const seq = ++loadSeq.current
    setLoading(true); setError('')
    const effectiveMetadataStatus = imageStatus === 'missing' ? 'missing-images' : imageStatus === 'normal' ? 'complete' : metadataStatus
    const filters: AdvancedSearchFilters = {
      query,
      ...defaults,
      ratingFilter: rating,
      metadataStatus: effectiveMetadataStatus,
      libraryId: libraryId || undefined,
      sort,
      limit: pageSize,
      offset: (page - 1) * pageSize,
    }
    bridge.advancedSearch(filters)
      .then((result) => { if (seq === loadSeq.current) { setItems(result.items); setTotal(result.total) } })
      .catch((reason: Error) => { if (seq === loadSeq.current) setError(reason.message) })
      .finally(() => { if (seq === loadSeq.current) setLoading(false) })
  }, [defaults, imageStatus, libraryId, metadataStatus, page, pageSize, query, rating, sort])

  useEffect(load, [load, reloadSignal])
  useEffect(() => () => { loadSeq.current += 1 }, [])
  useEffect(() => { bridge.libraries().then(setLibraries).catch(() => undefined) }, [])
  useEffect(() => {
    const next = buildSavedState()
    if (shouldRestoreScroll.current && saved.restoreSignature === stateSignature) {
      window.sessionStorage.setItem(stateKey, JSON.stringify({ ...next, scrollY: pendingScrollY.current, restoreScroll: true, restoreSignature: stateSignature }))
      return
    }
    shouldRestoreScroll.current = false
    window.sessionStorage.setItem(stateKey, JSON.stringify(next))
  }, [buildSavedState, saved.restoreSignature, stateKey, stateSignature])
  useLayoutEffect(() => {
    if (!shouldRestoreScroll.current || loading || error) return
    shouldRestoreScroll.current = false
    window.scrollTo(0, pendingScrollY.current)
    window.sessionStorage.setItem(stateKey, JSON.stringify(buildSavedState()))
  }, [buildSavedState, error, items.length, loading, stateKey, total])
  useEffect(() => {
    const next = normalizeSearch(search)
    if (next === query) return
    const timer = window.setTimeout(() => { setPage(1); setQuery(next) }, 300)
    return () => window.clearTimeout(timer)
  }, [query, search])

  const rememberScrollForDetail = useCallback(() => {
    window.sessionStorage.setItem(stateKey, JSON.stringify({ ...buildSavedState(), scrollY: window.scrollY, restoreScroll: true, restoreSignature: stateSignature }))
  }, [buildSavedState, stateKey, stateSignature])
  const openMovie = (item: MediaItem) => {
    const openDefault = () => {
      rememberScrollForDetail()
      movieActions.openMovie(item, { source: 'movie-wall', search: query, sort })
    }
    if (onOpenItem) onOpenItem(item, openDefault)
    else openDefault()
  }
  const submitSearch = () => { setPage(1); setQuery(normalizeSearch(search)) }
  const clearSearch = () => { setSearch(''); setQuery(''); setPage(1) }
  const clearFilters = () => {
    setRating('all'); setMetadataStatus('all'); setImageStatus('all'); setLibraryId(defaults.libraryId ?? 0); setPage(1)
  }
  const handleSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') { event.preventDefault(); submitSearch() }
    else if (event.key === 'Escape') { if (search) clearSearch(); else event.currentTarget.blur() }
  }
  const context = { total, items, loading, query, reload: load } satisfies MovieWallRenderContext
  const stats = defaultLabel || renderStats ? <Stack spacing={1}>{defaultLabel}{renderStats?.(context)}</Stack> : undefined

  const filters = <Stack component="form" onSubmit={(event) => { event.preventDefault(); submitSearch() }} spacing={1.25}>
    <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', md: 'center' }, flexWrap: 'wrap' }}>
      <TextField size="small" value={search} onChange={(event) => setSearch(event.target.value)} onKeyDown={handleSearchKeyDown} placeholder="搜索标题、番号、演员、标签，或输入“评分>=4 收藏”" sx={{ flex: '1 1 280px' }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon/></InputAdornment> } }}/>
      <TextField select size="small" label="排序" value={sort} onChange={(event) => { setPage(1); setSort(event.target.value) }} sx={fieldSx}>
        <MenuItem value="newest">最新导入</MenuItem><MenuItem value="code">番号</MenuItem><MenuItem value="title">标题</MenuItem><MenuItem value="release">发行日期</MenuItem><MenuItem value="rating">个人评分</MenuItem>
      </TextField>
      <Button type="submit" variant="contained">搜索</Button>
      <ViewModeToggle value={view} onChange={setView}/>
      <Button color="inherit" endIcon={moreOpen ? <ExpandLessRoundedIcon/> : <ExpandMoreRoundedIcon/>} onClick={() => setMoreOpen((value) => !value)}>{moreOpen ? '收起筛选' : '更多筛选'}</Button>
    </Stack>
    <Collapse in={moreOpen} unmountOnExit={false}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', md: 'center' }, flexWrap: 'wrap' }}>
        <TextField select size="small" label="评分" value={rating} onChange={(event) => { setPage(1); setRating(event.target.value) }} sx={fieldSx}>
          <MenuItem value="all">全部评分</MenuItem><MenuItem value="unrated">未评分</MenuItem>{[5, 4, 3, 2, 1].map((value) => <MenuItem key={value} value={String(value)}>{'★'.repeat(value)}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="元数据状态" value={metadataStatus} onChange={(event) => { setPage(1); setMetadataStatus(event.target.value) }} sx={fieldSx}>
          <MenuItem value="all">全部</MenuItem><MenuItem value="complete">已完整</MenuItem><MenuItem value="unscraped">未刮削</MenuItem><MenuItem value="missing-nfo">缺 NFO</MenuItem><MenuItem value="missing-actors">缺演员</MenuItem><MenuItem value="missing-tags">缺标签</MenuItem><MenuItem value="missing-description">缺简介</MenuItem>
        </TextField>
        <TextField select size="small" label="图片状态" value={imageStatus} onChange={(event) => { setPage(1); setImageStatus(event.target.value) }} sx={fieldSx}>
          <MenuItem value="all">全部图片</MenuItem><MenuItem value="missing">缺少图片</MenuItem><MenuItem value="normal">图片正常</MenuItem>
        </TextField>
        <TextField select size="small" label="媒体库" value={libraryId} onChange={(event) => { setPage(1); setLibraryId(Number(event.target.value)) }} sx={fieldSx}>
          <MenuItem value={0}>全部媒体库</MenuItem>{libraries.map((library) => <MenuItem key={library.id} value={library.id}>{library.name}</MenuItem>)}
        </TextField>
        {filterDirty && <Button color="inherit" onClick={clearFilters}>清空</Button>}
      </Stack>
    </Collapse>
  </Stack>

  return <WorkspacePage title={title} description={description?.(total)} stats={stats} filters={filters} activeFilterCount={activeFilterCount} loading={loading} error={error}
    primaryActions={[...(primaryActions?.(context) ?? []), refreshAction(load)]}>
    <MovieResultContainer items={items} total={total} page={page} pageSize={pageSize} onPageChange={setPage} view={view} selectable={selectable} selectedIds={selectedIds} onSelect={onSelect} onRatingClick={onRatingClick} onContextMenu={onContextMenu} onPlay={movieActions.playMovie} onOpen={openMovie} emptyTitle={emptyTitle} emptyDescription={emptyDescription}/>
    {childrenAfterResults?.(context)}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </WorkspacePage>
}

function readSavedState(key: string): SavedMovieWallState {
  try {
    return JSON.parse(window.sessionStorage.getItem(key) || '{}') as SavedMovieWallState
  } catch {
    return {}
  }
}
