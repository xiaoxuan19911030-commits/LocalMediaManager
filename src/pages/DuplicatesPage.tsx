import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded'
import PlaceRoundedIcon from '@mui/icons-material/PlaceRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, MenuItem, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { DuplicateGroup, DuplicateResults } from '@/types/media'

const ruleLabels: Record<DuplicateGroup['rule'], string> = { code: '番号重复', path: '文件路径重复', hash: '文件 Hash 重复' }

export default function DuplicatesPage() {
  const navigate = useNavigate()
  const [rule, setRule] = useState<'all' | 'code' | 'path' | 'hash'>('all')
  const [data, setData] = useState<DuplicateResults>()
  const [error, setError] = useState('')
  const load = useCallback(() => {
    setData(undefined); setError('')
    bridge.duplicates(rule).then(setData).catch((reason: Error) => setError(reason.message))
  }, [rule])
  useEffect(load, [load])
  const locate = (code: string, title: string) => {
    const query = (code || title).trim()
    navigate(`/search?q=${encodeURIComponent(query)}`)
  }
  return <Box>
    <PageHeader title="查重结果" description="只读扫描数据库中的重复番号、文件路径和已有文件 Hash，不执行删除或合并。"
      action={<Button variant="outlined" startIcon={<RefreshRoundedIcon/>} onClick={load}>重新扫描</Button>}/>
    <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 2 }}>
      <TextField select size="small" label="重复类型" value={rule} onChange={event => setRule(event.target.value as typeof rule)} sx={{ width: 180 }}>
        <MenuItem value="all">全部重复项</MenuItem>
        <MenuItem value="code">番号重复</MenuItem>
        <MenuItem value="path">文件路径重复</MenuItem>
        <MenuItem value="hash">文件 Hash 重复</MenuItem>
      </TextField>
    </Box>
    {error && <Alert severity="error" sx={{ mb: 2 }}>查重失败：{error}</Alert>}
    {!data && !error ? <Box sx={{ minHeight: 320, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : data && <>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1.5, mb: 2.5 }}>
        <StatCard label="重复组" value={data.totalGroups} icon={<ContentCopyRoundedIcon/>} tone={data.totalGroups ? 'warning.main' : 'success.main'}/>
        <StatCard label="涉及影片" value={data.totalMovies} icon={<ContentCopyRoundedIcon/>} tone={data.totalMovies ? 'warning.main' : 'success.main'}/>
        <StatCard label="番号" value={data.codeGroups} icon={<ContentCopyRoundedIcon/>}/>
        <StatCard label="路径" value={data.pathGroups} icon={<ContentCopyRoundedIcon/>}/>
        <StatCard label="Hash" value={data.hashGroups} icon={<ContentCopyRoundedIcon/>}/>
      </Box>
      {data.groups.length === 0 ? <EmptyState title="未发现重复项" description="当前筛选范围内没有重复番号、路径或已有文件 Hash。"/> :
        <Stack spacing={1.5}>{data.groups.map(group => <Card key={`${group.rule}:${group.key}`}><CardContent>
          <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1.25 }}>
            <Chip color={group.rule === 'code' ? 'primary' : group.rule === 'path' ? 'warning' : 'info'} variant="outlined" label={ruleLabels[group.rule]}/>
            <Typography sx={{ fontWeight: 800, minWidth: 0, overflowWrap: 'anywhere' }}>{group.key}</Typography>
            <Chip size="small" label={`${group.count} 部`}/>
          </Stack>
          <Stack spacing={1}>{group.items.map(item => <Box key={item.movieId} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(150px,.8fr) minmax(180px,1fr) minmax(220px,1.4fr) auto' }, gap: 1.25, alignItems: 'center', py: .75, borderTop: 1, borderColor: 'divider' }}>
            <Box sx={{ minWidth: 0 }}><Typography noWrap sx={{ fontWeight: 750 }}>{item.code || `#${item.movieId}`}</Typography><Typography variant="caption" color="text.secondary">ID {item.movieId}</Typography></Box>
            <Typography noWrap title={item.title || item.code}>{item.title || '未命名影片'}</Typography>
            <Tooltip title={item.fileHash || item.filePath || '无文件信息'}><Typography variant="body2" color="text.secondary" noWrap>{group.rule === 'hash' ? item.fileHash : item.filePath}</Typography></Tooltip>
            <Stack direction="row" spacing={.5}>
              <Button size="small" startIcon={<PlaceRoundedIcon/>} onClick={() => locate(item.code, item.title)}>定位</Button>
              <Button size="small" startIcon={<OpenInNewRoundedIcon/>} onClick={() => navigate(`/movies/${item.movieId}`)}>详情</Button>
            </Stack>
          </Box>)}</Stack>
        </CardContent></Card>)}</Stack>}
    </>}
  </Box>
}
