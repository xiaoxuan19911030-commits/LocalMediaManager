import FilterAltRoundedIcon from '@mui/icons-material/FilterAltRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import { Alert, Box, Button, Chip, CircularProgress, FormControlLabel, InputAdornment, MenuItem, Pagination, Snackbar, Stack, Switch, TextField } from '@mui/material'
import { FormEvent, useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { MediaCard, MediaCardGrid } from '@/components/MediaCard'
import { EmptyState, SectionTitle, SurfaceSection } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { GlobalSearchResult, MediaItem, MediaLibrary } from '@/types/media'

const pageSize = 48
export default function SearchPage() {
  const [params, setParams] = useSearchParams(); const navigate = useNavigate(); const query = params.get('q') || ''; const page = Math.max(1, Number(params.get('page') || 1))
  const [input, setInput] = useState(query); const [items, setItems] = useState<MediaItem[]>(); const [total, setTotal] = useState(0); const [related, setRelated] = useState<GlobalSearchResult>(); const [libraries, setLibraries] = useState<MediaLibrary[]>([])
  const [loading, setLoading] = useState(false); const [error, setError] = useState(''); const [notice, setNotice] = useState('')
  const [favorite, setFavorite] = useState(false); const [rating, setRating] = useState(0); const [metadata, setMetadata] = useState('all'); const [fileStatus, setFileStatus] = useState('all'); const [libraryId, setLibraryId] = useState(0); const [sort, setSort] = useState('newest')
  useEffect(() => { bridge.libraries().then(setLibraries).catch(() => undefined) }, [])
  const signature = useMemo(() => [query, page, favorite, rating, metadata, fileStatus, libraryId, sort].join('|'), [query, page, favorite, rating, metadata, fileStatus, libraryId, sort])
  useEffect(() => { setInput(query); if (!query && !favorite && !rating && metadata === 'all' && fileStatus === 'all' && !libraryId) { setItems(undefined); setTotal(0); setRelated(undefined); return }
    setLoading(true); setError(''); Promise.all([
      bridge.advancedSearch({ query, favorite: favorite ? true : undefined, ratingMin: rating, metadata, fileStatus, libraryId: libraryId || undefined, sort, limit: pageSize, offset: (page - 1) * pageSize }),
      query ? bridge.search(query) : Promise.resolve(undefined),
    ]).then(([result, entities]) => { setItems(result.items); setTotal(result.total); setRelated(entities) }).catch((reason: Error) => setError(reason.message)).finally(() => setLoading(false))
  }, [signature])
  const updatePage = (next: number) => { const value = new URLSearchParams(params); value.set('page', String(next)); setParams(value) }
  const submit = (event: FormEvent) => { event.preventDefault(); const value = new URLSearchParams(params); const text = input.trim(); if (text) value.set('q', text); else value.delete('q'); value.delete('page'); setParams(value) }
  const play = (item: MediaItem) => bridge.play(item.dataId).then(() => setNotice(`正在打开：${item.code}`)).catch((reason: Error) => setNotice(reason.message))
  const active = Boolean(query || favorite || rating || metadata !== 'all' || fileStatus !== 'all' || libraryId)
  return <Box><PageHeader title="搜索 2.0" description="通过 Bridge 组合搜索影片、收藏、评分、元数据、文件状态和媒体库。"/>
    <SurfaceSection title="搜索条件" description="所有筛选均在 Bridge 中查询，React 不直接访问 SQLite。"><Box component="form" onSubmit={submit} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(280px,2fr) repeat(5,minmax(120px,1fr)) auto' }, gap: 1.25, alignItems: 'center' }}>
      <TextField size="small" value={input} onChange={event => setInput(event.target.value)} placeholder="番号、标题、演员或标签" slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon/></InputAdornment> } }}/>
      <TextField select size="small" label="最低评分" value={rating} onChange={event => { setRating(Number(event.target.value)); updatePage(1) }}><MenuItem value={0}>全部评分</MenuItem>{[1,2,3,4,5].map(value => <MenuItem key={value} value={value}>{value} 星及以上</MenuItem>)}</TextField>
      <TextField select size="small" label="元数据" value={metadata} onChange={event => { setMetadata(event.target.value); updatePage(1) }}><MenuItem value="all">全部</MenuItem><MenuItem value="complete">已刮削</MenuItem><MenuItem value="missing">缺少信息</MenuItem></TextField>
      <TextField select size="small" label="文件" value={fileStatus} onChange={event => { setFileStatus(event.target.value); updatePage(1) }}><MenuItem value="all">全部</MenuItem><MenuItem value="available">文件存在</MenuItem><MenuItem value="missing">文件缺失</MenuItem></TextField>
      <TextField select size="small" label="媒体库" value={libraryId} onChange={event => { setLibraryId(Number(event.target.value)); updatePage(1) }}><MenuItem value={0}>全部媒体库</MenuItem>{libraries.map(item => <MenuItem key={item.id} value={item.id}>{item.name}</MenuItem>)}</TextField>
      <TextField select size="small" label="排序" value={sort} onChange={event => { setSort(event.target.value); updatePage(1) }}><MenuItem value="newest">最新导入</MenuItem><MenuItem value="code">番号</MenuItem><MenuItem value="rating">评分</MenuItem></TextField>
      <Button type="submit" variant="contained" startIcon={<FilterAltRoundedIcon/>}>搜索</Button>
      <FormControlLabel sx={{ gridColumn: { lg: '1 / -1' }, ml: 0 }} control={<Switch checked={favorite} onChange={event => { setFavorite(event.target.checked); updatePage(1) }}/>} label="仅显示已收藏"/>
    </Box></SurfaceSection>
    {error && <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert>}{loading && <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box>}
    {!active && !loading && <Box sx={{ mt: 2 }}><EmptyState title="搜索你的媒体库" description="输入关键词，或直接选择评分、收藏、元数据、文件状态和媒体库条件。"/></Box>}
    {items && !loading && <Stack spacing={3} sx={{ mt: 2.5 }}><Box><SectionTitle title={`影片（${total}）`}/>{items.length ? <MediaCardGrid>{items.map(item => <MediaCard key={item.dataId} item={item} onPlay={play} onOpen={() => navigate(`/movies/${item.dataId}`)}/>)}</MediaCardGrid> : <EmptyState title="没有匹配影片" description="尝试减少筛选条件或使用更短的关键词。"/>}</Box>
      {related && (related.actors.length > 0 || related.tags.length > 0) && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2 }}><Box><SectionTitle title="演员"/><Stack direction="row" useFlexGap spacing={1} sx={{ flexWrap: 'wrap' }}>{related.actors.map(item => <Chip clickable key={item.id} label={`${item.name} · ${item.movieCount}`} onClick={() => navigate('/actors')}/>)}</Stack></Box><Box><SectionTitle title="标签"/><Stack direction="row" useFlexGap spacing={1} sx={{ flexWrap: 'wrap' }}>{related.tags.map(item => <Chip clickable key={item.id} color="primary" variant="outlined" label={`${item.name} · ${item.movieCount}`} onClick={() => navigate('/tags')}/>)}</Stack></Box></Box>}
      {total > pageSize && <Box sx={{ display: 'flex', justifyContent: 'center' }}><Pagination count={Math.ceil(total / pageSize)} page={page} onChange={(_, value) => updatePage(value)} color="primary"/></Box>}
    </Stack>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
