import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import ErrorRoundedIcon from '@mui/icons-material/ErrorRounded'
import InfoRoundedIcon from '@mui/icons-material/InfoRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Box, Card, CardContent, Stack, Typography } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { MaintenanceStatusBadge, StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { DiagnosticsResult } from '@/types/media'

export default function DiagnosticsPage() {
  const [data, setData] = useState<DiagnosticsResult>()
  const [error, setError] = useState('')
  const load = useCallback(() => { setData(undefined); setError(''); bridge.diagnostics().then(setData).catch((reason: Error) => setError(reason.message)) }, [])
  useEffect(load, [load])
  const stats = data && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(190px,1fr))', gap: 1.5 }}>
    <StatCard label="SQLite 完整性" value={data.integrity === 'ok' ? '正常' : data.integrity} icon={data.integrity === 'ok' ? <CheckCircleRoundedIcon/> : <ErrorRoundedIcon/>} tone={data.integrity === 'ok' ? 'success.main' : 'error.main'}/>
    <StatCard label="外键错误" value={data.foreignKeyErrors} icon={<WarningAmberRoundedIcon/>} tone={data.foreignKeyErrors ? 'error.main' : 'success.main'}/>
    <StatCard label="问题类别" value={data.items.length} icon={<InfoRoundedIcon/>} tone="warning.main"/>
  </Box>

  return <WorkspacePage title="诊断中心" description="集中检查数据库、文件、文本编码、重复数据与迁移兼容问题。" stats={stats} loading={!data && !error} error={error}
    primaryActions={[refreshAction(load, '重新检查')]}>
    {data && <>
      {data.items.length ? <Stack spacing={1.25}>{data.items.map(item => <Card key={item.code} variant="outlined"><CardContent sx={{ display: 'flex', alignItems: 'center', gap: 1.5, '&:last-child': { pb: 2 } }}>
        <MaintenanceStatusBadge severity={item.severity} label={item.title}/>
        <Box sx={{ flex: 1, minWidth: 0 }}><Typography sx={{ fontWeight: 800 }}>{item.code}</Typography><Typography variant="body2" color="text.secondary">{item.detail}</Typography></Box>
        <StatusBadge label={item.count.toLocaleString()} tone={item.severity === 'error' ? 'error' : item.severity === 'warning' ? 'warning' : 'info'}/>
      </CardContent></Card>)}</Stack> : <EmptyState title="未发现问题" description="数据库和媒体资源检查均已通过。"/>}
      <Alert severity="info" sx={{ mt: 2 }}>诊断中心当前执行只读检查；修复、清理和重建索引需在预览、备份和可回滚机制完成后开放。</Alert>
    </>}
  </WorkspacePage>
}
