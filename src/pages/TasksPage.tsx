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
import { Alert, Box, Button, Card, Checkbox, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, LinearProgress, MenuItem, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { TaskStatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import type { TaskItem, TaskLogItem } from '@/types/media'
import { bridge } from '@/services/bridge'

const taskNames: Record<string, string> = { Download: '下载任务', Poster: '封面任务', Preview: '预览图任务', Screenshot: '截图任务', GIF: 'GIF 任务', Scan: '扫描任务', Sync: '同步任务', Crop: '裁切任务', AI: 'AI 任务', ActorRepair: '演员修复', ImageCacheRebuild: '图片缓存重建', Organizer: '文件整理' }
const statusNames: Record<string, string> = { Pending: '等待中', Preparing: '准备中', FetchingMetadata: '获取元数据', DownloadingImages: '下载图片', WritingMetadata: '写入元数据', WritingNfo: '写入 NFO', Retrying: '等待重试', Running: '进行中', Paused: '已暂停', Completed: '已完成', Failed: '失败', Cancelled: '已取消' }
const activeStates = ['Pending', 'Preparing', 'FetchingMetadata', 'DownloadingImages', 'WritingMetadata', 'WritingNfo', 'Retrying', 'Running', 'Paused']

export default function TasksPage() {
  const [tasks, setTasks] = useState<TaskItem[]>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [filter, setFilter] = useState('all')
  const [busy, setBusy] = useState<number>()
  const [cancelTarget, setCancelTarget] = useState<TaskItem>()
  const [batchCancelOpen, setBatchCancelOpen] = useState(false)
  const [cleanupTarget, setCleanupTarget] = useState<'completed' | 'failed' | 'cancelled' | 'terminal'>()
  const [deleteTarget, setDeleteTarget] = useState<TaskItem>()
  const [selectedSyncTasks, setSelectedSyncTasks] = useState<number[]>([])
  const [logs, setLogs] = useState<{ task: TaskItem; items?: TaskLogItem[] }>()

  const load = useCallback((showLoading = false) => {
    if (showLoading) setTasks(undefined)
    setError('')
    return bridge.tasks().then(setTasks).catch((reason: Error) => setError(reason.message))
  }, [])
  useEffect(() => { void load(true) }, [load])

  const visible = useMemo(() => tasks?.filter(item => filter === 'all' || item.status === filter) ?? [], [tasks, filter])
  const selectedSyncTaskItems = useMemo(() => tasks?.filter(item => selectedSyncTasks.includes(item.id)) ?? [], [tasks, selectedSyncTasks])
  const active = tasks?.filter(item => activeStates.includes(item.status)).length ?? 0
  const completed = tasks?.filter(item => item.status === 'Completed').length ?? 0
  const failed = tasks?.filter(item => item.status === 'Failed').length ?? 0

  const mutate = async (task: TaskItem, action: 'pause' | 'resume' | 'retry') => {
    setBusy(task.id); setError(''); setNotice('')
    try { const result = action === 'pause' ? await bridge.pauseTask(task.id) : action === 'resume' ? await bridge.resumeTask(task.id) : await bridge.retryTask(task.id); setNotice(result.message); await load() }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const cancel = async () => {
    if (!cancelTarget) return
    setBusy(cancelTarget.id); setError('')
    try { const result = await bridge.cancelTask(cancelTarget.id); setNotice(result.message); setCancelTarget(undefined); await load() }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const cancelBatch = async () => {
    setBusy(-1); setError('')
    try { const result = await bridge.cancelSyncTasks(selectedSyncTasks); setNotice(result.message); setSelectedSyncTasks([]); setBatchCancelOpen(false); await load() }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const cleanup = async () => {
    if (!cleanupTarget) return
    setBusy(-2); setError('')
    try { const result = await bridge.cleanupTasks(cleanupTarget); setNotice(result.message); setCleanupTarget(undefined); await load() }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const deleteTask = async () => {
    if (!deleteTarget) return
    setBusy(deleteTarget.id); setError('')
    try { const result = await bridge.deleteTask(deleteTarget.id); setNotice(result.message); setDeleteTarget(undefined); await load() }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }
  const openLogs = async (task: TaskItem) => {
    setLogs({ task }); setError('')
    try { setLogs({ task, items: await bridge.taskLogs(task.id) }) }
    catch (reason) { setError((reason as Error).message); setLogs(undefined) }
  }

  const stats = tasks && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1.5 }}>
    <StatCard label="任务总数" value={tasks.length} icon={<HourglassTopRoundedIcon/>}/>
    <StatCard label="活动任务" value={active} icon={<HourglassTopRoundedIcon/>} tone="primary.main"/>
    <StatCard label="已完成" value={completed} icon={<CheckCircleRoundedIcon/>} tone="success.main"/>
    <StatCard label="失败" value={failed} icon={<ErrorRoundedIcon/>} tone="error.main"/>
  </Box>
  const filters = <Stack direction={{ xs: 'column', md: 'row' }} spacing={1} sx={{ alignItems: { xs: 'stretch', md: 'center' }, justifyContent: 'space-between' }}>
    {selectedSyncTasks.length > 0 ? <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}><Typography sx={{ fontWeight: 750 }}>已选择 {selectedSyncTasks.length} 个同步任务</Typography><Button size="small" color="error" variant="outlined" startIcon={<CancelRoundedIcon/>} onClick={() => setBatchCancelOpen(true)}>批量取消</Button><Button size="small" color="inherit" onClick={() => setSelectedSyncTasks([])}>取消选择</Button></Stack> : <Typography variant="body2" color="text.secondary">筛选和批量操作默认可见</Typography>}
    <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
      <Button size="small" variant="outlined" startIcon={<DeleteSweepRoundedIcon/>} onClick={() => setCleanupTarget('completed')}>清理已完成</Button>
      <Button size="small" variant="outlined" color="warning" startIcon={<DeleteSweepRoundedIcon/>} onClick={() => setCleanupTarget('failed')}>清理失败</Button>
      <Button size="small" variant="outlined" color="warning" startIcon={<DeleteSweepRoundedIcon/>} onClick={() => setCleanupTarget('cancelled')}>清理取消</Button>
      <Button size="small" variant="outlined" color="error" startIcon={<DeleteSweepRoundedIcon/>} onClick={() => setCleanupTarget('terminal')}>清理终态</Button>
      <TextField select size="small" label="状态" value={filter} onChange={event => setFilter(event.target.value)} sx={{ width: 160 }}><MenuItem value="all">全部</MenuItem>{Object.entries(statusNames).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}</TextField>
    </Stack>
  </Stack>

  return <WorkspacePage title="任务中心" description="统一查看扫描、导入、同步、图片及其他长任务。" stats={stats} filters={filters} activeFilterCount={filter === 'all' ? 0 : 1} onClearFilters={() => setFilter('all')} loading={!tasks && !error} error={error} primaryActions={[refreshAction(() => void load(true))]}>
    {notice && <Alert severity="success" onClose={() => setNotice('')} sx={{ mb: 2 }}>{notice}</Alert>}
    {tasks && (visible.length === 0 ? <EmptyState title="暂无任务" description={filter === 'all' ? '扫描、同步等长任务会显示在这里。' : '当前筛选状态下没有任务。'}/> : <Stack spacing={1.25}>{visible.map(task => <TaskCard key={task.id} task={task} busy={busy} selected={selectedSyncTasks.includes(task.id)} onSelect={(checked) => setSelectedSyncTasks(current => checked ? [...current, task.id] : current.filter(id => id !== task.id))} onMutate={mutate} onCancel={() => setCancelTarget(task)} onDelete={() => setDeleteTarget(task)} onLogs={() => void openLogs(task)}/>)}</Stack>)}
    <Dialog open={Boolean(cancelTarget)} onClose={busy ? undefined : () => setCancelTarget(undefined)} fullWidth maxWidth="sm"><DialogTitle>取消任务</DialogTitle><DialogContent dividers><Alert severity="warning">将取消“{cancelTarget?.name}”。已经完成并提交的单项不会回滚，未处理项目将停止。</Alert></DialogContent><DialogActions><Button onClick={() => setCancelTarget(undefined)} disabled={busy !== undefined}>返回</Button><Button color="error" variant="contained" onClick={() => void cancel()} disabled={busy !== undefined}>取消任务</Button></DialogActions></Dialog>
    <Dialog open={batchCancelOpen} onClose={busy ? undefined : () => setBatchCancelOpen(false)} fullWidth maxWidth="sm"><DialogTitle>批量取消同步任务</DialogTitle><DialogContent dividers><Alert severity="warning">将取消 {selectedSyncTaskItems.length} 个同步任务。已经完成并提交的单项不会回滚，未处理项目将停止。</Alert></DialogContent><DialogActions><Button onClick={() => setBatchCancelOpen(false)} disabled={busy !== undefined}>返回</Button><Button color="error" variant="contained" onClick={() => void cancelBatch()} disabled={busy !== undefined || selectedSyncTasks.length === 0}>批量取消</Button></DialogActions></Dialog>
    <Dialog open={Boolean(cleanupTarget)} onClose={busy ? undefined : () => setCleanupTarget(undefined)} fullWidth maxWidth="sm"><DialogTitle>清理任务记录</DialogTitle><DialogContent dividers><Alert severity="warning">只会删除任务中心中的终态任务记录和对应日志，不会删除影片、图片、NFO 或媒体文件。</Alert></DialogContent><DialogActions><Button onClick={() => setCleanupTarget(undefined)} disabled={busy !== undefined}>返回</Button><Button color="error" variant="contained" onClick={() => void cleanup()} disabled={busy !== undefined}>确认清理</Button></DialogActions></Dialog>
    <Dialog open={Boolean(deleteTarget)} onClose={busy ? undefined : () => setDeleteTarget(undefined)} fullWidth maxWidth="sm"><DialogTitle>删除任务记录</DialogTitle><DialogContent dividers><Alert severity="warning">将删除“{deleteTarget?.name}”及其日志。活动任务不会被删除，媒体文件和数据库业务数据不受影响。</Alert></DialogContent><DialogActions><Button onClick={() => setDeleteTarget(undefined)} disabled={busy !== undefined}>返回</Button><Button color="error" variant="contained" onClick={() => void deleteTask()} disabled={busy !== undefined}>删除记录</Button></DialogActions></Dialog>
    <Dialog open={Boolean(logs)} onClose={() => setLogs(undefined)} fullWidth maxWidth="md"><DialogTitle>任务日志 · {logs?.task.name}</DialogTitle><DialogContent dividers>{!logs?.items ? <Box sx={{ minHeight: 180, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : logs.items.length === 0 ? <EmptyState title="暂无日志" description="该任务没有持久日志记录。"/> : <Stack spacing={1}>{logs.items.map(log => <Box key={log.id} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '160px 80px minmax(0,1fr)' }, gap: 1.5, py: 1, borderBottom: 1, borderColor: 'divider' }}><Typography variant="caption" color="text.secondary">{log.createdAt.slice(0, 19).replace('T', ' ')}</Typography><TaskStatusBadge status={log.level}/><Typography variant="body2">{log.message}</Typography></Box>)}</Stack>}</DialogContent><DialogActions><Button onClick={() => setLogs(undefined)}>关闭</Button></DialogActions></Dialog>
  </WorkspacePage>
}

function TaskCard({ task, busy, selected, onSelect, onMutate, onCancel, onDelete, onLogs }: { task: TaskItem; busy?: number; selected: boolean; onSelect: (checked: boolean) => void; onMutate: (task: TaskItem, action: 'pause' | 'resume' | 'retry') => void; onCancel: () => void; onDelete: () => void; onLogs: () => void }) {
  const complete = task.status === 'Completed'
  const failedTask = task.status === 'Failed'
  const Icon = complete ? CheckCircleRoundedIcon : failedTask ? ErrorRoundedIcon : HourglassTopRoundedIcon
  const canPause = ['Scan', 'Sync'].includes(task.type) && activeStates.includes(task.status) && task.status !== 'Paused'
  const canResume = ['Scan', 'Sync'].includes(task.type) && task.status === 'Paused'
  const canCancel = activeStates.includes(task.status)
  const canBatchCancel = task.type === 'Sync' && canCancel
  const canRetry = ['Scan', 'Sync'].includes(task.type) && ['Failed', 'Cancelled'].includes(task.status)
  const canDelete = ['Completed', 'Failed', 'Cancelled'].includes(task.status)
  return <Card sx={{ p: 2 }}><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '190px 115px minmax(130px,1fr) minmax(170px,1.2fr) 155px auto' }, gap: 2, alignItems: 'center' }}>
    <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>{canBatchCancel && <Checkbox size="small" checked={selected} onChange={(_, checked) => onSelect(checked)} slotProps={{ input: { 'aria-label': `选择同步任务 ${task.name}` } }}/>}<Icon color={complete ? 'success' : failedTask ? 'error' : 'primary'}/><Typography sx={{ fontWeight: 750 }}>{taskNames[task.type] || task.type}</Typography></Box>
    <TaskStatusBadge status={task.status}/>
    <Box sx={{ minWidth: 0 }}><Typography noWrap title={task.name}>{task.name}</Typography>{task.provider && <Typography variant="caption" color="text.secondary">{task.provider}{task.retryCount ? ` · 重试 ${task.retryCount}` : ''}</Typography>}</Box>
    <Box><LinearProgress variant="determinate" value={Math.max(0, Math.min(100, task.progress))}/><Typography variant="caption" color="text.secondary">{task.completedItems}/{task.totalItems} · {task.progress.toFixed(0)}%</Typography></Box>
    <Typography variant="body2" color="text.secondary">{task.createdAt.slice(0, 19).replace('T', ' ')}</Typography>
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
