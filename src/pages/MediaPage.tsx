import FavoriteBorderRoundedIcon from '@mui/icons-material/FavoriteBorderRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import StarRoundedIcon from '@mui/icons-material/StarRounded'
import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import { Alert, Autocomplete, Button, Dialog, DialogActions, DialogContent, DialogTitle, InputAdornment, MenuItem, Snackbar, Stack, TextField, Typography } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { MovieResultContainer, useMovieActions } from '@/components/workspace/MovieResults'
import { ViewModeToggle, WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { MediaItem, NamedItem } from '@/types/media'
import type { WorkspaceViewMode } from '@/components/workspace/Workspace'

const pageSize = 24

export default function MediaPage() {
  const [items, setItems] = useState<MediaItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [search, setSearch] = useState('')
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState('newest')
  const [view, setView] = useState<WorkspaceViewMode>('grid')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [batchRating, setBatchRating] = useState('5')
  const [ratingDialog, setRatingDialog] = useState(false)
  const [selected, setSelected] = useState<number[]>([])
  const [tagDialog, setTagDialog] = useState(false)
  const [tags, setTags] = useState<NamedItem[]>([])
  const [addTags, setAddTags] = useState<NamedItem[]>([])
  const [removeTags, setRemoveTags] = useState<NamedItem[]>([])
  const movieActions = useMovieActions({ onNotice: setNotice, play: bridge.play })

  const load = useCallback(() => {
    setLoading(true); setError('')
    bridge.videos(pageSize, (page - 1) * pageSize, query, sort)
      .then((result) => { setItems(result.items); setTotal(result.total) })
      .catch((reason: Error) => setError(reason.message))
      .finally(() => setLoading(false))
  }, [page, query, sort])
  useEffect(load, [load])

  const submitSearch = () => { setPage(1); setQuery(search.trim()) }
  const clearFilters = () => { setSearch(''); setQuery(''); setSort('newest'); setPage(1) }
  const batchFavorite = (favorite: boolean) => bridge.setBatchFavorite(selected, favorite).then((result) => { setNotice(result.message); setSelected([]); load() }).catch((reason: Error) => setNotice(reason.message))
  const openBatchTags = () => { setAddTags([]); setRemoveTags([]); setTagDialog(true); bridge.entities('tags', '', 'name', 96, 0).then((result) => setTags(result.items)).catch((reason: Error) => setNotice(reason.message)) }
  const saveBatchTags = () => bridge.updateBatchTags(selected, addTags.map((item) => item.id), removeTags.map((item) => item.id)).then((result) => { setNotice(result.message); setTagDialog(false); setSelected([]); load() }).catch((reason: Error) => setNotice(reason.message))
  const saveBatchRating = () => bridge.setBatchRating(selected, batchRating === '' ? undefined : Number(batchRating), batchRating === '').then((result) => { setNotice(result.message); setRatingDialog(false); setSelected([]); load() }).catch((reason: Error) => setNotice(reason.message))
  const createBatchSync = () => bridge.createBatchSync(selected).then((result) => { setNotice(result.message); setSelected([]) }).catch((reason: Error) => setNotice(reason.message))

  const filters = <Stack component="form" onSubmit={(event) => { event.preventDefault(); submitSearch() }} direction={{ xs: 'column', md: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', md: 'center' }, flexWrap: 'wrap' }}>
    <TextField size="small" value={search} onChange={(event) => setSearch(event.target.value)} placeholder="搜索番号或标题" sx={{ flex: '1 1 280px' }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon /></InputAdornment> } }} />
    <TextField select size="small" value={sort} onChange={(event) => { setPage(1); setSort(event.target.value) }} sx={{ width: 150 }}>
      <MenuItem value="newest">最新导入</MenuItem><MenuItem value="code">番号</MenuItem><MenuItem value="title">标题</MenuItem><MenuItem value="release">发行日期</MenuItem><MenuItem value="rating">个人评分</MenuItem>
    </TextField>
    <Button type="submit" variant="contained">搜索</Button>
    <ViewModeToggle value={view} onChange={setView}/>
  </Stack>

  const stats = selected.length > 0 && <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', p: 1.25, border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: 'action.hover' }}>
    <Typography sx={{ fontWeight: 800 }}>已选择 {selected.length} 部</Typography>
    <Button size="small" startIcon={<FavoriteRoundedIcon/>} onClick={() => batchFavorite(true)}>收藏</Button>
    <Button size="small" startIcon={<FavoriteBorderRoundedIcon/>} onClick={() => batchFavorite(false)}>取消收藏</Button>
    <Button size="small" onClick={openBatchTags}>批量标签</Button>
    <TextField select size="small" label="评分" value={batchRating} onChange={event => setBatchRating(event.target.value)} sx={{ width: 116 }}>
      <MenuItem value="">清除</MenuItem>{[0, 1, 2, 3, 4, 5].map(value => <MenuItem key={value} value={value}>{value}</MenuItem>)}
    </TextField>
    <Button size="small" startIcon={<StarRoundedIcon/>} onClick={() => setRatingDialog(true)}>应用评分</Button>
    <Button size="small" startIcon={<SyncRoundedIcon/>} onClick={createBatchSync}>同步信息</Button>
    <Button size="small" color="inherit" onClick={() => setSelected([])}>取消选择</Button>
  </Stack>

  return <WorkspacePage title="影片墙" description={`共 ${total} 部影片，支持搜索、排序和分页浏览。`} stats={stats} filters={filters} activeFilterCount={[query, sort !== 'newest'].filter(Boolean).length} onClearFilters={clearFilters} loading={loading} error={error}
    primaryActions={[refreshAction(load)]}>
    <MovieResultContainer items={items} total={total} page={page} pageSize={pageSize} onPageChange={setPage} view={view} selectable selectedIds={selected} onSelect={(value, checked) => setSelected((current) => checked ? [...current, value.dataId] : current.filter((id) => id !== value.dataId))} onRatingClick={selected.length > 0 ? () => setRatingDialog(true) : undefined} onPlay={movieActions.playMovie} onOpen={(item) => movieActions.openMovie(item, { search: query, sort })} emptyTitle="暂无影片" emptyDescription="当前媒体库还没有可展示的影片。"/>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice} />
    <Dialog open={ratingDialog} onClose={() => setRatingDialog(false)} fullWidth maxWidth="xs"><DialogTitle>批量评分</DialogTitle><DialogContent><TextField select fullWidth margin="normal" label="评分" value={batchRating} onChange={event => setBatchRating(event.target.value)}><MenuItem value="">清除评分</MenuItem>{[0, 1, 2, 3, 4, 5].map(value => <MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField></DialogContent><DialogActions><Button onClick={() => setRatingDialog(false)}>取消</Button><Button variant="contained" onClick={saveBatchRating}>应用到 {selected.length} 部影片</Button></DialogActions></Dialog>
    <Dialog open={tagDialog} onClose={() => setTagDialog(false)} fullWidth maxWidth="sm"><DialogTitle>批量编辑标签</DialogTitle><DialogContent><Autocomplete multiple options={tags.filter((tag) => !removeTags.some((item) => item.id === tag.id))} value={addTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setAddTags(value)} renderInput={(params) => <TextField {...params} label="添加标签" margin="normal"/>}/><Autocomplete multiple options={tags.filter((tag) => !addTags.some((item) => item.id === tag.id))} value={removeTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setRemoveTags(value)} renderInput={(params) => <TextField {...params} label="解绑标签" margin="normal" helperText="只解除关系，不删除标签"/>}/></DialogContent><DialogActions><Button onClick={() => setTagDialog(false)}>取消</Button><Button variant="contained" disabled={!addTags.length && !removeTags.length} onClick={saveBatchTags}>应用到 {selected.length} 部影片</Button></DialogActions></Dialog>
  </WorkspacePage>
}
