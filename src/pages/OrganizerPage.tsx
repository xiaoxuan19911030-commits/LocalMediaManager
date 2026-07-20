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
import { Alert, Autocomplete, Box, Button, Card, CardContent, Checkbox, Chip, Collapse, Dialog, DialogActions, DialogContent, DialogTitle, Divider, FormControlLabel, List, ListItemButton, ListItemIcon, ListItemText, MenuItem, Paper, Radio, Snackbar, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { SmartImage } from '@/components/SmartImage'
import { MovieWall, type MovieWallRenderContext } from '@/components/workspace/MovieWall'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { BRIDGE_ORIGIN, bridge } from '@/services/bridge'
import type { DuplicateDeleteGroupCommand, DuplicateDeletePreview, DuplicateGroup, DuplicateMovie, DuplicateResults, MediaItem, NamedItem, OrganizerPreview } from '@/types/media'

type OrganizerMode = 'duplicates' | 'batch'
type DuplicateRule = 'all' | 'code' | 'path' | 'hash'
type BatchOrganizerKind = 'move' | 'rename'

export default function OrganizerPage({ search = '' }: { search?: string }) {
  return <DuplicateOrganizerView search={search}/>
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

function DuplicateOrganizerView({ search }: { search: string }) {
  const navigate = useNavigate()
  const [rule, setRule] = useState<DuplicateRule>('all')
  const [data, setData] = useState<DuplicateResults>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState('')
  const [expanded, setExpanded] = useState<Record<string, boolean>>({})
  const [selectedGroups, setSelectedGroups] = useState<string[]>([])
  const [keepByGroup, setKeepByGroup] = useState<Record<string, number>>({})
  const [deletePreview, setDeletePreview] = useState<DuplicateDeletePreview>()
  const [deletePlan, setDeletePlan] = useState<DuplicateDeleteGroupCommand[]>([])
  const [confirmOriginal, setConfirmOriginal] = useState(false)

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
      setKeepByGroup((current) => {
        const next = { ...current }
        for (const group of result.groups) {
          const key = groupKey(group)
          if (!next[key]) next[key] = group.items.find((item) => item.recommendation === '建议保留')?.movieId ?? group.items[0]?.movieId ?? 0
        }
        return next
      })
    }).catch((reason: Error) => setError(reason.message))
  }, [rule])
  useEffect(load, [load])

  const groupsByKey = useMemo(() => new Map((data?.groups ?? []).map((group) => [groupKey(group), group])), [data])
  const visibleGroups = useMemo(() => filterDuplicateGroups(data?.groups ?? [], search), [data, search])
  const buildPlan = (keys = selectedGroups) => keys.map((key) => {
    const group = groupsByKey.get(key)
    return group ? { groupKey: key, keepMovieId: keepByGroup[key] ?? 0, candidateMovieIds: group.items.map((item) => item.movieId) } : undefined
  }).filter(Boolean) as DuplicateDeleteGroupCommand[]
  const previewDuplicateDelete = (keys = selectedGroups) => {
    const plan = buildPlan(keys)
    if (!plan.length) { setNotice('请选择至少一个重复组'); return }
    if (plan.some((item) => !item.keepMovieId)) { setNotice('每个重复组必须选择一个保留版本'); return }
    setBusy('duplicate-preview')
    bridge.previewDuplicateDelete(plan, 'media', true).then((preview) => {
      setDeletePlan(plan)
      setDeletePreview(preview)
      setConfirmOriginal(false)
    }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(''))
  }
  const previewAllUnkept = () => {
    const keys = (data?.groups ?? []).map(groupKey)
    setSelectedGroups(keys)
    previewDuplicateDelete(keys)
  }
  const executeDuplicateDelete = () => {
    if (!deletePreview || busy) return
    setBusy('duplicate-execute')
    bridge.executeDuplicateDelete(deletePlan, 'media', true, deletePreview.confirmationToken, confirmOriginal)
      .then((result) => {
        setNotice(result.message)
        setDeletePreview(undefined)
        setSelectedGroups([])
        load()
      }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(''))
  }

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
    {selectedGroups.length > 0 && <StatusBadge tone="info" label={`已纳入 ${selectedGroups.length} 组`}/>}
  </Stack>

  return <>
    <WorkspacePage title="重复影片" description="按真实规则处理重复候选。每组先选择一个保留版本，其余候选进入 Safe Delete 预览；不会自动删除。"
      stats={stats} filters={filters} activeFilterCount={rule === 'all' ? 0 : 1} onClearFilters={() => setRule('all')}
      loading={!data && !error} error={error}
      primaryActions={[
        { key: 'delete-all-unkept', label: '删除所有未保留影片', icon: <DeleteOutlineRoundedIcon/>, variant: 'contained', color: 'error', disabled: !data?.groups.length || Boolean(busy), onClick: previewAllUnkept },
        { key: 'preview-delete', label: '预览所选', icon: <DeleteOutlineRoundedIcon/>, variant: 'outlined', color: 'error', disabled: selectedGroups.length === 0 || Boolean(busy), onClick: () => previewDuplicateDelete() },
        refreshAction(load, '重新扫描')
      ]}>
      {data && (data.groups.length === 0 ? <EmptyState title="未发现重复影片" description="当前规则下没有重复编号、重复文件路径或重复 Hash。"/> :
        visibleGroups.length === 0 ? <EmptyState title="没有匹配的重复影片" description="当前搜索条件下没有匹配的番号、标题或路径。"/> :
        <Stack spacing={1.5}>{visibleGroups.map(group => {
          const key = groupKey(group)
          return <DuplicateGroupCard key={key} group={group} expanded={expanded[key] ?? true} keepId={keepByGroup[key] ?? 0} selectedForProcessing={selectedGroups.includes(key)}
            onProcessingChange={(checked) => setSelectedGroups((current) => checked ? uniqueStrings([...current, key]) : current.filter((item) => item !== key))}
            onKeepChange={(movieId) => setKeepByGroup((current) => ({ ...current, [key]: movieId }))}
            onExpandedChange={(open) => setExpanded((current) => ({ ...current, [key]: open }))}
            onOpen={(id) => navigate(`/movies/${id}`)}/>
        })}</Stack>)}
    </WorkspacePage>
    <DuplicateDeleteDialog preview={deletePreview} busy={Boolean(busy)} confirmOriginal={confirmOriginal} onConfirmOriginal={setConfirmOriginal} onClose={() => setDeletePreview(undefined)} onExecute={executeDuplicateDelete}/>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </>
}

