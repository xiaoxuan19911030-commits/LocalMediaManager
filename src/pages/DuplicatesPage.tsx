import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded'
import PlaceRoundedIcon from '@mui/icons-material/PlaceRounded'
import { Box, Button, Card, CardContent, MenuItem, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { DuplicateStatusBadge, StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { DuplicateResults } from '@/types/media'

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
  const locate = (code: string, title: string) => navigate(`/search?q=${encodeURIComponent((code || title).trim())}`)
  const filters = <TextField select size="small" label="重复类型" value={rule} onChange={event => setRule(event.target.value as typeof rule)} sx={{ width: 190 }}>
    <MenuItem value="all">全部重复项</MenuItem><MenuItem value="code">番号重复</MenuItem><MenuItem value="path">文件路径重复</MenuItem><MenuItem value="hash">文件 Hash 重复</MenuItem>
  </TextField>
  const stats = data && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1.5 }}>
    <StatCard label="重复组" value={data.totalGroups} icon={<ContentCopyRoundedIcon/>} tone={data.totalGroups ? 'warning.main' : 'success.main'}/>
    <StatCard label="涉及影片" value={data.totalMovies} icon={<ContentCopyRoundedIcon/>} tone={data.totalMovies ? 'warning.main' : 'success.main'}/>
    <StatCard label="番号" value={data.codeGroups} icon={<ContentCopyRoundedIcon/>}/>
    <StatCard label="路径" value={data.pathGroups} icon={<ContentCopyRoundedIcon/>}/>
    <StatCard label="Hash" value={data.hashGroups} icon={<ContentCopyRoundedIcon/>}/>
  </Box>

  return <WorkspacePage title="查重结果" description="只读扫描数据库中的重复番号、文件路径和已有文件 Hash，不执行删除或合并。" stats={stats} filters={filters} activeFilterCount={rule === 'all' ? 0 : 1} onClearFilters={() => setRule('all')} loading={!data && !error} error={error}
    primaryActions={[refreshAction(load, '重新扫描')]}>
    {data && (data.groups.length === 0 ? <EmptyState title="未发现重复项" description="当前筛选范围内没有重复番号、路径或已有文件 Hash。"/> :
      <Stack spacing={1.5}>{data.groups.map(group => <Card key={`${group.rule}:${group.key}`}><CardContent>
        <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1.25 }}>
          <DuplicateStatusBadge rule={group.rule}/>
          <Typography sx={{ fontWeight: 800, minWidth: 0, overflowWrap: 'anywhere' }}>{group.key}</Typography>
          <StatusBadge label={`${group.count} 部`} tone="neutral"/>
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
      </CardContent></Card>)}</Stack>)}
  </WorkspacePage>
}
