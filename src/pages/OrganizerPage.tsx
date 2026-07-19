import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import DriveFileMoveRoundedIcon from '@mui/icons-material/DriveFileMoveRounded'
import ExpandLessRoundedIcon from '@mui/icons-material/ExpandLessRounded'
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import LabelRoundedIcon from '@mui/icons-material/LabelRounded'
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded'
import StarRoundedIcon from '@mui/icons-material/StarRounded'
import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import { Alert, Autocomplete, Box, Button, Card, CardContent, Checkbox, Chip, Collapse, Dialog, DialogActions, DialogContent, DialogTitle, Divider, List, ListItemButton, ListItemIcon, ListItemText, MenuItem, Paper, Snackbar, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { SmartImage } from '@/components/SmartImage'
import { MovieWall, type MovieWallRenderContext } from '@/components/workspace/MovieWall'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { BRIDGE_ORIGIN, bridge } from '@/services/bridge'
import type { DuplicateGroup, DuplicateMovie, DuplicateResults, MediaItem, NamedItem } from '@/types/media'

type OrganizerMode = 'duplicates' | 'batch'
type DuplicateRule = 'all' | 'code' | 'path' | 'hash'

export default function OrganizerPage() {
  const [mode, setMode] = useState<OrganizerMode>(readOrganizerMode)
  useEffect(() => { window.sessionStorage.setItem('lmm.organizer.mode', mode) }, [mode])
  return <OrganizerShell mode={mode} onModeChange={setMode}>
    {mode === 'batch' ? <BatchOrganizerView/> : <DuplicateOrganizerView/>}
  </OrganizerShell>
}

function OrganizerShell({ mode, onModeChange, children }: { mode: OrganizerMode; onModeChange: (mode: OrganizerMode) => void; children: ReactNode }) {
  const items = [
    { mode: 'duplicates' as const, label: '重复影片', icon: <ContentCopyRoundedIcon fontSize="small"/> },
    { mode: 'batch' as const, label: '批量整理', icon: <DriveFileMoveRoundedIcon fontSize="small"/> },
  ]
  return <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '220px minmax(0,1fr)' }, gap: 2, alignItems: 'start' }}>
    <Paper variant="outlined" sx={{ p: 1.25, borderRadius: 3, position: { lg: 'sticky' }, top: 16 }}>
      <Typography variant="overline" color="text.secondary" sx={{ px: 1, fontWeight: 900 }}>整理工具</Typography>
      <Divider sx={{ my: 1 }}/>
      <List disablePadding>
        {items.map((item) => <ListItemButton key={item.mode} selected={mode === item.mode} onClick={() => onModeChange(item.mode)} sx={{ borderRadius: 2, mb: .5 }}>
          <ListItemIcon sx={{ minWidth: 34 }}>{item.icon}</ListItemIcon>
          <ListItemText primary={<Typography sx={{ fontWeight: mode === item.mode ? 900 : 700 }}>{item.label}</Typography>}/>
        </ListItemButton>)}
      </List>
    </Paper>
    <Box sx={{ minWidth: 0 }}>{children}</Box>
  </Box>
}

