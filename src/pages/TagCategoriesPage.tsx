import BusinessRoundedIcon from '@mui/icons-material/BusinessRounded'
import CategoryRoundedIcon from '@mui/icons-material/CategoryRounded'
import GroupsRoundedIcon from '@mui/icons-material/GroupsRounded'
import LocalOfferRoundedIcon from '@mui/icons-material/LocalOfferRounded'
import MovieRoundedIcon from '@mui/icons-material/MovieRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import { Box, Button, Card, CardActionArea, CardContent, InputAdornment, MenuItem, Pagination, Stack, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material'
import { FormEvent, ReactNode, useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState } from '@/components/ProductComponents'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { EntityCard, MediaLibrary } from '@/types/media'

const pageSize = 48
const stateKey = 'lmm.tags.categoryPage'

type CategoryKey = 'all' | 'directors' | 'genres' | 'series' | 'studios' | 'tags'
type SortKey = 'count' | 'count-asc' | 'name' | 'name-desc'
type EntityType = 'directors' | 'genres' | 'series' | 'studios' | 'tags'
type DisplayEntityCard = EntityCard & { categoryKey?: CategoryKey }

interface CategoryMeta {
  label: string
  apiType: EntityType
  mediaParam: string
  mediaNameParam: string
  noun: string
  icon: ReactNode
}

interface SavedState {
  category?: CategoryKey
  sort?: SortKey
  libraryId?: number
  query?: string
  input?: string
  page?: number
  scrollY?: number
  restoreScroll?: boolean
}

const categories: Record<CategoryKey, CategoryMeta> = {
  all: { label: '全部', apiType: 'genres', mediaParam: 'genreId', mediaNameParam: 'genreName', noun: '标签', icon: <CategoryRoundedIcon/> },
  directors: { label: '导演', apiType: 'directors', mediaParam: 'directorId', mediaNameParam: 'directorName', noun: '导演', icon: <GroupsRoundedIcon/> },
  genres: { label: '标签', apiType: 'genres', mediaParam: 'genreId', mediaNameParam: 'genreName', noun: '标签', icon: <CategoryRoundedIcon/> },
  series: { label: '系列', apiType: 'series', mediaParam: 'seriesId', mediaNameParam: 'seriesName', noun: '系列', icon: <MovieRoundedIcon/> },
  studios: { label: '厂商', apiType: 'studios', mediaParam: 'studioId', mediaNameParam: 'studioName', noun: '厂商', icon: <BusinessRoundedIcon/> },
  tags: { label: '自定义', apiType: 'tags', mediaParam: 'customTagId', mediaNameParam: 'customTagName', noun: '自定义标签', icon: <LocalOfferRoundedIcon/> },
}

const readable = (value: string) => value && !value.includes('\uFFFD') ? value : '名称待修复'