function DuplicateGroupCard({ group, expanded, keepId, selectedForProcessing, onProcessingChange, onKeepChange, onExpandedChange, onOpen }: {
  group: DuplicateGroup
  expanded: boolean
  keepId: number
  selectedForProcessing: boolean
  onProcessingChange: (checked: boolean) => void
  onKeepChange: (movieId: number) => void
  onExpandedChange: (open: boolean) => void
  onOpen: (id: number) => void
}) {
  const recommended = group.items.find((item) => item.recommendation === '建议保留')
  return <Card variant="outlined" sx={{ borderRadius: 2 }}>
    <CardContent sx={{ py: 1.25, '&:last-child': { pb: 1.25 } }}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', md: 'center' }, justifyContent: 'space-between' }}>
        <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', minWidth: 0 }}>
          <Checkbox checked={selectedForProcessing} onChange={(event) => onProcessingChange(event.target.checked)} slotProps={{ input: { 'aria-label': `纳入处理 ${group.key}` } }}/>
          <StatusBadge label={duplicateReasonLabel(group.rule)} tone={group.rule === 'hash' ? 'warning' : 'info'}/>
          <Typography sx={{ fontWeight: 900, overflowWrap: 'anywhere' }}>{group.key}</Typography>
          <StatusBadge label={`${group.count} 部`} tone="neutral"/>
          {keepId > 0 && <StatusBadge label={`保留 ID ${keepId}`} tone="success"/>}
        </Stack>
        <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', justifyContent: { xs: 'flex-start', md: 'flex-end' } }}>
          <Button size="small" variant="outlined" onClick={() => onExpandedChange(!expanded)} startIcon={expanded ? <ExpandLessRoundedIcon/> : <ExpandMoreRoundedIcon/>}>{expanded ? '收起' : '展开'}</Button>
          <Button size="small" variant="outlined" onClick={() => onProcessingChange(true)}>纳入处理</Button>
          <Button size="small" color="inherit" variant="outlined" disabled={!selectedForProcessing} onClick={() => onProcessingChange(false)}>取消</Button>
        </Stack>
      </Stack>
      {recommended && <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', mt: .75 }}>
        <Chip size="small" icon={<CheckCircleRoundedIcon/>} color="info" label={`建议保留：${recommended.code || recommended.title || `ID ${recommended.movieId}`}`}/>
        {recommended.recommendationReasons.map((reason) => <Chip key={reason} size="small" variant="outlined" label={`✓ ${reason}`}/>)}
      </Stack>}
      <Collapse in={expanded} unmountOnExit>
        <Stack spacing={1} sx={{ mt: 1.25 }}>{group.items.map((item) => <DuplicateMovieRow key={item.movieId} item={item} rule={group.rule} keep={keepId === item.movieId}
          onKeep={() => onKeepChange(item.movieId)} onOpen={() => onOpen(item.movieId)}/>)}</Stack>
      </Collapse>
    </CardContent>
  </Card>
}

