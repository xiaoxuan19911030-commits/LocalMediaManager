import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded'
import GroupsRoundedIcon from '@mui/icons-material/GroupsRounded'
import LocalOfferRoundedIcon from '@mui/icons-material/LocalOfferRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import { Alert, Avatar, Box, Card, CardActionArea, CardContent, Chip, CircularProgress, InputAdornment, MenuItem, Pagination, Stack, TextField, Typography } from '@mui/material'
import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { MediaCard, MediaCardGrid } from '@/components/MediaCard'
import { EmptyState } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { EntityCard, MediaItem } from '@/types/media'

const pageSize = 48
const readable = (value: string) => value && !value.includes('\uFFFD') ? value : '名称待修复'

export default function EntityPage({ type }: { type: 'actors' | 'tags' }) {
  const actorMode = type === 'actors'; const navigate = useNavigate()
  const [items, setItems] = useState<EntityCard[]>([]); const [total, setTotal] = useState(0); const [page, setPage] = useState(1)
  const [input, setInput] = useState(''); const [query, setQuery] = useState(''); const [sort, setSort] = useState('count'); const [loading, setLoading] = useState(true); const [error, setError] = useState('')
  const [selected, setSelected] = useState<EntityCard>(); const [movies, setMovies] = useState<MediaItem[]>(); const [notice, setNotice] = useState('')
  const load = useCallback(() => { setLoading(true); setError(''); bridge.entities(type, query, sort, pageSize, (page - 1) * pageSize).then((result) => { setItems(result.items); setTotal(result.total) }).catch((reason: Error) => setError(reason.message)).finally(() => setLoading(false)) }, [page, query, sort, type])
  useEffect(load, [load])
  const open = (item: EntityCard) => { setSelected(item); setMovies(undefined); bridge.entityMovies(type, item.id).then((result) => setMovies(result.items)).catch((reason: Error) => setError(reason.message)) }
  const play = (item: MediaItem) => bridge.play(item.dataId).then(() => setNotice(`正在打开：${item.code}`)).catch((reason: Error) => setNotice(reason.message))
  const submit = (event: FormEvent) => { event.preventDefault(); setPage(1); setQuery(input.trim()) }

  if (selected) return <Box><PageHeader title={readable(selected.name)} description={`${selected.movieCount} 部关联影片`} action={<Chip clickable icon={<ArrowBackRoundedIcon/>} label={`返回${actorMode ? '演员' : '标签'}`} onClick={() => { setSelected(undefined); setMovies(undefined) }}/>}/>
    {movies === undefined ? <Box sx={{ minHeight: 300, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : movies.length ? <MediaCardGrid>{movies.map((item) => <MediaCard key={item.dataId} item={item} onPlay={play} onOpen={() => navigate(`/movies/${item.dataId}`)}/>)}</MediaCardGrid> : <EmptyState title="暂无关联影片" description="数据已迁移，但该关联当前没有可展示影片。"/>}</Box>

  return <Box><PageHeader title={actorMode ? '演员' : '标签'} description={actorMode ? '按作品数量或名称浏览演员及其关联影片。' : '浏览自定义标签、兼容标签和影片关联。'}/>
    <Box component="form" onSubmit={submit} sx={{ display: 'flex', gap: 1.25, flexWrap: 'wrap', mb: 2.5 }}><TextField size="small" value={input} onChange={(event) => setInput(event.target.value)} placeholder={actorMode ? '搜索演员' : '搜索标签'} sx={{ minWidth: 260 }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon/></InputAdornment> } }}/><TextField select size="small" value={sort} onChange={(event) => { setPage(1); setSort(event.target.value) }} sx={{ width: 150 }}><MenuItem value="count">作品数量</MenuItem><MenuItem value="name">名称排序</MenuItem></TextField></Box>
    {error && <Alert severity="error">{error}</Alert>}{loading ? <Box sx={{ minHeight: 320, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : items.length ? <Box sx={{ display: 'grid', gridTemplateColumns: actorMode ? 'repeat(auto-fill,minmax(150px,1fr))' : 'repeat(auto-fill,minmax(190px,1fr))', gap: 1.25 }}>
      {items.map((item) => <Card key={item.id}><CardActionArea onClick={() => open(item)} sx={{ height: '100%' }}><CardContent sx={{ display: 'flex', flexDirection: actorMode ? 'column' : 'row', alignItems: 'center', gap: 1.25, textAlign: actorMode ? 'center' : 'left' }}>
        {actorMode ? <Avatar src={item.imageUrl} alt={readable(item.name)} sx={{ width: 82, height: 82, bgcolor: 'action.selected' }}><GroupsRoundedIcon/></Avatar> : <Box sx={{ width: 38, height: 38, borderRadius: 2, bgcolor: 'action.hover', color: 'primary.main', display: 'grid', placeItems: 'center' }}><LocalOfferRoundedIcon/></Box>}
        <Box sx={{ minWidth: 0, flex: 1 }}><Typography noWrap sx={{ fontWeight: 800 }}>{readable(item.name)}</Typography><Typography variant="body2" color="text.secondary">{item.movieCount} 部影片</Typography></Box>
      </CardContent></CardActionArea></Card>)}
    </Box> : <EmptyState title="没有匹配内容" description="尝试清除搜索条件。"/>}
    {total > pageSize && <Stack sx={{ pt: 3, alignItems: 'center' }}><Pagination count={Math.ceil(total / pageSize)} page={page} onChange={(_, value) => setPage(value)} color="primary"/></Stack>}
    {notice && <Alert severity="info" onClose={() => setNotice('')} sx={{ mt: 2 }}>{notice}</Alert>}
  </Box>
}
