import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import ErrorRoundedIcon from '@mui/icons-material/ErrorRounded'
import HourglassTopRoundedIcon from '@mui/icons-material/HourglassTopRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import { Alert, Box, Button, Card, Chip, CircularProgress, LinearProgress, MenuItem, Stack, TextField, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { TaskItem } from '@/types/media'

const taskNames: Record<string, string> = { Download: '下载任务', Screenshot: '截图任务', Scan: '扫描任务', Sync: '同步任务', Crop: '裁切任务', AI: 'AI 任务' }
const statusNames: Record<string, string> = { Pending: '等待中', Running: '进行中', Paused: '已暂停', Completed: '已完成', Failed: '失败', Cancelled: '已取消' }
export default function TasksPage() {
  const [tasks, setTasks] = useState<TaskItem[]>(); const [error, setError] = useState(''); const [filter, setFilter] = useState('all')
  const load = useCallback(() => { setTasks(undefined); setError(''); bridge.tasks().then(setTasks).catch((reason: Error) => setError(reason.message)) }, []); useEffect(load, [load])
  const visible = useMemo(() => tasks?.filter(item => filter === 'all' || item.status === filter) ?? [], [tasks, filter]); const active = tasks?.filter(item => ['Pending', 'Running', 'Paused'].includes(item.status)).length ?? 0; const completed = tasks?.filter(item => item.status === 'Completed').length ?? 0; const failed = tasks?.filter(item => item.status === 'Failed').length ?? 0
  return <Box><PageHeader title="任务中心" description="统一查看扫描、下载、同步、截图、裁切及后续 AI 长任务。" action={<Button variant="outlined" startIcon={<RefreshRoundedIcon/>} onClick={load}>刷新</Button>}/>
    {error && <Alert severity="error">任务读取失败：{error}</Alert>}{!tasks && !error ? <Box sx={{ minHeight: 320, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : tasks && <>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1.5, mb: 2.5 }}><StatCard label="任务总数" value={tasks.length} icon={<HourglassTopRoundedIcon/>}/><StatCard label="活动任务" value={active} icon={<HourglassTopRoundedIcon/>} tone="primary.main"/><StatCard label="已完成" value={completed} icon={<CheckCircleRoundedIcon/>} tone="success.main"/><StatCard label="失败" value={failed} icon={<ErrorRoundedIcon/>} tone="error.main"/></Box>
      <Box sx={{ display: 'flex', justifyContent: 'flex-end', mb: 1.5 }}><TextField select size="small" label="状态" value={filter} onChange={event => setFilter(event.target.value)} sx={{ width: 150 }}><MenuItem value="all">全部</MenuItem>{Object.entries(statusNames).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}</TextField></Box>
      {visible.length === 0 ? <EmptyState title="暂无任务" description={filter === 'all' ? '长任务会通过统一 Tasks 系统显示在这里。' : '当前筛选状态下没有任务。'}/> : <Stack spacing={1.25}>{visible.map(task => { const complete = task.status === 'Completed'; const failedTask = task.status === 'Failed'; const Icon = complete ? CheckCircleRoundedIcon : failedTask ? ErrorRoundedIcon : HourglassTopRoundedIcon; return <Card key={task.id} sx={{ p: 2 }}><Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '180px 120px minmax(180px,1fr) 170px' }, gap: 2, alignItems: 'center' }}>
        <Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}><Icon color={complete ? 'success' : failedTask ? 'error' : 'primary'}/><Typography sx={{ fontWeight: 750 }}>{taskNames[task.type] || task.type}</Typography></Box><Chip size="small" color={complete ? 'success' : failedTask ? 'error' : 'primary'} variant="outlined" label={statusNames[task.status] || task.status}/>
        <Box><LinearProgress variant="determinate" value={Math.max(0, Math.min(100, task.progress))}/><Typography variant="caption" color="text.secondary">{task.completedItems}/{task.totalItems} · {task.progress.toFixed(0)}%</Typography></Box><Typography variant="body2" color="text.secondary">{task.createdAt.slice(0, 19).replace('T', ' ')}</Typography>
      </Box>{task.errorMessage && <Alert severity="error" sx={{ mt: 1.5 }}>{task.errorMessage}</Alert>}</Card>})}</Stack>}
      <Alert severity="info" sx={{ mt: 2 }}>任务查询已接入 Bridge；暂停、重试、取消等写操作需等对应命令接口和回滚规则完成后开放。</Alert>
    </>}
  </Box>
}