function DuplicateOrganizerView() {
  const navigate = useNavigate()
  const [rule, setRule] = useState<DuplicateRule>('all')
  const [data, setData] = useState<DuplicateResults>()
  const [error, setError] = useState('')
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})
  const [selected, setSelected] = useState<number[]>([])
  const load = useCallback(() => {
    setData(undefined)
    setError('')
    bridge.duplicates(rule).then((result) => {
      setData(result)
      setExpanded((current) => {
        const next = { ...current }
        for (const group of result.groups) next[groupKey(group)] ??= true
        return next
      })
    }).catch((reason: Error) => setError(reason.message))
  }, [rule])
  useEffect(load, [load])

  const stats = data && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(160px,1fr))', gap: 1.5 }}>
    <StatCard label="重复组" value={data.totalGroups} icon={<ContentCopyRoundedIcon/>} tone={data.totalGroups ? 'warning.main' : 'success.main'}/>
    <StatCard label="涉及影片" value={data.totalMovies} icon={<ContentCopyRoundedIcon/>} tone={data.totalMovies ? 'warning.main' : 'success.main'}/>
    <StatCard label="编号" value={data.codeGroups} icon={<ContentCopyRoundedIcon/>}/>
    <StatCard label="文件" value={data.pathGroups} icon={<ContentCopyRoundedIcon/>}/>
    <StatCard label="Hash" value={data.hashGroups} icon={<ContentCopyRoundedIcon/>}/>
  </Box>

  const filters = <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', sm: 'center' }, flexWrap: 'wrap' }}>
    <TextField select size="small" label="重复规则" value={rule} onChange={event => setRule(event.target.value as DuplicateRule)} sx={{ width: { xs: '100%', sm: 190 } }}>
      <MenuItem value="all">全部真实规则</MenuItem>
      <MenuItem value="code">编号重复</MenuItem>
      <MenuItem value="path">文件重复</MenuItem>
      <MenuItem value="hash">Hash 重复</MenuItem>
    </TextField>
  </Stack>

  return <WorkspacePage title="重复影片" description="按真实规则列出可能重复的影片。当前页面只辅助查看、选择和判断，不执行删除。"
    stats={stats} filters={filters} activeFilterCount={rule === 'all' ? 0 : 1} onClearFilters={() => setRule('all')}
    loading={!data && !error} error={error} primaryActions={[refreshAction(load, '重新扫描')]}>
    {data && (data.groups.length === 0 ? <EmptyState title="未发现重复影片" description="当前规则下没有重复编号、重复文件路径或重复 Hash。"/> :
      <Stack spacing={1.5}>{data.groups.map(group => <DuplicateGroupCard key={groupKey(group)} group={group} expanded={expanded[groupKey(group)] ?? true}
        selected={selected} onExpandedChange={(open) => setExpanded((current) => ({ ...current, [groupKey(group)]: open }))}
        onSelect={(ids, checked) => setSelected((current) => checked ? unique([...current, ...ids]) : current.filter((id) => !ids.includes(id)))}
        onOpen={(id) => navigate(`/movies/${id}`)}/>)}</Stack>)}
  </WorkspacePage>
}

function DuplicateGroupCard({ group, expanded, selected, onExpandedChange, onSelect, onOpen }: {
  group: DuplicateGroup
  expanded: boolean
  selected: number[]
  onExpandedChange: (open: boolean) => void
  onSelect: (ids: number[], checked: boolean) => void
  onOpen: (id: number) => void
}) {
  const ids = group.items.map((item) => item.movieId)
  const selectedCount = ids.filter((id) => selected.includes(id)).length
  const recommended = group.items.find((item) => item.recommendation === '建议保留')
  return <Card variant="outlined" sx={{ borderRadius: 3 }}>
    <CardContent>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', md: 'center' }, justifyContent: 'space-between' }}>
        <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', minWidth: 0 }}>
          <StatusBadge label={duplicateReasonLabel(group.rule)} tone={group.rule === 'hash' ? 'warning' : 'info'}/>
          <Typography sx={{ fontWeight: 900, overflowWrap: 'anywhere' }}>{group.key}</Typography>
          <StatusBadge label={`${group.count} 部`} tone="neutral"/>
          {selectedCount > 0 && <StatusBadge label={`已选 ${selectedCount}`} tone="success"/>}
        </Stack>
        <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', justifyContent: { xs: 'flex-start', md: 'flex-end' } }}>
          <Button size="small" variant="outlined" onClick={() => onExpandedChange(!expanded)} startIcon={expanded ? <ExpandLessRoundedIcon/> : <ExpandMoreRoundedIcon/>}>{expanded ? '收起' : '展开'}</Button>
          <Button size="small" variant="outlined" onClick={() => onSelect(ids, true)}>全选</Button>
          <Button size="small" color="inherit" variant="outlined" disabled={selectedCount === 0} onClick={() => onSelect(ids, false)}>取消</Button>
        </Stack>
      </Stack>
      {recommended && <Alert severity="info" icon={<CheckCircleRoundedIcon/>} sx={{ mt: 1.25 }}>
        <Stack spacing={.75}>
          <Typography sx={{ fontWeight: 850 }}>建议保留：{recommended.code || recommended.title || `ID ${recommended.movieId}`}</Typography>
          <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap' }}>
            {recommended.recommendationReasons.map((reason) => <Chip key={reason} size="small" label={`✓ ${reason}`}/>)}
          </Stack>
        </Stack>
      </Alert>}
      <Collapse in={expanded} unmountOnExit>
        <Stack spacing={1} sx={{ mt: 1.25 }}>{group.items.map((item) => <DuplicateMovieRow key={item.movieId} item={item} rule={group.rule} selected={selected.includes(item.movieId)}
          onSelectedChange={(checked) => onSelect([item.movieId], checked)} onOpen={() => onOpen(item.movieId)}/>)}</Stack>
      </Collapse>
    </CardContent>
  </Card>
}

