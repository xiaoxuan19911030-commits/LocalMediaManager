import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import ErrorRoundedIcon from '@mui/icons-material/ErrorRounded'
import HourglassTopRoundedIcon from '@mui/icons-material/HourglassTopRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import { Alert, Box, Button, Card, Chip, CircularProgress, LinearProgress, Stack, Typography } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { EmptyState } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { TaskItem } from '@/types/media'

const taskNames:Record<string,string>={Download:'下载任务',Screenshot:'截图任务',Scan:'扫描任务',Sync:'同步任务',Crop:'裁切任务'}
export default function TasksPage(){
  const[tasks,setTasks]=useState<TaskItem[]>();const[error,setError]=useState('')
  const load=useCallback(()=>{setError('');bridge.tasks().then(setTasks).catch((reason:Error)=>setError(reason.message))},[]);useEffect(load,[load])
  return <Box><PageHeader title="任务中心" description="统一查看扫描、下载、同步、截图和裁切任务。" action={<Button variant="outlined" startIcon={<RefreshRoundedIcon/>} onClick={load}>刷新</Button>}/>
    {error&&<Alert severity="error">{error}</Alert>}{!tasks&&!error?<Box sx={{minHeight:320,display:'grid',placeItems:'center'}}><CircularProgress/></Box>:tasks?.length===0?<EmptyState title="暂无任务" description="后续扫描、同步、下载和裁切任务会统一显示在这里。"/>:<Stack spacing={1.25}>{tasks?.map(task=>{
      const complete=task.status==='Completed',failed=task.status==='Failed';const Icon=complete?CheckCircleRoundedIcon:failed?ErrorRoundedIcon:HourglassTopRoundedIcon
      return <Card key={task.id} sx={{p:2}}><Box sx={{display:'grid',gridTemplateColumns:{xs:'1fr',md:'180px 120px minmax(180px,1fr) 160px'},gap:2,alignItems:'center'}}>
        <Box sx={{display:'flex',gap:1,alignItems:'center'}}><Icon color={complete?'success':failed?'error':'primary'}/><Typography sx={{fontWeight:750}}>{taskNames[task.type]||task.type}</Typography></Box><Chip size="small" color={complete?'success':failed?'error':'primary'} variant="outlined" label={task.status}/>
        <Box><LinearProgress variant="determinate" value={Math.max(0,Math.min(100,task.progress))}/><Typography variant="caption" color="text.secondary">{task.completedItems}/{task.totalItems} · {task.progress.toFixed(0)}%</Typography></Box><Typography variant="body2" color="text.secondary">{task.createdAt.slice(0,19).replace('T',' ')}</Typography>
      </Box>{task.errorMessage&&<Alert severity="error" sx={{mt:1.5}}>{task.errorMessage}</Alert>}</Card>})}</Stack>}
  </Box>
}
