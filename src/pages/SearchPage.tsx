import FilterAltRoundedIcon from '@mui/icons-material/FilterAltRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import { Button, Chip, FormControlLabel, InputAdornment, MenuItem, Snackbar, Stack, Switch, TextField } from '@mui/material'
import { FormEvent, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { SectionTitle } from '@/components/ProductComponents'
import { MovieResultContainer, useMovieActions } from '@/components/workspace/MovieResults'
import { ViewModeToggle, WorkspacePage } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { GlobalSearchResult, MediaItem, MediaLibrary } from '@/types/media'
import type { WorkspaceViewMode } from '@/components/workspace/Workspace'

const pageSize = 48
const searchStateKey = 'lmm.searchPage.state'

interface SavedSearchState {
  favorite?: boolean
  watched?: boolean
  rating?: string
  metadata?: string
  metadataStatus?: string
  fileStatus?: string
  libraryId?: number
  sort?: string
  view?: WorkspaceViewMode
  scrollY?: number
  restoreScroll?: boolean
  restoreSignature?: string
}

const readSavedSearchState = (): SavedSearchState => {
  try {
    return JSON.parse(window.sessionStorage.getItem(searchStateKey) || '{}') as SavedSearchState
  } catch {
    return {}
  }
}

export default function SearchPage() {
  const [params, setParams] = useSearchParams()
  const navigate = useNavigate()
  const saved = useMemo(readSavedSearchState, [])
  const query = params.get('q') || ''
  const page = Math.max(1, Number(params.get('page') || 1))
  const initialMetadataStatus = params.get('metadataStatus') || saved.metadataStatus || 'all'
  const [input, setInput] = useState(query)
  const [items, setItems] = useState<MediaItem[]>()
  const [total, setTotal] = useState(0)
  const [related, setRelated] = useState<GlobalSearchResult>()
  const [libraries, setLibraries] = useState<MediaLibrary[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [view, setView] = useState<WorkspaceViewMode>(saved.view ?? 'grid')
  const [favorite, setFavorite] = useState(saved.favorite ?? false)
  const [watched, setWatched] = useState(saved.watched ?? false)
  const [rating, setRating] = useState(saved.rating ?? 'all')
  const [metadata, setMetadata] = useState(saved.metadata ?? 'all')
  const [metadataStatus, setMetadataStatus] = useState(initialMetadataStatus)
  const [fileStatus, setFileStatus] = useState(saved.fileStatus ?? 'all')
  const [libraryId, setLibraryId] = useState(saved.libraryId ?? 0)
  const [sort, setSort] = useState(saved.sort ?? 'newest')
  const movieActions = useMovieActions({ onNotice: setNotice, play: bridge.play })
  const loadSeq = useRef(0)

  useEffect(() => { bridge.libraries().then(setLibraries).catch(() => undefined) }, [])
  useEffect(() => () => { loadSeq.current += 1 }, [])
  const signature = useMemo(() => JSON.stringify({ query, page, favorite, watched, rating, metadata, metadataStatus, fileStatus, libraryId, sort, view }), [query, page, favorite, watched, rating, metadata, metadataStatus, fileStatus, libraryId, sort, view])
  const shouldRestoreScroll = useRef(saved.restoreScroll === true && saved.restoreSignature === signature)
  const pendingScrollY = useRef(typeof saved.scrollY === 'number' ? saved.scrollY : 0)
  const buildSavedState = useCallback((): SavedSearchState => ({ favorite, watched, rating, metadata, metadataStatus, fileStatus, libraryId, sort, view }), [favorite, fileStatus, libraryId, metadata, metadataStatus, rating, sort, view, watched])
  const activeFilterCount = [query, favorite, watched, rating !== 'all', metadata !== 'all', metadataStatus !== 'all', fileStatus !== 'all', libraryId > 0].filter(Boolean).length

  useEffect(() => {
    const next = buildSavedState()
    if (shouldRestoreScroll.current && saved.restoreSignature === signature) {
      window.sessionStorage.setItem(searchStateKey, JSON.stringify({ ...next, scrollY: pendingScrollY.current, restoreScroll: true, restoreSignature: signature }))
      return
    }
    shouldRestoreScroll.current = false
    window.sessionStorage.setItem(searchStateKey, JSON.stringify(next))
  }, [buildSavedState, saved.restoreSignature, signature])

  useEffect(() => {
    setInput(query)
    if (!activeFilterCount) { setItems(undefined); setTotal(0); setRelated(undefined); return }
    const seq = ++loadSeq.current
    setLoading(true); setError('')
    Promise.all([
      bridge.advancedSearch({ query, favorite: favorite ? true : undefined, watched: watched ? true : undefined, ratingFilter: rating, metadata, metadataStatus, fileStatus, libraryId: libraryId || undefined, sort, limit: pageSize, offset: (page - 1) * pageSize }),
      query ? bridge.search(query) : Promise.resolve(undefined),
    ]).then(([result, entities]) => { if (seq === loadSeq.current) { setItems(result.items); setTotal(result.total); setRelated(entities) } }).catch((reason: Error) => { if (seq === loadSeq.current) setError(reason.message) }).finally(() => { if (seq === loadSeq.current) setLoading(false) })
  }, [signature, activeFilterCount, query, page, favorite, watched, rating, metadata, metadataStatus, fileStatus, libraryId, sort])

  useLayoutEffect(() => {
    if (!shouldRestoreScroll.current || loading || error) return
    shouldRestoreScroll.current = false
    window.scrollTo(0, pendingScrollY.current)
    window.sessionStorage.setItem(searchStateKey, JSON.stringify(buildSavedState()))
  }, [buildSavedState, error, items?.length, loading, total])

  const updatePage = (next: number) => { const value = new URLSearchParams(params); value.set('page', String(next)); setParams(value) }
  const submit = (event: FormEvent) => { event.preventDefault(); const value = new URLSearchParams(params); const text = input.trim(); if (text) value.set('q', text); else value.delete('q'); value.delete('page'); setParams(value) }
  const clearFilters = () => { setFavorite(false); setWatched(false); setRating('all'); setMetadata('all'); setMetadataStatus('all'); setFileStatus('all'); setLibraryId(0); setSort('newest'); setParams(new URLSearchParams()) }
  const rememberScrollForDetail = useCallback(() => {
    window.sessionStorage.setItem(searchStateKey, JSON.stringify({ ...buildSavedState(), scrollY: window.scrollY, restoreScroll: true, restoreSignature: signature }))
  }, [buildSavedState, signature])
  const openMovieFromSearch = (item: MediaItem) => { rememberScrollForDetail(); movieActions.openMovie(item, { source: 'search', search: query, sort }) }

  const filters = <Stack component="form" onSubmit={submit} direction={{ xs: 'column', xl: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', xl: 'center' }, flexWrap: 'wrap' }}>
    <TextField size="small" value={input} onChange={event => setInput(event.target.value)} placeholder="标题、番号、文件名、演员、标签；支持 收藏 已观看 评分>=4" sx={{ flex: '1 1 300px' }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon/></InputAdornment> } }}/>
    <TextField select size="small" label="评分" value={rating} onChange={event => { setRating(event.target.value); updatePage(1) }} sx={{ minWidth: 130 }}><MenuItem value="all">全部评分</MenuItem><MenuItem value="unrated">未评分</MenuItem>{[5, 4, 3, 2, 1].map(value => <MenuItem key={value} value={String(value)}>{'★'.repeat(value)}</MenuItem>)}</TextField>
    <TextField select size="small" label="元数据状态" value={metadataStatus} onChange={event => { setMetadataStatus(event.target.value); updatePage(1) }} sx={{ minWidth: 150 }}><MenuItem value="all">全部</MenuItem><MenuItem value="complete">已完整</MenuItem><MenuItem value="unscraped">未刮削</MenuItem><MenuItem value="missing-images">缺图片</MenuItem><MenuItem value="missing-nfo">缺 NFO</MenuItem><MenuItem value="missing-actors">缺演员</MenuItem><MenuItem value="missing-tags">缺标签</MenuItem><MenuItem value="missing-description">缺简介</MenuItem></TextField>
    <TextField select size="small" label="元数据" value={metadata} onChange={event => { setMetadata(event.target.value); updatePage(1) }} sx={{ minWidth: 120 }}><MenuItem value="all">全部</MenuItem><MenuItem value="complete">已刮削</MenuItem><MenuItem value="missing">缺信息</MenuItem></TextField>
    <TextField select size="small" label="文件" value={fileStatus} onChange={event => { setFileStatus(event.target.value); updatePage(1) }} sx={{ minWidth: 120 }}><MenuItem value="all">全部</MenuItem><MenuItem value="available">文件存在</MenuItem><MenuItem value="missing">文件缺失</MenuItem></TextField>
    <TextField select size="small" label="媒体库" value={libraryId} onChange={event => { setLibraryId(Number(event.target.value)); updatePage(1) }} sx={{ minWidth: 150 }}><MenuItem value={0}>全部媒体库</MenuItem>{libraries.map(item => <MenuItem key={item.id} value={item.id}>{item.name}</MenuItem>)}</TextField>
    <TextField select size="small" label="排序" value={sort} onChange={event => { setSort(event.target.value); updatePage(1) }} sx={{ minWidth: 130 }}><MenuItem value="newest">最新导入</MenuItem><MenuItem value="code">番号</MenuItem><MenuItem value="rating">评分</MenuItem></TextField>
    <Button type="submit" variant="contained" startIcon={<FilterAltRoundedIcon/>}>搜索</Button>
    <ViewModeToggle value={view} onChange={setView}/>
    <FormControlLabel sx={{ ml: 0 }} control={<Switch checked={favorite} onChange={event => { setFavorite(event.target.checked); updatePage(1) }}/>} label="收藏"/>
    <FormControlLabel sx={{ ml: 0 }} control={<Switch checked={watched} onChange={event => { setWatched(event.target.checked); updatePage(1) }}/>} label="已观看"/>
  </Stack>

  return <WorkspacePage title="搜索" description="通过 Bridge 组合搜索影片、收藏、评分、元数据、文件状态和媒体库。" filters={filters} activeFilterCount={activeFilterCount} onClearFilters={clearFilters} loading={loading} error={error}
  >
    {!activeFilterCount && !items ? <MovieResultContainer items={[]} view={view} onPlay={movieActions.playMovie} onOpen={openMovieFromSearch} emptyTitle="搜索你的媒体库" emptyDescription="输入关键词，或直接选择评分、收藏、元数据、文件状态和媒体库条件。"/> : items && <Stack spacing={3}>
      <MovieResultContainer title="影片" total={total} items={items} view={view} page={page} pageSize={pageSize} onPageChange={updatePage} onPlay={movieActions.playMovie} onOpen={openMovieFromSearch} emptyTitle="没有匹配影片" emptyDescription="尝试减少筛选条件或使用更短的关键词。"/>
      {related && (related.actors.length > 0 || related.tags.length > 0) && <Stack spacing={2}>
        <SectionTitle title="相关维度"/>
        <Stack direction="row" useFlexGap spacing={1} sx={{ flexWrap: 'wrap' }}>
          {related.actors.map(item => <Chip clickable key={`actor-${item.id}`} label={`演员：${item.name} · ${item.movieCount}`} onClick={() => navigate('/actors')}/>)}
          {related.tags.map(item => <Chip clickable key={`tag-${item.id}`} color="primary" variant="outlined" label={`标签：${item.name} · ${item.movieCount}`} onClick={() => navigate('/tags')}/>)}
        </Stack>
      </Stack>}
    </Stack>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </WorkspacePage>
}