export default function TagCategoriesPage() {
  const saved = useMemo(readSavedState, [])
  const navigate = useNavigate()
  const [items, setItems] = useState<DisplayEntityCard[]>([])
  const [total, setTotal] = useState(0)
  const [libraries, setLibraries] = useState<MediaLibrary[]>([])
  const [category, setCategory] = useState<CategoryKey>(saved.category ?? 'all')
  const [sort, setSort] = useState<SortKey>(saved.sort ?? 'count')
  const [libraryId, setLibraryId] = useState(saved.libraryId ?? 0)
  const [input, setInput] = useState(saved.input ?? '')
  const [query, setQuery] = useState(saved.query ?? '')
  const [page, setPage] = useState(saved.page ?? 1)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const restoreScroll = useRef(saved.restoreScroll === true)
  const pendingScrollY = useRef(saved.scrollY ?? 0)
  const meta = categories[category]

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    if (category === 'all') {
      const keys: CategoryKey[] = ['directors', 'genres', 'series', 'studios', 'tags']
      Promise.all(keys.map(key => bridge.entities(categories[key].apiType, query, sort, 500, 0, libraryId || undefined)
        .then(result => ({ key, result }))))
        .then(results => {
          const merged = sortAllCategory(results.flatMap(({ key, result }) => result.items.map(item => ({ ...item, categoryKey: key }))), sort)
          setTotal(results.reduce((sum, item) => sum + item.result.total, 0))
          setItems(merged.slice((page - 1) * pageSize, page * pageSize))
        })
        .catch((reason: Error) => setError(reason.message))
        .finally(() => setLoading(false))
      return
    }
    bridge.entities(meta.apiType, query, sort, pageSize, (page - 1) * pageSize, libraryId || undefined)
      .then((result) => { setItems(result.items); setTotal(result.total) })
      .catch((reason: Error) => setError(reason.message))
      .finally(() => setLoading(false))
  }, [libraryId, meta.apiType, page, query, sort])

  useEffect(load, [load])
  useEffect(() => { bridge.libraries().then(setLibraries).catch(() => undefined) }, [])
  useEffect(() => {
    window.sessionStorage.setItem(stateKey, JSON.stringify({ category, sort, libraryId, input, query, page }))
  }, [category, input, libraryId, page, query, sort])
  useLayoutEffect(() => {
    if (!restoreScroll.current || loading || error) return
    restoreScroll.current = false
    scrollContainer()?.scrollTo({ top: pendingScrollY.current })
    window.sessionStorage.setItem(stateKey, JSON.stringify({ category, sort, libraryId, input, query, page }))
  }, [category, error, input, libraryId, loading, page, query, sort])

  const selectCategory = (next: CategoryKey) => { setCategory(next); setPage(1) }
  const submit = (event: FormEvent) => { event.preventDefault(); setPage(1); setQuery(input.trim()) }
  const clearFilters = () => { setLibraryId(0); setCategory('all'); setSort('count'); setInput(''); setQuery(''); setPage(1) }
  const open = (item: DisplayEntityCard) => {
    const itemMeta = categories[item.categoryKey ?? category]
    const target = new URLSearchParams({ [itemMeta.mediaParam]: String(item.id), [itemMeta.mediaNameParam]: readable(item.name) })
    const library = libraries.find((value) => value.id === libraryId)
    if (library) {
      target.set('libraryId', String(library.id))
      target.set('libraryName', library.name)
    }
    window.sessionStorage.setItem(stateKey, JSON.stringify({ category, sort, libraryId, input, query, page, scrollY: scrollContainer()?.scrollTop ?? 0, restoreScroll: true }))
    navigate(`/media?${target.toString()}`)
  }
  const dirty = libraryId !== 0 || category !== 'all' || sort !== 'count' || Boolean(query)

  const filters = <Stack component="form" onSubmit={submit} direction={{ xs: 'column', lg: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', lg: 'center' }, flexWrap: 'wrap' }}>
    <TextField select size="small" label="范围" value={libraryId} onChange={(event) => { setPage(1); setLibraryId(Number(event.target.value)) }} sx={{ minWidth: { xs: '100%', sm: 180 }, flex: { lg: '0 0 auto' } }}>
      <MenuItem value={0}>全部标准库</MenuItem>
      {libraries.map((library) => <MenuItem key={library.id} value={library.id}>{library.name}</MenuItem>)}
    </TextField>
    <ToggleButtonGroup exclusive size="small" value={category} onChange={(_, next) => next && selectCategory(next as CategoryKey)} sx={{ flexWrap: 'wrap', gap: 0.75, '& .MuiToggleButtonGroup-grouped': { mx: 0, border: 1, borderColor: 'divider', borderRadius: 1.5, whiteSpace: 'nowrap', px: 1.5, minHeight: 40 } }}>
      {(Object.keys(categories) as CategoryKey[]).map((key) => <ToggleButton key={key} value={key}>{categories[key].label}</ToggleButton>)}
    </ToggleButtonGroup>
    <TextField size="small" value={input} onChange={(event) => setInput(event.target.value)} placeholder={`搜索${meta.noun}`} sx={{ minWidth: { xs: '100%', sm: 180 }, flex: { lg: '1 1 180px' } }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon fontSize="small"/></InputAdornment> } }}/>
    <Button type="submit" variant="contained" sx={{ whiteSpace: 'nowrap' }}>搜索</Button>
    <TextField select size="small" label="排序" value={sort} onChange={(event) => { setPage(1); setSort(event.target.value as SortKey) }} sx={{ minWidth: { xs: '100%', sm: 170 }, ml: { lg: 'auto' } }}>
      <MenuItem value="count">影片数倒序</MenuItem>
      <MenuItem value="count-asc">影片数正序</MenuItem>
      <MenuItem value="name">名称升序</MenuItem>
      <MenuItem value="name-desc">名称降序</MenuItem>
    </TextField>
  </Stack>

  const stats = <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
    <StatusBadge tone="info" label={category === 'all' ? `${total} 个分类项` : `${total} 个${meta.noun}`}/>
    <StatusBadge tone={libraryId ? 'warning' : 'neutral'} label={libraries.find((library) => library.id === libraryId)?.name ?? '全部标准库'}/>
  </Stack>

  return <WorkspacePage title="标签" description="按导演、标签、系列、厂商和自定义标签浏览影片。" stats={stats}
    filters={filters} activeFilterCount={dirty ? 1 : 0} onClearFilters={clearFilters} loading={loading} error={error}
    primaryActions={[refreshAction(load)]}>
    {items.length ? <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill,minmax(260px,1fr))', gap: 1 }}>
      {items.map((item) => <Card key={item.id} variant="outlined" sx={{ minWidth: 0 }}>
        <CardActionArea onClick={() => open(item)}>
          <CardContent sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}>
            <Box sx={{ width: 38, height: 38, flex: '0 0 auto', borderRadius: 1.5, bgcolor: 'action.hover', color: 'primary.main', display: 'grid', placeItems: 'center' }}>{categories[item.categoryKey ?? category].icon}</Box>
            <Box sx={{ minWidth: 0, flex: 1 }}>
              <Typography sx={{ fontWeight: 850, overflowWrap: 'anywhere' }}>{readable(item.name)}</Typography>
              <Typography variant="body2" color="text.secondary">{category === 'all' ? `${categories[item.categoryKey ?? category].noun} · ` : ''}{item.movieCount} 部影片</Typography>
            </Box>
          </CardContent>
        </CardActionArea>
      </Card>)}
    </Box> : <EmptyState title="没有匹配内容" description="当前范围下没有可展示的分类项。"/>}
    {total > pageSize && <Stack sx={{ pt: 3, alignItems: 'center' }}><Pagination count={Math.ceil(total / pageSize)} page={page} onChange={(_, value) => setPage(value)} color="primary"/></Stack>}
  </WorkspacePage>
}

function sortAllCategory(items: DisplayEntityCard[], sort: SortKey) {
  const byName = (a: DisplayEntityCard, b: DisplayEntityCard) => readable(a.name).localeCompare(readable(b.name), 'zh-Hans-CN')
  const byCount = (a: DisplayEntityCard, b: DisplayEntityCard) => b.movieCount - a.movieCount || byName(a, b)
  if (sort === 'count-asc') return [...items].sort((a, b) => a.movieCount - b.movieCount || byName(a, b))
  if (sort === 'name') return [...items].sort(byName)
  if (sort === 'name-desc') return [...items].sort((a, b) => byName(b, a))
  return [...items].sort(byCount)
}

function readSavedState(): SavedState {
  try {
    return JSON.parse(window.sessionStorage.getItem(stateKey) || '{}') as SavedState
  } catch {
    return {}
  }
}

function scrollContainer() {
  return document.querySelector('main') ?? document.documentElement
}
