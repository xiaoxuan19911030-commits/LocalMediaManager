import CancelRoundedIcon from '@mui/icons-material/CancelRounded'
import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import ErrorRoundedIcon from '@mui/icons-material/ErrorRounded'
import HourglassTopRoundedIcon from '@mui/icons-material/HourglassTopRounded'
import ListAltRoundedIcon from '@mui/icons-material/ListAltRounded'
import PauseRoundedIcon from '@mui/icons-material/PauseRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import ReplayRoundedIcon from '@mui/icons-material/ReplayRounded'
import { Alert, Box, Button, Card, Chip, CircularProgress, Dialog, DialogActions, DialogContent, DialogTitle, IconButton, LinearProgress, MenuItem, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { TaskItem, TaskLogItem } from '@/types/media'

const taskNames: Record<string, string> = { Download: '下载任务', Screenshot: '截图任务', Scan: '扫描任务', Sync: '同步任务', Crop: '裁切任务', AI: 'AI 任务', ActorRepair: '演员修复' }
const statusNames: Record<string, string> = { Pending: '等待中', Running: '进行中', Paused: '已暂停', Completed: '已完成', Failed: '失败', Cancelled: '已取消' }

export default function TasksPage() {
  const [tasks, setTasks] = useState<TaskItem[]>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [filter, setFilter] = useState('all')
  const [busy, setBusy] = useState<number>()
  const [cancelTarget, setCancelTarget] = useState<TaskItem>()
  const [logs, setLogs] = useState<{ task: TaskItem; items?: TaskLogItem[] }>()

  const load = useCallback((showLoading = false) => {
    if (showLoading) setTasks(undefined)
    setError('')
    return bridge.tasks().then(setTasks).catch((reason: Error) => setError(reason.message))
  }, [])
  useEffect(() => { void load(true) }, [load])

  const visible = useMemo(() => tasks?.filter(item => filter === 'all' || item.status === filter) ?? [], [tasks, filter])
  const active = tasks?.filter(item => ['Pending', 'Running', 'Paused'].includes(item.status)).length ?? 0
  const completed = tasks?.filter(item => item.status === 'Completed').length ?? 0
  const failed = tasks?.filter(item => item.status === 'Failed').length ?? 0

  const mutate = async (task: TaskItem, action: 'pause' | 'resume' | 'retry') => {
    setBusy(task.id); setError(''); setNotice('')
    try {
      const result = action === 'pause' ? await bridge.pauseTask(task.id) : action === 'resume' ? await bridge.resumeTask(task.id) : await bridge.retryTask(task.id)
      setNotice(result.message); await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }

  const cancel = async () => {
    if (!cancelTarget) return
    setBusy(cancelTarget.id); setError('')
    try {
      const result = await bridge.cancelTask(cancelTarget.id)
      setNotice(result.message); setCancelTarget(undefined); await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(undefined) }
  }

  const openLogs = async (task: TaskItem) => {
    setLogs({ task }); setError('')
    try { setLogs({ task, items: await bridge.taskLogs(task.id) }) }
    catch (reason) { setError((reason as Error).message); setLogs(undefined) }
  }

  return <Box>
    <PageHeader title="任务中心" description="统一查看扫描、导入、同步、图片及其他长任务。" action={<Button variant="outlined" startIcon={<RefreshRoundedIcon/>} onClick={() => void load(true)}>刷新</Button>}/>
    {error && <Alert severity="error" sx={{ mb: 2 }}>任务操作失败：{error}</Alert>}
    {notice && <Alert severity="success" onClose={() => setNotice('')} sx={{ mb: 2 }}>{notice}</Alert>}
    {!tasks && !error ? <Box sx={{ minHeight: 320, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : tasks && <>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1.5, mb: 2.5 }}>
        <StatCard label="任务总数" value={tasks.length} icon={<HourglassTopRoundedIcon/>}/><StatCard label="活动任务" value={active} icon={<HourglassTopRoundedIcon/>} tone="primary.main"/><StatCard label="已完成" value={completed} icon={<CheckCircleRoundedIcon/>} tone="success.main"/><StatCard label="失败" value={failed} icon={<ErrorRoundedIcon/>} tone="error.main"/>
      </Box>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1.5 }}><TextField select size="small" label="状态" value={filter} onChange={event => setFilter(event.target.value)} sx={{ width: 150 }}><MenuItem value="all">全部</MenuItem>{Object.entries(statusNames).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}</TextField></Box>
      {visible.length === 0 ? <EmptyState title="暂无任务" description={filter === 'all' ? '扫描、同步等长任务会显示在这里。' : '当前筛选状态下没有任务。'}/> : <Stack spacing={1.25}>{visible.map(task => {
        const complete = task.status === 'Completed'; const failedTask = task.status === 'Failed'; const Icon = complete ? CheckCircleRoundedIcon : failedTask ? ErrorRoundedIcon : HourglassTopRoundedIcon
        const canPause = task.type === 'Scan' && ['Pending', 'Running'].includes(task.status)
        const canResume = task.type === 'Scan' && task.status === 'Paused'
        const canCancel = ['Pending', 'Running', 'Paused'].includes(task.status)
        const canRetry = task.type === 'Scan' && ['Failed', 'Cancelled'].includes(task.status)
        return <Card key={task.id} sx={{ p: 2 }}><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '160px 105px minmax(130px,1fr) minmax(170px,1.2fr) 155px auto' }, gap: 2, alignItems: 'center' }}>
          <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}><Icon color={complete ? 'success' : failedTask ? 'error' : 'primary'}/><Typography sx={{ fontWeight: 750 }}>{taskNames[task.type] || task.type}</Typography></Box>
          <Chip size="small" color={complete ? 'success' : failedTask ? 'error' : task.status === 'Cancelled' ? 'default' : 'primary'} variant="outlined" label={statusNames[task.status] || task.status}/>
          <Typography noWrap title={task.name}>{task.name}</Typography>
          <Box><LinearProgress variant="determinate" value={Math.max(0, Math.min(100, task.progress))}/><Typography variant="caption" color="text.secondary">{task.completedItems}/{task.totalItems} · {task.progress.toFixed(0)}%</Typography></Box>
          <Typography variant="body2" color="text.secondary">{task.createdAt.slice(0, 19).replace('T', ' ')}</Typography>
          <Stack direction="row" spacing={0.25}>
            {canPause && <Tooltip title="暂停"><span><IconButton size="small" aria-label="暂停任务" disabled={busy === task.id} onClick={() => void mutate(task, 'pause')}><PauseRoundedIcon/></IconButton></span></Tooltip>}
            {canResume && <Tooltip title="继续"><span><IconButton size="small" aria-label="继续任务" disabled={busy === task.id} onClick={() => void mutate(task, 'resume')}><PlayArrowRoundedIcon/></IconButton></span></Tooltip>}
            {canRetry && <Tooltip title="重试"><span><IconButton size="small" aria-label="重试任务" disabled={busy === task.id} onClick={() => void mutate(task, 'retry')}><ReplayRoundedIcon/></IconButton></span></Tooltip>}
            {canCancel && <Tooltip title="取消"><span><IconButton size="small" aria-label="取消任务" color="error" disabled={busy === task.id} onClick={() => setCancelTarget(task)}><CancelRoundedIcon/></IconButton></span></Tooltip>}
            <Tooltip title="查看日志"><IconButton size="small" aria-label="查看任务日志" onClick={() => void openLogs(task)}><ListAltRoundedIcon/></IconButton></Tooltip>
          </Stack>
        </Box>{task.errorMessage && <Alert severity="error" sx={{ mt: 1.5 }}>{task.errorMessage}</Alert>}</Card>
      })}</Stack>}
    </>}

    <Dialog open={Boolean(cancelTarget)} onClose={busy ? undefined : () => setCancelTarget(undefined)} fullWidth maxWidth="sm"><DialogTitle>取消任务</DialogTitle><DialogContent dividers><Alert severity="warning">将取消“{cancelTarget?.name}”。已经完成并提交的单项不会回滚，未处理项目将停止。</Alert></DialogContent><DialogActions><Button onClick={() => setCancelTarget(undefined)} disabled={busy !== undefined}>返回</Button><Button color="error" variant="contained" onClick={() => void cancel()} disabled={busy !== undefined}>取消任务</Button></DialogActions></Dialog>

    <Dialog open={Boolean(logs)} onClose={() => setLogs(undefined)} fullWidth maxWidth="md"><DialogTitle>任务日志 · {logs?.task.name}</DialogTitle><DialogContent dividers>{!logs?.items ? <Box sx={{ minHeight: 180, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : logs.items.length === 0 ? <EmptyState title="暂无日志" description="该任务没有持久日志记录。"/> : <Stack spacing={1}>{logs.items.map(log => <Box key={log.id} sx={{ display: 'grid', gridTemplateColumns: '160px 80px minmax(0,1fr)', gap: 1.5, py: 1, borderBottom: 1, borderColor: 'divider' }}><Typography variant="caption" color="text.secondary">{log.createdAt.slice(0, 19).replace('T', ' ')}</Typography><Chip size="small" variant="outlined" color={log.level === 'Error' ? 'error' : log.level === 'Warning' ? 'warning' : 'default'} label={log.level}/><Typography variant="body2">{log.message}</Typography></Box>)}</Stack>}</DialogContent><DialogActions><Button onClick={() => setLogs(undefined)}>关闭</Button></DialogActions></Dialog>
  </Box>
}
