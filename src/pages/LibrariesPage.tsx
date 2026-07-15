import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import StorageRoundedIcon from '@mui/icons-material/StorageRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Box, Card, CardContent, Chip, CircularProgress, Stack, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { EmptyState, HealthMeter, StatCard } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { MediaLibrary } from '@/types/media'

export default function LibrariesPage() {
  const [libraries, setLibraries] = useState<MediaLibrary[]>(); const [error, setError] = useState('')
  useEffect(() => { bridge.libraries().then(setLibraries).catch((reason: Error) => setError(reason.message)) }, [])
  const movies = libraries?.reduce((sum, item) => sum + item.movieCount, 0) ?? 0; const missing = libraries?.reduce((sum, item) => sum + item.missingCount, 0) ?? 0; const folders = libraries?.reduce((sum, item) => sum + item.folders.length, 0) ?? 0
  return <Box><PageHeader title="媒体库" description="查看媒体库健康状态、来源文件夹和最近扫描信息。"/>
    {error && <Alert severity="error">媒体库读取失败：{error}</Alert>}{!libraries && !error ? <Box sx={{ minHeight: 320, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : libraries && <>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(190px,1fr))', gap: 1.5, mb: 2.5 }}><StatCard label="媒体库" value={libraries.length} icon={<StorageRoundedIcon/>}/><StatCard label="来源文件夹" value={folders} icon={<FolderRoundedIcon/>}/><StatCard label="关联影片" value={movies} icon={<StorageRoundedIcon/>} tone="success.main"/><StatCard label="文件缺失" value={missing} icon={<WarningAmberRoundedIcon/>} tone="warning.main"/></Box>
      {libraries.length ? <Stack spacing={1.5}>{libraries.map(library => { const health = Math.round((library.movieCount - library.missingCount) / Math.max(1, library.movieCount) * 100); return <Card key={library.id}><CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'start', gap: 2, mb: 2 }}><Box sx={{ display: 'flex', gap: 1.5 }}><Box sx={{ width: 46, height: 46, borderRadius: 2.25, bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}><StorageRoundedIcon color="primary"/></Box><Box><Typography variant="h6" sx={{ fontWeight: 800 }}>{library.name}</Typography><Typography variant="body2" color="text.secondary">{library.description || '本地媒体库'} · {library.movieCount} 部影片</Typography></Box></Box><Chip color={library.enabled ? 'success' : 'default'} label={library.enabled ? '已启用' : '已停用'}/></Box>
        <HealthMeter label="文件可用率" value={health} detail={`${library.missingCount} 个缺失`} tone={health > 95 ? 'success' : health > 80 ? 'warning' : 'error'}/>
        <Stack spacing={1} sx={{ mt: 2 }}>{library.folders.map(folder => <Box key={folder.id} sx={{ display: 'grid', gridTemplateColumns: 'auto minmax(0,1fr) auto', alignItems: 'center', gap: 1.5, p: 1.5, borderRadius: 2, bgcolor: 'action.hover' }}><FolderRoundedIcon color="action"/><Box sx={{ minWidth: 0 }}><Typography noWrap title={folder.path}>{folder.path}</Typography><Typography variant="caption" color="text.secondary">{folder.includeSubfolders ? '包含子目录' : '仅当前目录'} · {folder.scanMode} · {folder.lastScannedAt ? `上次扫描 ${folder.lastScannedAt.slice(0, 19).replace('T', ' ')}` : '尚未扫描'}</Typography></Box><RefreshRoundedIcon fontSize="small" color={folder.lastScannedAt ? 'success' : 'disabled'}/></Box>)}</Stack>
      </CardContent></Card>})}</Stack> : <EmptyState title="没有媒体库" description="新数据库中尚未迁移媒体库定义。"/>}
      <Alert severity="info" sx={{ mt: 2 }}>0.4.0 已迁移媒体库只读展示；新增、编辑、删除和扫描写操作尚未迁移，后续必须通过 Bridge、任务系统、确认和回滚机制实现。</Alert>
    </>}
  </Box>
}