function DuplicateMovieRow({ item, rule, selected, onSelectedChange, onOpen }: {
  item: DuplicateMovie
  rule: DuplicateGroup['rule']
  selected: boolean
  onSelectedChange: (checked: boolean) => void
  onOpen: () => void
}) {
  return <Paper variant="outlined" sx={{ p: 1, borderRadius: 2, bgcolor: selected ? 'action.selected' : 'background.paper' }}>
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'auto 76px minmax(0,1fr)', md: 'auto 88px minmax(210px,1.1fr) minmax(220px,1fr) minmax(260px,1.2fr) auto' }, gap: 1.25, alignItems: 'center' }}>
      <Checkbox checked={selected} onChange={(event) => onSelectedChange(event.target.checked)} slotProps={{ input: { 'aria-label': `选择 ${item.code || item.title || item.movieId}` } }}/>
      <DuplicatePoster movieId={item.movieId} title={item.title || item.code}/>
      <Stack sx={{ minWidth: 0 }}>
        <Typography sx={{ fontWeight: 900 }} noWrap>{item.title || '未命名影片'}</Typography>
        <Typography variant="body2" color="text.secondary" noWrap>{item.code || `ID ${item.movieId}`}</Typography>
        <Typography variant="caption" color="text.secondary" noWrap>{item.fileName || fileNameFromPath(item.filePath) || '无文件名'}</Typography>
      </Stack>
      <Stack sx={{ minWidth: 0, display: { xs: 'none', md: 'flex' } }}>
        <Typography variant="body2">大小：{formatFileSize(item.fileSize)}</Typography>
        <Typography variant="body2">分辨率：{resolutionText(item.resolutionWidth, item.resolutionHeight)}</Typography>
        <Typography variant="body2">评分：{ratingText(item)}</Typography>
        <Typography variant="body2">收藏：{item.favorite ? '是' : '否'}</Typography>
      </Stack>
      <Stack sx={{ minWidth: 0, display: { xs: 'none', md: 'flex' } }}>
        <Typography variant="body2" noWrap>媒体库：{item.libraryName || item.sourceType || '未归属'}</Typography>
        <Tooltip title={item.filePath || '无文件路径'}><Typography variant="body2" color="text.secondary" noWrap>路径：{item.filePath || '无文件路径'}</Typography></Tooltip>
        <Typography variant="body2" color="text.secondary">重复原因：{duplicateReasonLabel(rule)}</Typography>
      </Stack>
      <Button size="small" variant="outlined" startIcon={<OpenInNewRoundedIcon/>} onClick={onOpen} sx={{ gridColumn: { xs: '2 / 4', md: 'auto' }, justifySelf: { xs: 'start', md: 'end' } }}>详情</Button>
    </Box>
  </Paper>
}

