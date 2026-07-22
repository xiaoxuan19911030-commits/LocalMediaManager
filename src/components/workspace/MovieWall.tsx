import CheckRoundedIcon from '@mui/icons-material/CheckRounded'
import KeyboardArrowDownRoundedIcon from '@mui/icons-material/KeyboardArrowDownRounded'
import NavigateBeforeRoundedIcon from '@mui/icons-material/NavigateBeforeRounded'
import NavigateNextRoundedIcon from '@mui/icons-material/NavigateNextRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import ShuffleRoundedIcon from '@mui/icons-material/ShuffleRounded'
import { Box, Button, Divider, IconButton, InputAdornment, Menu, MenuItem, Paper, Snackbar, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState, type KeyboardEvent, type MouseEvent, type ReactNode } from 'react'
import { MovieResultContainer, useMovieActions } from '@/components/workspace/MovieResults'
import { WorkspacePage, refreshAction, type WorkspaceAction, type WorkspaceViewMode } from '@/components/workspace/Workspace'
import { defaultMovieWallDisplay, normalizeMovieWallDisplay, type MovieWallDisplaySettings } from '@/components/workspace/movieWallDisplay'
import { bridge } from '@/services/bridge'
import type { AdvancedSearchFilters, MediaItem, MediaLibrary } from '@/types/media'

const toolbarScrollSx = {
  overflowX: 'auto',
  scrollbarWidth: 'none',
  msOverflowStyle: 'none',
  '&::-webkit-scrollbar': { display: 'none' },
}
const controlSx = {
  height: 36,
  borderRadius: 1.25,
  borderColor: 'divider',
  bgcolor: 'background.paper',
  color: 'text.primary',
  boxShadow: 'inset 0 1px 0 rgba(255,255,255,.04)',
  whiteSpace: 'nowrap',
  '&:hover': {
    borderColor: 'primary.main',
    bgcolor: 'action.hover',
  },
}

export interface MovieWallDefaults {
  actorId?: number
  directorId?: number
  movieTagId?: number
  customTagId?: number
  genreId?: number
  seriesId?: number
  studioId?: number
  libraryId?: number
  favorite?: boolean
  watched?: boolean
  metadataStatus?: string
  fileStatus?: string
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
  filters: AdvancedSearchFilters
  reload: () => void
}

type MenuKind = 'sort' | 'rating' | 'metadata' | 'image' | 'library'

const sortOptions = [
  { value: 'newest', label: '最新导入' },
  { value: 'code', label: '番号' },
  { value: 'title', label: '标题' },
  { value: 'release', label: '发行日期' },
  { value: 'rating', label: '个人评分' },
]

const ratingOptions = [
  { value: 'all', label: '全部评分' },
  { value: 'unrated', label: '未评分' },
  { value: '5', label: '★★★★★' },
  { value: '4', label: '★★★★' },
  { value: '3', label: '★★★' },
  { value: '2', label: '★★' },
  { value: '1', label: '★' },
]

const metadataOptions = [
  { value: 'all', label: '全部元数据' },
  { value: 'complete', label: '已完成' },
  { value: 'unscraped', label: '未刮削' },
  { value: 'missing-nfo', label: '缺 NFO' },
  { value: 'missing-actors', label: '缺演员' },
  { value: 'missing-tags', label: '缺标签' },
  { value: 'missing-description', label: '缺简介' },
]

const imageOptions = [
  { value: 'all', label: '全部图片' },
  { value: 'missing', label: '缺少图片' },
  { value: 'normal', label: '图片正常' },
]

const EMPTY_MOVIE_WALL_DEFAULTS: MovieWallDefaults = {}

function libraryOptions(libraries: MediaLibrary[]) {
  return [
    { value: '0', label: '全部媒体库' },
    ...libraries.map((library) => ({ value: String(library.id), label: library.name })),
  ]
}

function labelForSort(value: string) {
  return sortOptions.find((item) => item.value === value)?.label ?? '最新导入'
}

function labelForRating(value: string) {
  return ratingOptions.find((item) => item.value === value)?.label ?? '全部评分'
}

function labelForMetadata(value: string) {
  return metadataOptions.find((item) => item.value === value)?.label ?? '全部元数据'
}