function DuplicateMovieRow({ item, rule, keep, onKeep, onOpen }: { item: DuplicateMovie; rule: DuplicateGroup['rule']; keep: boolean; onKeep: () => void; onOpen: () => void }) {
  return <Paper variant="outlined" sx={{ p: 1, borderRadius: 2, bgcolor: keep ? 'action.selected' : 'background.paper' }}>
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'auto 76px minmax(0,1fr)', md: 'auto 88px minmax(210px,1.1fr) minmax(220px,1fr) minmax(260px,1.2fr) auto' }, gap: 1.25, alignItems: 'center' }}>
      <Radio checked={keep} onChange={onKeep} slotProps={{ input: { 'aria-label': `保留 ${item.code || item.title || item.movieId}` } }}/>
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

function DuplicateDeleteDialog({ preview, busy, confirmOriginal, onConfirmOriginal, onClose, onExecute }: {
  preview?: DuplicateDeletePreview
  busy: boolean
  confirmOriginal: boolean
  onConfirmOriginal: (value: boolean) => void
  onClose: () => void
  onExecute: () => void
}) {
  const safe = preview?.safeDelete
  const canExecute = Boolean(preview && safe && (!safe.deletesOriginalMedia || confirmOriginal))
  return <Dialog open={Boolean(preview)} onClose={busy ? undefined : onClose} maxWidth="md" fullWidth>
    <DialogTitle>重复影片 Safe Delete 预览</DialogTitle>
    <DialogContent dividers>
      {preview && safe && <Stack spacing={1.5}>
        <Alert severity={preview.canExecute ? 'warning' : 'error'}>Safe Delete 真实行为：影片文件会移入系统回收站；文件成功进入回收站后才删除对应数据库记录。保留项不会删除。</Alert>
        {preview.blockers.map((blocker) => <Alert key={blocker} severity="error">{blocker}</Alert>)}
        {preview.warnings.map((warning) => <Alert key={warning} severity="info">{warning}</Alert>)}
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(3,minmax(0,1fr))' }, gap: 1.25 }}>
          <Summary label="删除数量" value={`${safe.movieCount}`}/>
          <Summary label="保留数量" value={`${preview.merges.length}`}/>
          <Summary label="释放空间" value={formatFileSize(safe.estimatedBytes)}/>
        </Box>
        <Typography sx={{ fontWeight: 900 }}>用户数据合并</Typography>
        {preview.merges.map((merge) => <Paper key={merge.groupKey} variant="outlined" sx={{ p: 1.25, borderRadius: 2 }}>
          <Typography sx={{ fontWeight: 850 }}>{merge.groupKey} → 保留 ID {merge.keepMovieId}</Typography>
          <Typography variant="body2">待删除：{merge.deleteMovieIds.join(', ')}</Typography>
          <Typography variant="body2">收藏：{merge.favoriteWillMerge ? '将合并为已收藏' : '无需变化'}</Typography>
          <Typography variant="body2">评分：{merge.ratingConflict ? merge.ratingConflictDetail : merge.ratingToApply !== undefined ? `应用 ${merge.ratingToApply}` : '无需变化'}</Typography>
          <Typography variant="body2">标签：{merge.tagsToMerge.length ? merge.tagsToMerge.join('、') : '无'}</Typography>
          <Typography variant="body2">播放次数：{merge.playCountToApply}</Typography>
          <Typography variant="body2">最后播放：{merge.lastPlayedAtToApply || '无'}</Typography>
          {merge.notesConflict && <Alert severity="error" sx={{ mt: 1 }}>{merge.notesConflictDetail}</Alert>}
        </Paper>)}
        <Typography sx={{ fontWeight: 900 }}>Safe Delete 文件预览</Typography>
        {safe.items.map((item) => <Paper key={item.movieId} variant="outlined" sx={{ p: 1.25, borderRadius: 2 }}>
          <Typography sx={{ fontWeight: 850 }}>{item.code} {item.title}</Typography>
          {item.files.filter((file) => file.willDelete).map((file) => <Typography key={`${item.movieId}-${file.kind}-${file.path}`} variant="caption" sx={{ display: 'block', overflowWrap: 'anywhere' }}>{file.kind} · {file.exists ? formatFileSize(file.size) : '不存在'} · {file.path}</Typography>)}
        </Paper>)}
        {safe.deletesOriginalMedia && <Stack spacing={1}>
          <FormControlLabel control={<Checkbox checked={confirmOriginal} onChange={(_, checked) => onConfirmOriginal(checked)}/>} label="我确认将待处理影片文件移入系统回收站"/>
        </Stack>}
      </Stack>}
    </DialogContent>
    <DialogActions><Button onClick={onClose} disabled={busy}>取消</Button><Button color="error" variant="contained" disabled={!canExecute || busy} onClick={onExecute}>确认执行</Button></DialogActions>
  </Dialog>
}

