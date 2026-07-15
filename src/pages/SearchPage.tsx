import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import { Alert, Box, Button, Chip, CircularProgress, InputAdornment, Snackbar, Stack, TextField } from '@mui/material'
import { FormEvent, useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { MediaCard, MediaCardGrid } from '@/components/MediaCard'
import { EmptyState, SectionTitle } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { GlobalSearchResult, MediaItem } from '@/types/media'

export default function SearchPage() {
  const [params, setParams] = useSearchParams(); const query = params.get('q') || ''; const navigate = useNavigate()
  const [input, setInput] = useState(query); const [result, setResult] = useState<GlobalSearchResult>(); const [loading, setLoading] = useState(false); const [error, setError] = useState(''); const [notice, setNotice] = useState('')
  useEffect(() => { setInput(query); if (!query) { setResult(undefined); return } setLoading(true); setError(''); bridge.search(query).then(setResult).catch((reason: Error) => setError(reason.message)).finally(() => setLoading(false)) }, [query])
  const submit = (event: FormEvent) => { event.preventDefault(); const value=input.trim(); setParams(value ? { q:value } : {}) }
  const play = (item: MediaItem) => bridge.play(item.dataId).then(() => setNotice(`正在打开：${item.code}`)).catch((reason: Error) => setNotice(reason.message))
  return <Box>
    <PageHeader title="全局搜索" description="同时查找影片、演员和标签。"/>
    <Box component="form" onSubmit={submit} sx={{ display:'flex',gap:1,mb:3,maxWidth:760 }}><TextField autoFocus fullWidth value={input} onChange={(e)=>setInput(e.target.value)} placeholder="输入番号、标题、演员或标签"
      slotProps={{ input:{ startAdornment:<InputAdornment position="start"><SearchRoundedIcon/></InputAdornment> } }}/><Button type="submit" variant="contained" sx={{ px:3 }}>搜索</Button></Box>
    {error && <Alert severity="error">{error}</Alert>}{loading && <Box sx={{ minHeight:260,display:'grid',placeItems:'center' }}><CircularProgress/></Box>}
    {!query && !loading && <EmptyState title="搜索你的媒体库" description="输入番号、影片标题、演员或标签开始查找。"/>}
    {result && !loading && <Stack spacing={3}>
      <Box><SectionTitle title={`影片（${result.movies.length}）`}/>{result.movies.length ? <MediaCardGrid>{result.movies.map(item=><MediaCard key={item.dataId} item={item} onPlay={play} onOpen={()=>navigate(`/movies/${item.dataId}`)}/>)}</MediaCardGrid> : <EmptyState title="没有匹配影片" description="可以尝试更短的关键词。"/>}</Box>
      {(result.actors.length>0||result.tags.length>0)&&<Box sx={{ display:'grid',gridTemplateColumns:{xs:'1fr',md:'1fr 1fr'},gap:2 }}>
        <Box><SectionTitle title="演员"/><Stack direction="row" useFlexGap spacing={1} sx={{flexWrap:'wrap'}}>{result.actors.map(item=><Chip key={item.id} label={`${item.name} · ${item.movieCount}`}/>)}</Stack></Box>
        <Box><SectionTitle title="标签"/><Stack direction="row" useFlexGap spacing={1} sx={{flexWrap:'wrap'}}>{result.tags.map(item=><Chip key={item.id} color="primary" variant="outlined" label={`${item.name} · ${item.movieCount}`}/>)}</Stack></Box>
      </Box>}
    </Stack>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={()=>setNotice('')} message={notice}/>
  </Box>
}