function labelForImage(value: string) {
  return imageOptions.find((item) => item.value === value)?.label ?? '全部图片'
}

function labelForLibrary(value: number, libraries: MediaLibrary[]) {
  if (!value) return '全部媒体库'
  return libraries.find((item) => item.id === value)?.name ?? '全部媒体库'
}

export function MovieWall({
  title,
  description,
  stateKey,
  defaults = EMPTY_MOVIE_WALL_DEFAULTS,
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
  onRatingClick?: (item: MediaItem, value: number | null) => void
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
  const [view, setView] = useState<WorkspaceViewMode>(canReuseSavedState ? saved.view ?? 'grid' : 'grid')
  const [movieWallDisplay, setMovieWallDisplay] = useState<MovieWallDisplaySettings>(defaultMovieWallDisplay)
  const [shortcutsEnabled, setShortcutsEnabled] = useState(true)
  const [libraries, setLibraries] = useState<MediaLibrary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [randomLoading, setRandomLoading] = useState(false)
  const [ratingSavingId, setRatingSavingId] = useState<number>()
  const [pageInputFocusSignal, setPageInputFocusSignal] = useState(0)
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null)
  const [menuKind, setMenuKind] = useState<MenuKind>()
  const loadSeq = useRef(0)
  const movieActions = useMovieActions({ onNotice: setNotice, play: bridge.play })

  const stateSignature = useMemo(() => JSON.stringify({ defaultsSignature, page, query, sort, rating, metadataStatus, imageStatus, libraryId, view }), [defaultsSignature, imageStatus, libraryId, metadataStatus, page, query, rating, sort, view])
  const shouldRestoreScroll = useRef(saved.restoreScroll === true && saved.restoreSignature === stateSignature)
  const pendingScrollY = useRef(typeof saved.scrollY === 'number' ? saved.scrollY : 0)
  const buildSavedState = useCallback((): SavedMovieWallState => ({ page, search, query, sort, rating, metadataStatus, imageStatus, libraryId, view, defaultsSignature }), [defaultsSignature, imageStatus, libraryId, metadataStatus, page, query, rating, search, sort, view])
  const filterDirty = rating !== 'all' || metadataStatus !== 'all' || imageStatus !== 'all' || libraryId !== (defaults.libraryId ?? 0)
  const activeFilterCount = (query ? 1 : 0) + (sort !== 'newest' ? 1 : 0) + (filterDirty ? 1 : 0)
  const totalPages = Math.max(1, Math.ceil(total / pageSize))

  const buildSearchFilters = useCallback((nextLimit = pageSize, nextOffset = (page - 1) * pageSize): AdvancedSearchFilters => {
    const effectiveMetadataStatus = defaults.metadataStatus ?? (imageStatus === 'missing' ? 'missing-images' : imageStatus === 'normal' ? 'complete' : metadataStatus)
    return {
      query,
      ...defaults,
      ratingFilter: rating,
      metadataStatus: effectiveMetadataStatus,
      libraryId: libraryId || undefined,
      sort,
      limit: nextLimit,
      offset: nextOffset,
    }
  }, [defaults, imageStatus, libraryId, metadataStatus, page, pageSize, query, rating, sort])

  const load = useCallback(() => {
    const seq = ++loadSeq.current
    if (import.meta.env.DEV) console.debug('[MovieWall] Loading Start', { query, page, seq })
    setLoading(true)
    setError('')
    bridge.advancedSearch(buildSearchFilters())
      .then((result) => {
        if (seq === loadSeq.current) {
          if (import.meta.env.DEV) console.debug('[MovieWall] Search Response Count', { count: result.items.length, total: result.total, seq })
          setItems(result.items); setTotal(result.total)
        }
      })
      .catch((reason: Error) => { if (seq === loadSeq.current) { if (import.meta.env.DEV) console.debug('[MovieWall] Search Error', reason); setError(reason.message) } })
      .finally(() => { if (seq === loadSeq.current) { if (import.meta.env.DEV) console.debug('[MovieWall] Loading End', { seq }); setLoading(false) } })
  }, [buildSearchFilters])

  useEffect(load, [load, reloadSignal])
  useEffect(() => () => { loadSeq.current += 1 }, [])
  useEffect(() => { bridge.libraries().then(setLibraries).catch(() => undefined) }, [])
  useEffect(() => {
    bridge.allSettings().then(settings => {
      const display = normalizeMovieWallDisplay(settings.movieWallDisplay)
      setMovieWallDisplay(display)
      setShortcutsEnabled(settings.system?.globalShortcutsEnabled ?? true)
    }).catch(() => undefined)
  }, [])
  useEffect(() => {
    if (!loading && total > 0 && page > totalPages) setPage(totalPages)
  }, [loading, page, total, totalPages])
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
  const goToPage = useCallback((nextPage: number) => {
    const clamped = Math.max(1, Math.min(totalPages, nextPage))
    if (clamped === page) return
    setPage(clamped)
    window.scrollTo({ top: 0, behavior: 'smooth' })
  }, [page, totalPages])
  useEffect(() => {
    const handleKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.defaultPrevented || isMovieWallShortcutBlocked(event)) return
      if (!shortcutsEnabled) return
      if (event.ctrlKey && event.key.toLowerCase() === 'g') {
        event.preventDefault()
        setPageInputFocusSignal((value) => value + 1)
        return
      }
      if (event.ctrlKey || event.altKey || event.metaKey) return
      if (event.key === 'ArrowLeft' && page > 1) {
        event.preventDefault()
        goToPage(page - 1)
      } else if (event.key === 'ArrowRight' && page < totalPages) {
        event.preventDefault()
        goToPage(page + 1)
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [goToPage, page, shortcutsEnabled, totalPages])

  const openMovie = (item: MediaItem) => {
    const openDefault = () => {
      rememberScrollForDetail()
      movieActions.openMovie(item, { source: 'movie-wall', search: query, sort })
    }
    if (onOpenItem) onOpenItem(item, openDefault)
    else openDefault()
  }
  const openRandomMovie = () => {
    if (randomLoading) return
    setRandomLoading(true)
    bridge.randomMovie(buildSearchFilters(pageSize, 0))
      .then((result) => {
        if (result.items.length === 0) {
          setNotice('当前条件下没有可随机的影片。')
          return
        }
        setItems(result.items)
        setTotal(result.total)
        setPage(1)
        const first = result.items[0]
        setNotice(`已随机显示 ${result.items.length} 部影片${first ? `，首部：${first.code || first.title || first.dataId}` : ''}`)
      })
      .catch((reason: Error) => setNotice(reason.message))
      .finally(() => setRandomLoading(false))
  }
  const saveRating = (item: MediaItem, value: number | null) => {
    if (ratingSavingId) return
    setRatingSavingId(item.dataId)
    bridge.setUserState(item.dataId, value === null ? { clearRating: true } : { rating: value })
      .then(() => {
        setItems((current) => current.map((movie) => movie.dataId === item.dataId ? { ...movie, grade: value ?? 0 } : movie))
        setNotice(value === null ? '已清除评分' : '已保存评分')
      })
      .catch((reason: Error) => setNotice(reason.message))
      .finally(() => setRatingSavingId(undefined))
  }
  const submitSearch = () => { setPage(1); setQuery(normalizeSearch(search)) }
  const clearSearch = () => { setSearch(''); setQuery(''); setPage(1) }
  const handleSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter') { event.preventDefault(); submitSearch() }
    else if (event.key === 'Escape') { if (search) clearSearch(); else event.currentTarget.blur() }
  }
  const context = { total, items, loading, query, filters: buildSearchFilters(1, 0), reload: load } satisfies MovieWallRenderContext
  const stats = defaultLabel || renderStats ? <Stack spacing={1}>{defaultLabel}{renderStats?.(context)}</Stack> : undefined

  const actionItems: WorkspaceAction[] = [...(primaryActions?.(context) ?? []), { key: 'random', label: '随机', icon: <ShuffleRoundedIcon/>, variant: 'text', disabled: randomLoading, onClick: openRandomMovie }, refreshAction(load, '刷新')]
  const menuOptions = menuKind === 'sort'
    ? sortOptions
    : menuKind === 'rating'
      ? ratingOptions
      : menuKind === 'metadata'
        ? metadataOptions
        : menuKind === 'image'
          ? imageOptions
          : libraryOptions(libraries)
  const menuValue = menuKind === 'sort'
    ? sort
    : menuKind === 'rating'
      ? rating
      : menuKind === 'metadata'
        ? metadataStatus
        : menuKind === 'image'
          ? imageStatus
          : String(libraryId || 0)
  const openMenu = (anchor: HTMLElement, kind: MenuKind) => { setMenuAnchor(anchor); setMenuKind(kind) }
  const closeMenu = () => { setMenuAnchor(null); setMenuKind(undefined) }
  const applyMenuValue = (kind: MenuKind, value: string) => {
    setPage(1)
    if (kind === 'sort') setSort(value)
    else if (kind === 'rating') setRating(value)
    else if (kind === 'metadata') setMetadataStatus(value)
    else if (kind === 'image') setImageStatus(value)
    else setLibraryId(Number(value))
    closeMenu()
  }

  const filters = <Stack component="form" onSubmit={(event) => { event.preventDefault(); submitSearch() }} direction="row" spacing={0.75} useFlexGap sx={{ alignItems: 'center', flexWrap: 'nowrap', minWidth: 0, ...toolbarScrollSx }}>
    <TextField
      size="small"
      value={search}
      onChange={(event) => setSearch(event.target.value)}
      onKeyDown={handleSearchKeyDown}
      placeholder="搜索标题、番号、演员、标签……"
      sx={{ flex: '1 0 360px', minWidth: 280, '& .MuiInputBase-root': { height: 36, borderRadius: 1.25, bgcolor: 'background.paper', boxShadow: 'inset 0 1px 0 rgba(255,255,255,.04)' } }}
      slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon fontSize="small"/></InputAdornment> } }}
    />
    <Divider flexItem orientation="vertical" sx={{ mx: .25 }} />
    {[{ key: 'rating', label: labelForRating(rating) }, { key: 'metadata', label: labelForMetadata(metadataStatus) }, { key: 'image', label: labelForImage(imageStatus) }, { key: 'library', label: labelForLibrary(libraryId, libraries) }, { key: 'sort', label: labelForSort(sort) }].map((item, index, array) => <Box key={item.key} sx={{ display: 'flex', alignItems: 'center', flex: '0 0 auto' }}>
      <Button size="small" variant="outlined" color="inherit" endIcon={<KeyboardArrowDownRoundedIcon fontSize="small"/>} onClick={(event) => openMenu(event.currentTarget, item.key as MenuKind)} sx={{ ...controlSx, minWidth: item.key === 'metadata' || item.key === 'library' ? 126 : 104, px: 1.25, fontWeight: 750 }}>
        {item.label}
      </Button>
      {index < array.length - 1 && <Box sx={{ width: 2 }} />}
    </Box>)}
    <Divider flexItem orientation="vertical" sx={{ mx: .25 }} />
    {actionItems.map((action, index, array) => <Box key={action.key} sx={{ display: 'flex', alignItems: 'center', flex: '0 0 auto' }}>
      <Button size="small" variant="outlined" color={action.color ?? 'inherit'} disabled={action.disabled} onClick={action.onClick} startIcon={action.icon} sx={{ ...controlSx, minWidth: 0, px: 1.15, fontWeight: 750 }}>
        {action.label}
      </Button>
      {index < array.length - 1 && <Box sx={{ width: 2 }} />}
    </Box>)}
    <Menu open={Boolean(menuAnchor && menuKind)} anchorEl={menuAnchor} onClose={closeMenu}>
      {menuOptions.map((item) => <MenuItem key={item.value} selected={menuValue === item.value} onClick={() => menuKind && applyMenuValue(menuKind, item.value)}>
        {menuValue === item.value && <CheckRoundedIcon fontSize="small" sx={{ mr: 1 }}/>}
        {item.label}
      </MenuItem>)}
    </Menu>
  </Stack>

  return <WorkspacePage title={title} description={description?.(total)} stats={stats} filters={filters} activeFilterCount={activeFilterCount} loading={loading} error={error}>
    <Box sx={{ position: 'relative', pb: total > pageSize ? { xs: 9, md: 10 } : 0, pr: total > pageSize ? { lg: 13 } : 0 }}>
      <MovieResultContainer items={items} total={total} display={movieWallDisplay} view={view} selectable={selectable} selectedIds={selectedIds} onSelect={onSelect} onRatingClick={onRatingClick ?? saveRating} onContextMenu={onContextMenu} onPlay={movieActions.playMovie} onOpen={openMovie} emptyTitle={emptyTitle} emptyDescription={emptyDescription}/>
    </Box>
    {total > pageSize && <FloatingPagination page={page} totalPages={totalPages} onPageChange={goToPage} focusSignal={pageInputFocusSignal}/>}
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

