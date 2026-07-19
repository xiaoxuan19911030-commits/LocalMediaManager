import CancelRoundedIcon from '@mui/icons-material/CancelRounded'
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import DeleteSweepRoundedIcon from '@mui/icons-material/DeleteSweepRounded'
import ErrorRoundedIcon from '@mui/icons-material/ErrorRounded'
import HourglassTopRoundedIcon from '@mui/icons-material/HourglassTopRounded'
import ListAltRoundedIcon from '@mui/icons-material/ListAltRounded'
import PauseRoundedIcon from '@mui/icons-material/PauseRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import ReplayRoundedIcon from '@mui/icons-material/ReplayRounded'
import { Alert, Box, Button, Card, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, LinearProgress, MenuItem, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { TaskStatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import type { TaskItem, TaskLogItem } from '@/types/media'
import { bridge } from '@/services/bridge'

const taskNames: Record<string, string> = {
  Download: '下载任务',
  Poster: '封面任务',
  Preview: '预览图任务',
  Screenshot: '生成截图',
  GIF: '生成 GIF',
  Scan: '扫描影片',
  Sync: '同步信息',
  Crop: '裁切任务',
  AI: 'AI 任务',
  ActorRepair: '演员修复',
  ImageCacheRebuild: '图片缓存重建',
  Organizer: '批量整理',
  Rename: '重命名',
  DeleteMetadata: '删除影片',
  DeleteMedia: '删除影片',
}

const sourceNames: Record<string, string> = {
  Sync: 'MetaTube',
  Scan: '本地扫描',
  Screenshot: 'FFmpeg',
  GIF: 'FFmpeg',
  Preview: 'FFmpeg',
  Poster: 'FFmpeg',
  Crop: '本地裁切',
  Organizer: '整理工具',
  Rename: '文件操作',
  DeleteMetadata: 'Safe Delete',
  DeleteMedia: 'Safe Delete',
  ImageCacheRebuild: '图片缓存',
}

const statusOptions = [
  { value: 'all', label: '全部任务' },
  { value: 'active', label: '执行中' },
  { value: 'completed', label: '已完成' },
  { value: 'failed', label: '已失败' },
  { value: 'cancelled', label: '已取消' },
] as const

type StatusFilter = typeof statusOptions[number]['value']
type CleanupStatus = 'completed' | 'failed' | 'cancelled' | 'terminal' | 'all-tasks'
interface TaskTypeOption { value: string; label: string; types: string[] }

const activeStates = ['Pending', 'Preparing', 'FetchingMetadata', 'DownloadingImages', 'WritingMetadata', 'WritingNfo', 'Retrying', 'Running', 'Paused']
const terminalStates = ['Completed', 'CompletedWithErrors', 'Failed', 'Cancelled']

const preferredTypeOptions: TaskTypeOption[] = [
  { value: 'all', label: '全部类型', types: [] },
  { value: 'Sync', label: '同步信息', types: ['Sync'] },
  { value: 'Scan', label: '扫描影片', types: ['Scan'] },
  { value: 'Screenshot', label: '生成截图', types: ['Screenshot'] },
  { value: 'GIF', label: '生成 GIF', types: ['GIF'] },
  { value: 'Rename', label: '重命名', types: ['Rename'] },
  { value: 'Delete', label: '删除影片', types: ['DeleteMetadata', 'DeleteMedia'] },
]

export default function TasksPage() {
  const [tasks, setTasks] = useState<TaskItem[]>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [typeFilter, setTypeFilter] = useState('all')
  const [busy, setBusy] = useState<number>()
  const [cancelTarget, setCancelTarget] = useState<TaskItem>()
  const [cleanupTarget, setCleanupTarget] = useState<CleanupStatus>()
  const [bulkAction, setBulkAction] = useState<'cancel' | 'cleanup'>()
  const [deleteTarget, setDeleteTarget] = useState<TaskItem>()
  const [logs, setLogs] = useState<{ task: TaskItem; items?: TaskLogItem[] }>()

  const load = useCallback((showLoading = false) => {
    if (showLoading) setTasks(undefined)
    setError('')
    return bridge.tasks().then(setTasks).catch((reason: Error) => setError(reason.message))
  }, [])
  useEffect(() => { void load(true) }, [load])

  const typeOptions = useMemo(() => {
    const knownTypes = new Set(preferredTypeOptions.flatMap(item => item.types))
    const dynamic = Array.from(new Set((tasks ?? []).map(item => item.type)))
      .filter(type => !knownTypes.has(type))
      .sort((a, b) => labelForType(a).localeCompare(labelForType(b), 'zh-Hans-CN'))
      .map(type => ({ value: type, label: labelForType(type), types: [type] }))
    return [...preferredTypeOptions, ...dynamic]
  }, [tasks])

  const visible = useMemo(() => (tasks ?? []).filter(item =>
    matchesStatus(item, statusFilter) && matchesType(item, typeFilter, typeOptions)
  ), [tasks, statusFilter, typeFilter, typeOptions])
  const visibleActive = useMemo(() => visible.filter(item => activeStates.includes(item.status)), [visible])
  const visibleTerminal = useMemo(() => visible.filter(item => terminalStates.includes(item.status)), [visible])
  const active = tasks?.filter(item => activeStates.includes(item.status)).length ?? 0
  const completed = tasks?.filter(item => item.status === 'Completed').length ?? 0
  const failed = tasks?.filter(item => item.status === 'Failed').length ?? 0

  useEffect(() => {
    if (!typeOptions.some(item => item.value === typeFilter)) setTypeFilter('all')
  }, [typeFilter, typeOptions])

  const mutate = async (task: TaskItem, action: 'pause' | 'resume' | 'retry') => {
    setBusy(task.id); setError(''); setNotice('')
    try {
      const result = action === 'pause' ? await bridge.pauseTask(task.id) : action === 'resume' ? await bridge.resumeTask(task.id) : await bridge.retryTask(task.id)
      setNotice(result.message)
      await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const cancel = async () => {
    if (!cancelTarget) return
    setBusy(cancelTarget.id); setError('')
    try {
      const result = await bridge.cancelTask(cancelTarget.id)
      setNotice(result.message)
      setCancelTarget(undefined)
      await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const cancelVisible = async () => {
    if (visibleActive.length === 0) return
    setBusy(-1); setError(''); setNotice('')
    try {
      const result = await bridge.cancelTasks(visibleActive.map(item => item.id))
      setNotice(result.message)
      setBulkAction(undefined)
      setStatusFilter('cancelled')
      await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const cleanup = async () => {
    if (!cleanupTarget) return
    setBusy(-2); setError(''); setNotice('')
    try {
      const result = typeFilter === 'all'
        ? await bridge.cleanupTasks(cleanupTarget)
        : await cleanupVisibleTerminal()
      setNotice(result.message)
      setCleanupTarget(undefined)
      setBulkAction(undefined)
      await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const cleanupVisibleTerminal = async () => {
    let count = 0
    for (const task of visibleTerminal) {
      await bridge.deleteTask(task.id)
      count += 1
    }
    return { count, message: count === 0 ? '没有可清除的任务。' : `已清除 ${count} 个任务。` }
  }
  const deleteTask = async () => {
    if (!deleteTarget) return
    setBusy(deleteTarget.id); setError('')
    try {
      const result = await bridge.deleteTask(deleteTarget.id)
      setNotice(result.message)
      setDeleteTarget(undefined)
      await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const openLogs = async (task: TaskItem) => {
    setLogs({ task }); setError('')
    try { setLogs({ task, items: await bridge.taskLogs(task.id) }) }
    catch (reason) { setError((reason as Error).message); setLogs(undefined) }
  }

  const cleanupStatus = cleanupStatusFor(statusFilter)
  const mainButtonIsCancel = statusFilter === 'active'
  const mainButtonDisabled = busy !== undefined || (mainButtonIsCancel ? visibleActive.length === 0 : visibleTerminal.length === 0)
  const mainButton = mainButtonIsCancel
    ? <Button variant="contained" color="warning" startIcon={<CancelRoundedIcon/>} disabled={mainButtonDisabled} onClick={() => setBulkAction('cancel')} sx={{ minHeight: 40, whiteSpace: 'nowrap' }}>取消任务</Button>
    : <Button variant="contained" color="error" startIcon={<DeleteSweepRoundedIcon/>} disabled={mainButtonDisabled} onClick={() => { setCleanupTarget(cleanupStatus); setBulkAction('cleanup') }} sx={{ minHeight: 40, whiteSpace: 'nowrap' }}>清除任务</Button>

  const stats = tasks && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1.5 }}>
    <StatCard label="任务总数" value={tasks.length} icon={<HourglassTopRoundedIcon/>}/>
    <StatCard label="活动任务" value={active} icon={<HourglassTopRoundedIcon/>} tone="primary.main"/>
    <StatCard label="已完成" value={completed} icon={<CheckCircleRoundedIcon/>} tone="success.main"/>
    <StatCard label="失败" value={failed} icon={<ErrorRoundedIcon/>} tone="error.main"/>
  </Box>
  const filters = <Stack direction="row" spacing={1.25} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', width: '100%' }}>
    <TextField select size="small" label="状态" value={statusFilter} onChange={event => setStatusFilter(event.target.value as StatusFilter)} sx={{ width: { xs: '100%', sm: 180 } }}>
      {statusOptions.map(option => <MenuItem key={option.value} value={option.value}>{option.label}</MenuItem>)}
    </TextField>
    <TextField select size="small" label="类型" value={typeFilter} onChange={event => setTypeFilter(event.target.value)} sx={{ width: { xs: '100%', sm: 180 } }}>
      {typeOptions.map(option => <MenuItem key={option.value} value={option.value}>{option.label}</MenuItem>)}
    </TextField>
    <Box sx={{ flex: 1, minWidth: { xs: '100%', sm: 16 } }}/>
    {mainButton}
  </Stack>

  return <WorkspacePage title="任务中心" description="统一查看扫描、同步、截图和文件操作等长任务。" stats={stats} filters={filters} activeFilterCount={0} loading={!tasks && !error} error={error} primaryActions={[refreshAction(() => void load(true))]}>
    {notice && <Alert severity="success" onClose={() => setNotice('')} sx={{ mb: 2 }}>{notice}</Alert>}
    {tasks && (visible.length === 0 ? <EmptyState title="暂无任务" description="当前筛选条件下没有任务。"/> : <Stack spacing={1.25}>{visible.map(task => <TaskCard key={task.id} task={task} busy={busy} onMutate={mutate} onCancel={() => setCancelTarget(task)} onDelete={() => setDeleteTarget(task)} onLogs={() => void openLogs(task)}/>)}</Stack>)}
    <Dialog open={Boolean(cancelTarget)} onClose={busy ? undefined : () => setCancelTarget(undefined)} fullWidth maxWidth="sm"><DialogTitle>取消任务</DialogTitle><DialogContent dividers><Alert severity="warning">将取消“{cancelTarget?.name}”。已经完成并提交的单项不会回滚，未处理项目将停止。</Alert></DialogContent><DialogActions><Button onClick={() => setCancelTarget(undefined)} disabled={busy !== undefined}>返回</Button><Button color="error" variant="contained" onClick={() => void cancel()} disabled={busy !== undefined}>取消任务</Button></DialogActions></Dialog>
    <Dialog open={bulkAction === 'cancel'} onClose={busy ? undefined : () => setBulkAction(undefined)} fullWidth maxWidth="sm"><DialogTitle>取消任务</DialogTitle><DialogContent dividers><Alert severity="warning">将取消当前筛选结果中的 {visibleActive.length} 个执行中任务，包括等待中和排队中的任务。已经完成并提交的单项不会回滚。</Alert></DialogContent><DialogActions><Button onClick={() => setBulkAction(undefined)} disabled={busy !== undefined}>返回</Button><Button color="error" variant="contained" onClick={() => void cancelVisible()} disabled={busy !== undefined || visibleActive.length === 0}>确认取消</Button></DialogActions></Dialog>
    <Dialog open={bulkAction === 'cleanup'} onClose={busy ? undefined : () => { setBulkAction(undefined); setCleanupTarget(undefined) }} fullWidth maxWidth="sm"><DialogTitle>清除任务</DialogTitle><DialogContent dividers><Alert severity="warning">将清除当前筛选结果中的 {visibleTerminal.length} 个终态任务记录和对应日志。执行中的任务不会被清除，媒体文件和业务数据不受影响。</Alert></DialogContent><DialogActions><Button onClick={() => { setBulkAction(undefined); setCleanupTarget(undefined) }} disabled={busy !== undefined}>返回</Button><Button color="error" variant="contained" onClick={() => void cleanup()} disabled={busy !== undefined || visibleTerminal.length === 0}>确认清除</Button></DialogActions></Dialog>
    <Dialog open={Boolean(deleteTarget)} onClose={busy ? undefined : () => setDeleteTarget(undefined)} fullWidth maxWidth="sm"><DialogTitle>删除任务记录</DialogTitle><DialogContent dividers><Alert severity="warning">将删除“{deleteTarget?.name}”及其日志。活动任务不会被删除，媒体文件和数据库业务数据不受影响。</Alert></DialogContent><DialogActions><Button onClick={() => setDeleteTarget(undefined)} disabled={busy !== undefined}>返回</Button><Button color="error" variant="contained" onClick={() => void deleteTask()} disabled={busy !== undefined}>删除记录</Button></DialogActions></Dialog>
    <Dialog open={Boolean(logs)} onClose={() => setLogs(undefined)} fullWidth maxWidth="md"><DialogTitle>任务日志 · {logs?.task.name}</DialogTitle><DialogContent dividers>{!logs?.items ? <Box sx={{ minHeight: 180, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : logs.items.length === 0 ? <EmptyState title="暂无日志" description="该任务没有持久日志记录。"/> : <Stack spacing={1}>{logs.items.map(log => <Box key={log.id} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '160px 80px minmax(0,1fr)' }, gap: 1.5, py: 1, borderBottom: 1, borderColor: 'divider' }}><Typography variant="caption" color="text.secondary">{formatDate(log.createdAt)}</Typography><TaskStatusBadge status={log.level}/><Typography variant="body2">{log.message}</Typography></Box>)}</Stack>}</DialogContent><DialogActions><Button onClick={() => setLogs(undefined)}>关闭</Button></DialogActions></Dialog>
  </WorkspacePage>
}

function TaskCard({ task, busy, onMutate, onCancel, onDelete, onLogs }: { task: TaskItem; busy?: number; onMutate: (task: TaskItem, action: 'pause' | 'resume' | 'retry') => void; onCancel: () => void; onDelete: () => void; onLogs: () => void }) {
  const complete = task.status === 'Completed' || task.status === 'CompletedWithErrors'
  const failedTask = task.status === 'Failed'
  const Icon = complete ? CheckCircleRoundedIcon : failedTask ? ErrorRoundedIcon : HourglassTopRoundedIcon
  const canPause = ['Scan', 'Sync', 'Screenshot', 'GIF', 'Organizer', 'DeleteMetadata', 'DeleteMedia'].includes(task.type) && activeStates.includes(task.status) && task.status !== 'Paused'
  const canResume = ['Scan', 'Sync', 'Screenshot', 'GIF', 'Organizer', 'DeleteMetadata', 'DeleteMedia'].includes(task.type) && task.status === 'Paused'
  const canCancel = activeStates.includes(task.status)
  const canRetry = ['Scan', 'Sync', 'Screenshot', 'GIF', 'Organizer', 'DeleteMetadata', 'DeleteMedia'].includes(task.type) && ['Failed', 'Cancelled'].includes(task.status)
  const canDelete = terminalStates.includes(task.status)
  return <Card sx={{ p: 2 }}><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '160px 115px minmax(160px,1fr) minmax(170px,1.1fr) 155px auto' }, gap: 2, alignItems: 'center' }}>
    <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}><Icon color={complete ? 'success' : failedTask ? 'error' : 'primary'}/><Typography sx={{ fontWeight: 750 }}>{labelForType(task.type)}</Typography></Box>
    <TaskStatusBadge status={task.status}/>
    <Box sx={{ minWidth: 0 }}><Typography noWrap title={task.name}>{task.name}</Typography><Typography variant="caption" color="text.secondary">{task.provider || sourceNames[task.type] || '本地任务'}{task.retryCount ? ` · 重试 ${task.retryCount}` : ''}</Typography></Box>
    <Box><LinearProgress variant="determinate" value={Math.max(0, Math.min(100, task.progress))}/><Typography variant="caption" color="text.secondary">{task.completedItems}/{task.totalItems} · {task.progress.toFixed(0)}%</Typography></Box>
    <Typography variant="body2" color="text.secondary">{formatDate(task.createdAt)}</Typography>
    <Stack direction="row" spacing={0.25}>
      {canPause && <Tooltip title="暂停"><span><IconButton size="small" aria-label="暂停任务" disabled={busy === task.id} onClick={() => onMutate(task, 'pause')}><PauseRoundedIcon/></IconButton></span></Tooltip>}
      {canResume && <Tooltip title="继续"><span><IconButton size="small" aria-label="继续任务" disabled={busy === task.id} onClick={() => onMutate(task, 'resume')}><PlayArrowRoundedIcon/></IconButton></span></Tooltip>}
      {canRetry && <Tooltip title="重试"><span><IconButton size="small" aria-label="重试任务" disabled={busy === task.id} onClick={() => onMutate(task, 'retry')}><ReplayRoundedIcon/></IconButton></span></Tooltip>}
      {canCancel && <Tooltip title="取消"><span><IconButton size="small" aria-label="取消任务" color="error" disabled={busy === task.id} onClick={onCancel}><CancelRoundedIcon/></IconButton></span></Tooltip>}
      {canDelete && <Tooltip title="删除记录"><span><IconButton size="small" aria-label="删除任务记录" color="error" disabled={busy === task.id} onClick={onDelete}><DeleteOutlineRoundedIcon/></IconButton></span></Tooltip>}
      <Tooltip title="查看日志"><IconButton size="small" aria-label="查看任务日志" onClick={onLogs}><ListAltRoundedIcon/></IconButton></Tooltip>
    </Stack>
  </Box>{task.resultSummary && !task.errorMessage && <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>{task.resultSummary}</Typography>}{task.errorMessage && <Alert severity="error" sx={{ mt: 1.5 }}>{task.errorMessage}</Alert>}</Card>
}

function matchesStatus(task: TaskItem, filter: StatusFilter) {
  if (filter === 'all') return true
  if (filter === 'active') return activeStates.includes(task.status)
  if (filter === 'completed') return task.status === 'Completed' || task.status === 'CompletedWithErrors'
  if (filter === 'failed') return task.status === 'Failed'
  return task.status === 'Cancelled'
}

function matchesType(task: TaskItem, filter: string, options: TaskTypeOption[]) {
  if (filter === 'all') return true
  const option = options.find(item => item.value === filter)
  return option ? option.types.includes(task.type) : task.type === filter
}

function cleanupStatusFor(filter: StatusFilter): CleanupStatus {
  if (filter === 'completed') return 'completed'
  if (filter === 'failed') return 'failed'
  if (filter === 'cancelled') return 'cancelled'
  return 'all-tasks'
}

function labelForType(type: string) {
  return taskNames[type] || type
}

function formatDate(value: string) {
  return value.slice(0, 19).replace('T', ' ')
}
