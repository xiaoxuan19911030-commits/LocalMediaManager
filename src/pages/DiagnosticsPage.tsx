import CheckCircleRoundedIcon from '@mui/icons-material/CheckCircleRounded'
import ErrorRoundedIcon from '@mui/icons-material/ErrorRounded'
import InfoRoundedIcon from '@mui/icons-material/InfoRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Box, Button, Card, CardContent, Chip, CircularProgress, Stack, Typography } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { DiagnosticsResult } from '@/types/media'

export default function DiagnosticsPage() {
  const [data, setData] = useState<DiagnosticsResult>(); const [error, setError] = useState('')
  const load = useCallback(() => { setData(undefined); setError(''); bridge.diagnostics().then(setData).catch((reason: Error) => setError(reason.message)) }, [])
  useEffect(load, [load])
  return <Box><PageHeader title="诊断中心" description="集中检查数据库、文件、文本编码、重复数据与迁移兼容问题。" action={<Button variant="outlined" startIcon={<RefreshRoundedIcon/>} onClick={load}>重新检查</Button>}/>
    {error && <Alert severity="error">诊断失败：{error}</Alert>}{!data && !error ? <Box sx={{ minHeight: 360, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : data && <>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(190px,1fr))', gap: 1.5, mb: 2.5 }}>
        <StatCard label="SQLite 完整性" value={data.integrity === 'ok' ? '正常' : data.integrity} icon={data.integrity === 'ok' ? <CheckCircleRoundedIcon/> : <ErrorRoundedIcon/>} tone={data.integrity === 'ok' ? 'success.main' : 'error.main'}/>
        <StatCard label="外键错误" value={data.foreignKeyErrors} icon={<WarningAmberRoundedIcon/>} tone={data.foreignKeyErrors ? 'error.main' : 'success.main'}/>
        <StatCard label="问题类别" value={data.items.length} icon={<InfoRoundedIcon/>} tone="warning.main"/>
      </Box>
      {data.items.length ? <Stack spacing={1.25}>{data.items.map(item => <Card key={item.code}><CardContent sx={{ display: 'flex', alignItems: 'center', gap: 1.5, '&:last-child': { pb: 2 } }}>
        {item.severity === 'error' ? <ErrorRoundedIcon color="error"/> : item.severity === 'warning' ? <WarningAmberRoundedIcon color="warning"/> : <InfoRoundedIcon color="info"/>}
        <Box sx={{ flex: 1, minWidth: 0 }}><Typography sx={{ fontWeight: 800 }}>{item.title}</Typography><Typography variant="body2" color="text.secondary">{item.detail}</Typography></Box>
        <Chip label={item.count.toLocaleString()} color={item.severity === 'error' ? 'error' : item.severity === 'warning' ? 'warning' : 'info'} variant="outlined"/>
      </CardContent></Card>)}</Stack> : <EmptyState title="未发现问题" description="数据库和媒体资源检查均已通过。"/>}
      <Alert severity="info" sx={{ mt: 2 }}>诊断中心在 0.4.0 仅执行只读检查；修复、清理和重建索引需在预览、备份和可回滚机制完成后开放。</Alert>
    </>}
  </Box>
}