function DuplicatePoster({ movieId, title }: { movieId: number; title: string }) {
  const [failed, setFailed] = useState(false)
  const src = `${BRIDGE_ORIGIN}/api/images/${movieId}/primary`
  return <Box sx={{ width: 88, aspectRatio: '2 / 3', borderRadius: 1.5, overflow: 'hidden', bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}>
    {failed ? <Typography variant="caption" color="text.disabled">暂无海报</Typography> : <SmartImage src={src} alt={title} onError={() => setFailed(true)}/>}
  </Box>
}

function BatchOrganizerView() {
  const [selected, setSelected] = useState<number[]>([])
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState('')
  const [reloadSignal, setReloadSignal] = useState(0)
  const [rating, setRating] = useState('5')
  const [ratingDialog, setRatingDialog] = useState(false)
  const [favoriteDialog, setFavoriteDialog] = useState(false)
  const [tagDialog, setTagDialog] = useState(false)
  const [tags, setTags] = useState<NamedItem[]>([])
  const [addTags, setAddTags] = useState<NamedItem[]>([])
  const [removeTags, setRemoveTags] = useState<NamedItem[]>([])
  const refresh = () => setReloadSignal((value) => value + 1)
  const toggleSelected = (item: MediaItem, checked: boolean) => setSelected((current) => checked ? unique([...current, item.dataId]) : current.filter((id) => id !== item.dataId))
  const clearSelection = () => setSelected([])
  const completeMutation = (message: string) => {
    setNotice(message)
    clearSelection()
    refresh()
  }
  const openBatchTags = () => {
    setAddTags([])
    setRemoveTags([])
    setTagDialog(true)
    bridge.entities('tags', '', 'name', 96, 0).then((result) => setTags(result.items)).catch((reason: Error) => setNotice(reason.message))
  }
  const applyFavorite = (favorite: boolean) => {
    if (!selected.length || busy) return
    setBusy('favorite')
    bridge.setBatchFavorite(selected, favorite).then((result) => { setFavoriteDialog(false); completeMutation(result.message) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(''))
  }
  const applyRating = () => {
    if (!selected.length || busy) return
    setBusy('rating')
    bridge.setBatchRating(selected, rating === '' ? undefined : Number(rating), rating === '').then((result) => { setRatingDialog(false); completeMutation(result.message) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(''))
  }
  const applyTags = () => {
    if (!selected.length || busy) return
    setBusy('tags')
    bridge.updateBatchTags(selected, addTags.map((item) => item.id), removeTags.map((item) => item.id)).then((result) => { setTagDialog(false); completeMutation(result.message) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(''))
  }
  const createBatchSync = () => {
    if (!selected.length || busy) return
    setBusy('sync')
    bridge.createBatchSync(selected).then((result) => { setNotice(result.message); clearSelection() }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(''))
  }

  return <>
    <MovieWall title="批量整理" description={(total) => `复用 MovieWall 当前条件筛选 ${total} 部影片。所有批量能力集中在这里，文件移动、重命名和删除后续接入。`}
      stateKey="lmm.organizer.batch" selectable selectedIds={selected} onSelect={toggleSelected} reloadSignal={reloadSignal}
      defaultLabel={<StatusBadge tone={selected.length ? 'info' : 'neutral'} label={selected.length ? `已选择 ${selected.length} 部` : '未选择影片'}/>}
      renderStats={(context) => <BatchActionBar context={context} selected={selected} busy={Boolean(busy)}
        onSelectPage={(items) => setSelected((current) => unique([...current, ...items.map((item) => item.dataId)]))}
        onClear={clearSelection} onTags={openBatchTags} onFavorite={() => setFavoriteDialog(true)} onRating={() => setRatingDialog(true)} onSync={createBatchSync}/>}
      emptyTitle="暂无可整理影片" emptyDescription="当前筛选条件下没有可用于批量整理的影片。"/>
    <Dialog open={favoriteDialog} onClose={() => !busy && setFavoriteDialog(false)} fullWidth maxWidth="xs">
      <DialogTitle>批量收藏</DialogTitle>
      <DialogContent><Alert severity="info">将对已选择的 {selected.length} 部影片应用收藏状态。</Alert></DialogContent>
      <DialogActions>
        <Button disabled={Boolean(busy)} onClick={() => setFavoriteDialog(false)}>取消</Button>
        <Button disabled={Boolean(busy) || selected.length === 0} onClick={() => applyFavorite(false)}>取消收藏</Button>
        <Button variant="contained" disabled={Boolean(busy) || selected.length === 0} onClick={() => applyFavorite(true)}>收藏</Button>
      </DialogActions>
    </Dialog>
    <Dialog open={ratingDialog} onClose={() => !busy && setRatingDialog(false)} fullWidth maxWidth="xs">
      <DialogTitle>批量评分</DialogTitle>
      <DialogContent><TextField select fullWidth margin="normal" label="评分" value={rating} onChange={(event) => setRating(event.target.value)}>
        <MenuItem value="">清除评分</MenuItem>
        {[0, 1, 2, 3, 4, 5].map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}
      </TextField></DialogContent>
      <DialogActions><Button disabled={Boolean(busy)} onClick={() => setRatingDialog(false)}>取消</Button><Button variant="contained" disabled={Boolean(busy) || selected.length === 0} onClick={applyRating}>应用到 {selected.length} 部影片</Button></DialogActions>
    </Dialog>
    <Dialog open={tagDialog} onClose={() => !busy && setTagDialog(false)} fullWidth maxWidth="sm">
      <DialogTitle>批量标签</DialogTitle>
      <DialogContent>
        <Autocomplete multiple options={tags.filter((tag) => !removeTags.some((item) => item.id === tag.id))} value={addTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setAddTags(value)} renderInput={(params) => <TextField {...params} label="添加标签" margin="normal"/>}/>
        <Autocomplete multiple options={tags.filter((tag) => !addTags.some((item) => item.id === tag.id))} value={removeTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setRemoveTags(value)} renderInput={(params) => <TextField {...params} label="移除标签" margin="normal" helperText="只解除关系，不删除标签。"/>}/>
      </DialogContent>
      <DialogActions><Button disabled={Boolean(busy)} onClick={() => setTagDialog(false)}>取消</Button><Button variant="contained" disabled={Boolean(busy) || selected.length === 0 || (!addTags.length && !removeTags.length)} onClick={applyTags}>应用到 {selected.length} 部影片</Button></DialogActions>
    </Dialog>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </>
}

function BatchActionBar({ context, selected, busy, onSelectPage, onClear, onTags, onFavorite, onRating, onSync }: {
  context: MovieWallRenderContext
  selected: number[]
  busy: boolean
  onSelectPage: (items: MediaItem[]) => void
  onClear: () => void
  onTags: () => void
  onFavorite: () => void
  onRating: () => void
  onSync: () => void
}) {
  const disabled = selected.length === 0 || busy
  return <Paper variant="outlined" sx={{ p: 1.25, borderRadius: 2, bgcolor: 'action.hover' }}>
    <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
      <Typography sx={{ fontWeight: 900, mr: .5 }}>批量操作</Typography>
      <Button size="small" variant="outlined" onClick={() => onSelectPage(context.items)} disabled={context.items.length === 0 || busy}>全选</Button>
      <Button size="small" color="inherit" variant="outlined" onClick={onClear} disabled={selected.length === 0 || busy}>取消</Button>
      <Button size="small" variant="outlined" startIcon={<LabelRoundedIcon/>} onClick={onTags} disabled={disabled}>批量标签</Button>
      <Button size="small" variant="outlined" startIcon={<FavoriteRoundedIcon/>} onClick={onFavorite} disabled={disabled}>批量收藏</Button>
      <Button size="small" variant="outlined" startIcon={<StarRoundedIcon/>} onClick={onRating} disabled={disabled}>批量评分</Button>
      <Button size="small" variant="outlined" startIcon={<SyncRoundedIcon/>} onClick={onSync} disabled={disabled}>批量同步</Button>
      <Button size="small" variant="outlined" startIcon={<DriveFileMoveRoundedIcon/>} disabled>批量移动</Button>
      <Button size="small" variant="outlined" disabled>批量重命名</Button>
      <Button size="small" variant="outlined" color="error" startIcon={<DeleteOutlineRoundedIcon/>} disabled>批量删除</Button>
    </Stack>
  </Paper>
}

function duplicateReasonLabel(rule: DuplicateGroup['rule']) {
  if (rule === 'code') return '编号重复'
  if (rule === 'path') return '文件重复'
  if (rule === 'hash') return 'Hash 重复'
  return '数据库重复'
}

function groupKey(group: DuplicateGroup) {
  return `${group.rule}:${group.key}`
}

function unique(values: number[]) {
  return [...new Set(values)]
}

function formatFileSize(value: number) {
  if (!value) return '未知'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let size = value
  let index = 0
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024
    index += 1
  }
  return `${size.toFixed(index === 0 ? 0 : 1)} ${units[index]}`
}

function resolutionText(width: number, height: number) {
  return width > 0 && height > 0 ? `${width}x${height}` : '未知'
}

function ratingText(item: DuplicateMovie) {
  return item.userRatingSet ? `${item.userRating.toFixed(1)} / 5` : '未评分'
}

function fileNameFromPath(path: string) {
  return path.split(/[\\/]/).filter(Boolean).pop() ?? ''
}

function readOrganizerMode(): OrganizerMode {
  return window.sessionStorage.getItem('lmm.organizer.mode') === 'batch' ? 'batch' : 'duplicates'
}