function normalizeSearch(value: string) {
  return value.replace(/\u3000/g, ' ').replace(/\s+/g, ' ').trim()
}

function isMovieWallShortcutBlocked(event: globalThis.KeyboardEvent) {
  if (document.querySelector('.MuiModal-root, .MuiPopover-root')) return true
  const target = event.target
  if (!(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true
  const tag = target.tagName.toLowerCase()
  if (tag === 'input' || tag === 'textarea' || tag === 'select') return true
  const role = target.getAttribute('role')
  if (role === 'textbox' || role === 'combobox' || role === 'spinbutton') return true
  return Boolean(target.closest('[contenteditable="true"], input, textarea, select, [role="textbox"], [role="combobox"], [role="spinbutton"]'))
}

function FloatingPagination({ page, totalPages, onPageChange, focusSignal }: { page: number; totalPages: number; onPageChange: (page: number) => void; focusSignal: number }) {
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState(String(page))
  const inputRef = useRef<HTMLInputElement | null>(null)

  useEffect(() => {
    if (!editing) setDraft(String(page))
  }, [editing, page])
  useEffect(() => {
    if (focusSignal <= 0) return
    setEditing(true)
  }, [focusSignal])
  useEffect(() => {
    if (!editing) return
    window.requestAnimationFrame(() => {
      inputRef.current?.focus()
      inputRef.current?.select()
    })
  }, [editing])

  const commit = () => {
    const value = Number.parseInt(draft, 10)
    if (Number.isFinite(value) && value >= 1 && value <= totalPages) onPageChange(value)
    setEditing(false)
    setDraft(String(page))
  }

  return <Paper elevation={8} sx={{
    position: 'fixed',
    right: { xs: 14, md: 24 },
    bottom: { xs: 14, md: 24 },
    zIndex: theme => theme.zIndex.appBar - 1,
    borderRadius: 999,
    px: 0.75,
    py: 0.5,
    bgcolor: theme => theme.palette.mode === 'dark' ? 'rgba(18,22,31,.82)' : 'rgba(255,255,255,.86)',
    border: 1,
    borderColor: 'divider',
    backdropFilter: 'blur(14px)',
  }}>
    <Stack direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
      <Tooltip title="上一页">
        <span><IconButton size="small" disabled={page <= 1} onClick={() => onPageChange(page - 1)}><NavigateBeforeRoundedIcon fontSize="small"/></IconButton></span>
      </Tooltip>
      {editing ? (
        <TextField
          inputRef={inputRef}
          size="small"
          value={draft}
          onChange={(event) => setDraft(event.target.value.replace(/[^\d]/g, '').slice(0, 6))}
          onKeyDown={(event) => {
            if (event.key === 'Enter') { event.preventDefault(); commit() }
            else if (event.key === 'Escape') { event.preventDefault(); setEditing(false); setDraft(String(page)) }
          }}
          onBlur={commit}
          slotProps={{ input: { inputMode: 'numeric', sx: { width: 54, height: 30, px: 0.75, textAlign: 'center' } } }}
        />
      ) : (
        <Tooltip title="点击输入页码，Ctrl+G 可快速聚焦">
          <Button color="inherit" size="small" onClick={() => setEditing(true)} sx={{ minWidth: 82, px: 1, borderRadius: 999, fontWeight: 850 }}>
            <Typography component="span" variant="body2" sx={{ fontWeight: 850 }}>{page}</Typography>
            <Typography component="span" variant="body2" color="text.secondary" sx={{ mx: 0.5 }}>/</Typography>
            <Typography component="span" variant="body2" color="text.secondary">{totalPages}</Typography>
          </Button>
        </Tooltip>
      )}
      <Tooltip title="下一页">
        <span><IconButton size="small" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}><NavigateNextRoundedIcon fontSize="small"/></IconButton></span>
      </Tooltip>
    </Stack>
  </Paper>
}