function DuplicatePoster({ movieId, title }: { movieId: number; title: string }) {
  const [failed, setFailed] = useState(false)
  const src = `${BRIDGE_ORIGIN}/api/images/${movieId}/primary`
  return <Box sx={{ width: 64, aspectRatio: '2 / 3', borderRadius: 1.25, overflow: 'hidden', bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}>
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
  const [organizerDialog, setOrganizerDialog] = useState<BatchOrganizerKind>()
  const [organizerTemplate, setOrganizerTemplate] = useState('{Code}')
  const [organizerDestination, setOrganizerDestination] = useState('')
  const [organizerPreview, setOrganizerPreview] = useState<OrganizerPreview>()
  const [tags, setTags] = useState<NamedItem[]>([])
  const [addTags, setAddTags] = useState<NamedItem[]>([])
  const [removeTags, setRemoveTags] = useState<NamedItem[]>([])
  const refresh = () => setReloadSignal((value) => value + 1)
  const toggleSelected = (item: MediaItem, checked: boolean) => setSelected((current) => checked ? unique([...current, item.dataId]) : current.filter((id) => id !== item.dataId))
  const clearSelection = () => setSelected([])
  const completeMutation = (message: string) => { setNotice(message); clearSelection(); refresh() }
  const openBatchTags = () => { setAddTags([]); setRemoveTags([]); setTagDialog(true); bridge.entities('tags', '', 'name', 96, 0).then((result) => setTags(result.items)).catch((reason: Error) => setNotice(reason.message)) }
  const applyFavorite = (favorite: boolean) => { if (!selected.length || busy) return; setBusy('favorite'); bridge.setBatchFavorite(selected, favorite).then((result) => { setFavoriteDialog(false); completeMutation(result.message) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy('')) }
  const applyRating = () => { if (!selected.length || busy) return; setBusy('rating'); bridge.setBatchRating(selected, rating === '' ? undefined : Number(rating), rating === '').then((result) => { setRatingDialog(false); completeMutation(result.message) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy('')) }
  const applyTags = () => { if (!selected.length || busy) return; setBusy('tags'); bridge.updateBatchTags(selected, addTags.map((item) => item.id), removeTags.map((item) => item.id)).then((result) => { setTagDialog(false); completeMutation(result.message) }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy('')) }
  const createBatchSync = () => { if (!selected.length || busy) return; setBusy('sync'); bridge.createBatchSync(selected).then((result) => { setNotice(result.message); clearSelection() }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy('')) }
  const openOrganizer = (kind: BatchOrganizerKind) => { setOrganizerDialog(kind); setOrganizerPreview(undefined); setOrganizerTemplate('{Code}'); setOrganizerDestination('') }
  const dryRunOrganizer = () => {
    if (!selected.length || busy || !organizerDialog) return
    if (organizerDialog === 'move' && !organizerDestination.trim()) { setNotice('请选择目标目录'); return }
    setBusy('organizer-preview')
    bridge.organizerDryRun(selected, organizerTemplate, organizerDialog === 'move' ? organizerDestination : undefined).then(setOrganizerPreview).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(''))
  }
  const executeOrganizer = () => {
    if (!organizerPreview || busy) return
    setBusy('organizer-execute')
    bridge.executeOrganizer(organizerPreview.taskId, organizerPreview.confirmationToken).then((result) => {
      setNotice(result.message)
      setOrganizerDialog(undefined)
      setOrganizerPreview(undefined)
      clearSelection()
      refresh()
    }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(''))
  }

  return <>
    <MovieWall title="批量整理" description={(total) => `复用 MovieWall 当前条件筛选 ${total} 部影片。移动和重命名必须先预览，再确认进入任务中心执行。`}
      stateKey="lmm.organizer.batch" selectable selectedIds={selected} onSelect={toggleSelected} reloadSignal={reloadSignal}
      defaultLabel={<StatusBadge tone={selected.length ? 'info' : 'neutral'} label={selected.length ? `已选择 ${selected.length} 部` : '未选择影片'}/>}
      renderStats={(context) => <BatchActionBar context={context} selected={selected} busy={Boolean(busy)}
        onSelectPage={(items) => setSelected((current) => unique([...current, ...items.map((item) => item.dataId)]))}
        onClear={clearSelection} onTags={openBatchTags} onFavorite={() => setFavoriteDialog(true)} onRating={() => setRatingDialog(true)} onSync={createBatchSync}
        onMove={() => openOrganizer('move')} onRename={() => openOrganizer('rename')}/>}
      emptyTitle="暂无可整理影片" emptyDescription="当前筛选条件下没有可用于批量整理的影片。"/>
    <Dialog open={favoriteDialog} onClose={() => !busy && setFavoriteDialog(false)} fullWidth maxWidth="xs"><DialogTitle>批量收藏</DialogTitle><DialogContent><Alert severity="info">将对已选择的 {selected.length} 部影片应用收藏状态。</Alert></DialogContent><DialogActions><Button disabled={Boolean(busy)} onClick={() => setFavoriteDialog(false)}>取消</Button><Button disabled={Boolean(busy) || selected.length === 0} onClick={() => applyFavorite(false)}>取消收藏</Button><Button variant="contained" disabled={Boolean(busy) || selected.length === 0} onClick={() => applyFavorite(true)}>收藏</Button></DialogActions></Dialog>
    <Dialog open={ratingDialog} onClose={() => !busy && setRatingDialog(false)} fullWidth maxWidth="xs"><DialogTitle>批量评分</DialogTitle><DialogContent><TextField select fullWidth margin="normal" label="评分" value={rating} onChange={(event) => setRating(event.target.value)}><MenuItem value="">清除评分</MenuItem>{[0, 1, 2, 3, 4, 5].map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField></DialogContent><DialogActions><Button disabled={Boolean(busy)} onClick={() => setRatingDialog(false)}>取消</Button><Button variant="contained" disabled={Boolean(busy) || selected.length === 0} onClick={applyRating}>应用到 {selected.length} 部影片</Button></DialogActions></Dialog>
    <Dialog open={tagDialog} onClose={() => !busy && setTagDialog(false)} fullWidth maxWidth="sm"><DialogTitle>批量标签</DialogTitle><DialogContent><Autocomplete multiple options={tags.filter((tag) => !removeTags.some((item) => item.id === tag.id))} value={addTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setAddTags(value)} renderInput={(params) => <TextField {...params} label="添加标签" margin="normal"/>}/><Autocomplete multiple options={tags.filter((tag) => !addTags.some((item) => item.id === tag.id))} value={removeTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setRemoveTags(value)} renderInput={(params) => <TextField {...params} label="移除标签" margin="normal" helperText="只解除关系，不删除标签。"/>}/></DialogContent><DialogActions><Button disabled={Boolean(busy)} onClick={() => setTagDialog(false)}>取消</Button><Button variant="contained" disabled={Boolean(busy) || selected.length === 0 || (!addTags.length && !removeTags.length)} onClick={applyTags}>应用到 {selected.length} 部影片</Button></DialogActions></Dialog>
    <OrganizerPreviewDialog kind={organizerDialog} selectedCount={selected.length} template={organizerTemplate} destination={organizerDestination} preview={organizerPreview} busy={Boolean(busy)}
      onTemplate={setOrganizerTemplate} onDestination={setOrganizerDestination} onPreview={dryRunOrganizer} onExecute={executeOrganizer} onClose={() => { setOrganizerDialog(undefined); setOrganizerPreview(undefined) }}/>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </>
}

function OrganizerPreviewDialog({ kind, selectedCount, template, destination, preview, busy, onTemplate, onDestination, onPreview, onExecute, onClose }: {
  kind?: BatchOrganizerKind
  selectedCount: number
  template: string
  destination: string
  preview?: OrganizerPreview
  busy: boolean
  onTemplate: (value: string) => void
  onDestination: (value: string) => void
  onPreview: () => void
  onExecute: () => void
  onClose: () => void
}) {
  return <Dialog open={Boolean(kind)} onClose={busy ? undefined : onClose} maxWidth="md" fullWidth>
    <DialogTitle>{kind === 'move' ? '批量移动预览' : '批量重命名预览'}</DialogTitle>
    <DialogContent dividers><Stack spacing={1.5}>
      <Alert severity="info">已选择 {selectedCount} 部影片。执行前会检查源文件、目标冲突和文件指纹；目标存在时不会覆盖。</Alert>
      <TextField label="命名模板" value={template} onChange={(event) => { onTemplate(event.target.value); }} helperText="支持 {Code}、{Title}、{Year}、{Actors}，不会修改扩展名。"/>
      {kind === 'move' && <TextField label="目标目录" value={destination} onChange={(event) => onDestination(event.target.value)} helperText="请输入已授权的目标文件夹；不存在的末级目录会在执行时创建。"/>}
      {preview && <Stack spacing={1}>
        <Alert severity={preview.conflictItems ? 'error' : 'success'}>预览完成：{preview.validItems} 项可执行，{preview.conflictItems} 项冲突。尚未修改文件。</Alert>
        {preview.warnings.map((warning) => <Alert key={warning} severity="info">{warning}</Alert>)}
        {preview.items.map((item) => <Paper key={item.mediaFileId} variant="outlined" sx={{ p: 1.25, borderRadius: 2 }}>
          <Typography variant="caption" color="text.secondary">原路径</Typography>
          <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{item.sourcePath}</Typography>
          <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>目标路径</Typography>
          <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{item.destinationPath}</Typography>
          {item.conflict && <Alert severity="error" sx={{ mt: 1 }}>{item.conflict}</Alert>}
        </Paper>)}
      </Stack>}
    </Stack></DialogContent>
    <DialogActions><Button disabled={busy} onClick={onClose}>取消</Button><Button variant="outlined" disabled={busy || selectedCount === 0} onClick={onPreview}>生成预览</Button><Button variant="contained" disabled={busy || !preview || preview.conflictItems > 0} onClick={onExecute}>确认并进入任务</Button></DialogActions>
  </Dialog>
}

function BatchActionBar({ context, selected, busy, onSelectPage, onClear, onTags, onFavorite, onRating, onSync, onMove, onRename }: {
  context: MovieWallRenderContext
  selected: number[]
  busy: boolean
  onSelectPage: (items: MediaItem[]) => void
  onClear: () => void
  onTags: () => void
  onFavorite: () => void
  onRating: () => void
  onSync: () => void
  onMove: () => void
  onRename: () => void
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
      <Button size="small" variant="outlined" startIcon={<DriveFileMoveRoundedIcon/>} onClick={onMove} disabled={disabled}>批量移动</Button>
      <Button size="small" variant="outlined" onClick={onRename} disabled={disabled}>批量重命名</Button>
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
function groupKey(group: DuplicateGroup) { return `${group.rule}:${group.key}` }
function filterDuplicateGroups(groups: DuplicateGroup[], search: string) {
  const query = search.trim().toLowerCase()
  if (!query) return groups
  return groups.map((group) => ({
    ...group,
    items: group.items.filter((item) => `${group.key} ${item.code} ${item.title} ${item.fileName} ${item.filePath}`.toLowerCase().includes(query))
  })).filter((group) => group.key.toLowerCase().includes(query) || group.items.length > 0)
}
function unique(values: number[]) { return [...new Set(values)] }
function uniqueStrings(values: string[]) { return [...new Set(values)] }
function formatFileSize(value: number) {
  if (!value) return '未知'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let size = value
  let index = 0
  while (size >= 1024 && index < units.length - 1) { size /= 1024; index += 1 }
  return `${size.toFixed(index === 0 ? 0 : 1)} ${units[index]}`
}
function resolutionText(width: number, height: number) { return width > 0 && height > 0 ? `${width}x${height}` : '未知' }
function ratingText(item: DuplicateMovie) { return item.userRatingSet ? `${item.userRating.toFixed(1)} / 5` : '未评分' }
function fileNameFromPath(path: string) { return path.split(/[\\/]/).filter(Boolean).pop() ?? '' }
function Summary({ label, value }: { label: string; value: string }) {
  return <Box sx={{ border: 1, borderColor: 'divider', borderRadius: 1.5, p: 1.25 }}>
    <Typography variant="caption" color="text.secondary">{label}</Typography>
    <Typography sx={{ fontWeight: 850 }}>{value}</Typography>
  </Box>
}
function readOrganizerMode(): OrganizerMode {
  return window.sessionStorage.getItem('lmm.organizer.mode') === 'batch' ? 'batch' : 'duplicates'
}
