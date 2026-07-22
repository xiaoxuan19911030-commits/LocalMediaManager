import BuildRoundedIcon from '@mui/icons-material/BuildRounded'
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import ImageRoundedIcon from '@mui/icons-material/ImageRounded'
import ReportProblemRoundedIcon from '@mui/icons-material/ReportProblemRounded'
import TaskAltRoundedIcon from '@mui/icons-material/TaskAltRounded'
import { Alert, Box, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, LinearProgress, Pagination, Stack, Typography } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState, StatCard, SurfaceSection } from '@/components/ProductComponents'
import { DuplicateStatusBadge, MaintenanceStatusBadge, StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { metadataHealthFilters, type MetadataHealthFilter } from '@/features/metadataHealth'
import { bridge } from '@/services/bridge'
import type { MaintenanceReport, MediaStorageAvailability, MetadataHealthAnalysisState, MetadataHealthSummary } from '@/types/media'

const pageSize = 50
const repairTargets: Partial<Record<MetadataHealthFilter, string[]>> = {
  'missing-director': ['Director'], 'missing-studio': ['Studio'], 'missing-series': ['Series'],
  'missing-description': ['Description'], 'missing-actors': ['Actors'], 'missing-tags': ['Tags'],
  'missing-poster': ['Poster'], 'missing-fanart': ['Fanart'], 'missing-preview': ['Preview'], 'missing-nfo': ['NFO'],
}
const openDirectory = (path?: string) => {
  if (!path) return
  const directory = path.includes('.') ? path.replace(/[\\/][^\\/]*$/, '') : path
  bridge.openDirectory(directory).catch(() => undefined)
}

export default function MaintenancePage() {
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [data, setData] = useState<MaintenanceReport>()
  const [health, setHealth] = useState<MetadataHealthSummary>()
  const [analysis, setAnalysis] = useState<MetadataHealthAnalysisState>()
  const [repair, setRepair] = useState<{ filter: MetadataHealthFilter; targets: string[]; count: number; storage: MediaStorageAvailability }>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const load = useCallback((nextPage = page) => {
    setData(undefined); setError('')
    Promise.all([bridge.maintenanceReport(pageSize, (nextPage - 1) * pageSize), bridge.metadataHealth(), bridge.metadataHealthAnalysis()])
      .then(([report, summary, state]) => {
        setData(report); setHealth(summary); setAnalysis(state)
        if (state.invalidated && !state.running) bridge.startMetadataHealthAnalysis().then(setAnalysis).catch((reason: Error) => setError(reason.message))
      }).catch((reason: Error) => setError(reason.message))
  }, [page])
  useEffect(() => { load(page) }, [page, load])
  useEffect(() => {
    if (!analysis?.running) return
    const handle = window.setInterval(() => bridge.metadataHealthAnalysis().then(state => {
      setAnalysis(state)
      if (!state.running && state.result) setHealth(state.result)
    }).catch((reason: Error) => setError(reason.message)), 300)
    return () => window.clearInterval(handle)
  }, [analysis?.running])
  const action = (run: Promise<unknown>, message: string) => run.then(() => { setNotice(message); load(page) }).catch((reason: Error) => setError(reason.message))
  const previewRepair = (filter: MetadataHealthFilter, targets: string[]) => Promise.all([
    bridge.previewHealthRepair(filter, targets), bridge.metadataHealthStorage(),
  ]).then(([preview, storage]) => setRepair({ filter, targets, count: preview.count, storage })).catch((reason: Error) => setError(reason.message))
  const stats = data && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(170px,1fr))', gap: 1.25 }}>
    <StatCard label="影片总数" value={data.stats.totalMovies} icon={<TaskAltRoundedIcon/>}/><StatCard label="健康影片" value={data.stats.healthyMovies} icon={<TaskAltRoundedIcon/>} tone="success.main"/>
    <StatCard label="异常影片" value={data.stats.problemMovies} icon={<ReportProblemRoundedIcon/>} tone="warning.main"/><StatCard label="重复影片" value={data.stats.duplicateMovies} icon={<ContentCopyRoundedIcon/>} tone="warning.main"/>
    <StatCard label="缺图片" value={data.stats.missingImages} icon={<ImageRoundedIcon/>} tone="warning.main"/><StatCard label="缺 NFO" value={data.stats.missingNfo} icon={<ReportProblemRoundedIcon/>} tone="warning.main"/>
    <StatCard label="缺 Metadata" value={data.stats.missingMetadata} icon={<BuildRoundedIcon/>} tone="warning.main"/><StatCard label="孤立文件" value={data.stats.orphanFiles} icon={<FolderRoundedIcon/>} tone="info.main"/>
  </Box>

  return <WorkspacePage title="Maintenance" description="统一维护中心：健康检查、孤立文件、目录问题、重复影片和缓存维护。" stats={stats} loading={!data && !error} error={error} primaryActions={[refreshAction(() => load(page), '重新扫描')]}>
    {notice && <Alert severity="success" onClose={() => setNotice('')} sx={{ mb: 2 }}>{notice}</Alert>}
    {data && <Stack spacing={2.25}>
      {health && <HealthSection health={health} analysis={analysis} navigate={navigate} onAnalyze={() => bridge.startMetadataHealthAnalysis().then(setAnalysis).catch((reason: Error) => setError(reason.message))} onCancel={() => bridge.cancelMetadataHealthAnalysis().then(setAnalysis)} onRepair={previewRepair}/>} 
      <SurfaceSection title="一键维护" description="调用已有安全接口，不新增删除或修复逻辑。"><Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
        <Button variant="outlined" onClick={() => action(bridge.metadataOverview(), 'Metadata 状态已刷新')}>刷新 Metadata</Button><Button variant="outlined" onClick={() => action(bridge.rebuildImageCache(), 'Thumbnail 重建任务已创建')}>重新生成 Thumbnail</Button><Button variant="outlined" onClick={() => action(bridge.imageCachePreview(), '缓存状态已刷新')}>刷新缓存</Button>
      </Stack></SurfaceSection>
      <SurfaceSection title="Library Health" description="分页展示扫描结果；文件定位只打开目录，不执行删除。">
        {data.issues.length ? <Stack spacing={1} sx={{ maxHeight: 520, overflowY: 'auto' }}>{data.issues.map((item, index) => <Card key={`${item.category}-${item.movieId}-${item.path}-${index}`} variant="outlined"><CardContent sx={{ py: 1.25, '&:last-child': { pb: 1.25 } }}><Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}><MaintenanceStatusBadge severity={item.severity} label={item.category}/><Typography sx={{ fontWeight: 800 }}>{item.title}</Typography>{item.movieId && <Button size="small" onClick={() => navigate(`/movies/${item.movieId}`)}>详情</Button>}{item.path && <Button size="small" onClick={() => openDirectory(item.path)}>定位目录</Button>}</Stack><Typography variant="body2" color="text.secondary" sx={{ mt: .5, overflowWrap: 'anywhere' }}>{item.detail}{item.path ? ` · ${item.path}` : ''}</Typography></CardContent></Card>)}</Stack> : <EmptyState title="未发现健康问题" description="当前分页没有媒体库健康检查问题。"/>}
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 1.5 }}><Pagination count={Math.max(1, Math.ceil(data.stats.problemMovies / pageSize))} page={page} onChange={(_, value) => setPage(value)}/></Box>
      </SurfaceSection>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, gap: 2.25 }}><PathSection title="孤立文件" items={data.orphanFiles}/><PathSection title="目录问题" items={data.directories}/></Box>
      <SurfaceSection title="重复影片（二阶段）" description="根据现有规则提供保留建议，不执行删除。">{data.duplicates.groups.length ? <Stack spacing={1.25}>{data.duplicates.groups.slice(0, 20).map(group => <Card key={`${group.rule}:${group.key}`} variant="outlined"><CardContent><Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}><DuplicateStatusBadge rule={group.rule}/><Typography sx={{ fontWeight: 850 }}>{group.key}</Typography></Stack>{group.items.map(item => <Stack key={item.movieId} direction="row" spacing={1} sx={{ alignItems: 'center', py: .5, borderTop: 1, borderColor: 'divider' }}><StatusBadge tone={item.recommendation === '推荐保留' ? 'success' : 'warning'} label={item.recommendation}/><Typography sx={{ flex: 1 }} noWrap>{item.code || item.title}</Typography><Button size="small" onClick={() => navigate(`/movies/${item.movieId}`)}>详情</Button></Stack>)}</CardContent></Card>)}</Stack> : <EmptyState title="未发现重复影片" description="当前扫描规则下没有重复候选。"/>}</SurfaceSection>
    </Stack>}
    <RepairDialog value={repair} onClose={() => setRepair(undefined)} onConfirm={() => { if (!repair) return; bridge.launchHealthRepair(repair.filter, repair.targets).then(result => { setNotice(result.message); setRepair(undefined) }).catch((reason: Error) => setError(reason.message)) }}/>
  </WorkspacePage>
}

function HealthSection({ health, analysis, navigate, onAnalyze, onCancel, onRepair }: { health: MetadataHealthSummary; analysis?: MetadataHealthAnalysisState; navigate: (path: string) => void; onAnalyze: () => void; onCancel: () => void; onRepair: (filter: MetadataHealthFilter, targets: string[]) => void }) {
  return <SurfaceSection title="Metadata Health" description={`完整 ${health.completeMovies} / ${health.totalMovies}（${health.completeRate.toFixed(1)}%），最后分析 ${new Date(health.analyzedAt).toLocaleString()}`}>
    {analysis?.running ? <Stack spacing={.75} sx={{ mb: 1.5 }}><Typography variant="body2">{analysis.stage} · {analysis.completedSteps}/{analysis.totalSteps} · {analysis.percent.toFixed(0)}% · {analysis.elapsedMilliseconds} ms</Typography><LinearProgress variant="determinate" value={analysis.percent}/><Button size="small" color="warning" onClick={onCancel}>取消分析</Button></Stack> : <Stack direction="row" spacing={1} sx={{ mb: 1.5, alignItems: 'center' }}><Button variant="contained" onClick={onAnalyze}>重新分析</Button>{analysis?.invalidated && <Alert severity="info">影片数据已变化，统计将在后台刷新。</Alert>}</Stack>}
    <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}><Button variant="outlined" onClick={() => navigate('/media?health=incomplete')}>所有不完整影片 ({health.incompleteMovies})</Button><Button variant="outlined" onClick={() => navigate('/media?health=complete')}>完整影片 ({health.completeMovies})</Button>{health.fields.map(field => { const key = `missing-${field.key.replace(/[A-Z]/g, value => `-${value.toLowerCase()}`)}` as MetadataHealthFilter; const label = metadataHealthFilters[key]; const targets = repairTargets[key]; return label ? <Stack key={field.key} direction="row" spacing={.5}><Button variant="outlined" onClick={() => navigate(`/media?health=${key}`)}>{label} ({field.missing})</Button>{targets && <Button variant="contained" disabled={field.missing === 0} onClick={() => onRepair(key, targets)}>修复</Button>}</Stack> : null })}<Button variant="outlined" onClick={() => navigate('/media?health=missing-media')}>原始媒体文件不存在</Button></Stack>
  </SurfaceSection>
}

function RepairDialog({ value, onClose, onConfirm }: { value?: { filter: MetadataHealthFilter; targets: string[]; count: number; storage: MediaStorageAvailability }; onClose: () => void; onConfirm: () => void }) {
  const writesFiles = value?.targets.some(item => ['Poster', 'Fanart', 'Preview', 'NFO'].includes(item)) ?? false
  return <Dialog open={Boolean(value)} onClose={onClose} maxWidth="sm" fullWidth><DialogTitle>确认精确修复</DialogTitle>{value && <DialogContent><Stack spacing={1}><Typography>问题类型：{metadataHealthFilters[value.filter]}</Typography><Typography>将处理：{value.count} 部影片</Typography><Typography>目标字段：{value.targets.join('、')}</Typography><Typography>网络 Provider：是（MDC-NG → MetaTube → JavBus）</Typography><Typography>下载图片：{value.targets.some(item => ['Poster', 'Fanart', 'Preview'].includes(item)) ? '是' : '否'}</Typography><Typography>生成 NFO：{value.targets.includes('NFO') ? '是' : '否'}</Typography><Alert severity={value.storage.available ? 'success' : 'warning'}>MediaStorage：{value.storage.available ? '可用' : `不可用：${value.storage.rootPath} ${value.storage.error ?? ''}`}</Alert></Stack></DialogContent>}<DialogActions><Button onClick={onClose}>取消</Button><Button variant="contained" disabled={!value || value.count === 0 || (writesFiles && !value.storage.available)} onClick={onConfirm}>创建修复任务</Button></DialogActions></Dialog>
}

function PathSection({ title, items }: { title: string; items: { path: string; reason?: string }[] }) { return <SurfaceSection title={title} description="只显示诊断结果，不自动修改文件。"><Stack spacing={1} sx={{ maxHeight: 360, overflowY: 'auto' }}>{items.length ? items.map(item => <Card key={item.path} variant="outlined"><CardContent sx={{ py: 1.25, '&:last-child': { pb: 1.25 } }}><Typography sx={{ fontWeight: 750, overflowWrap: 'anywhere' }}>{item.path}</Typography>{item.reason && <Typography variant="body2" color="text.secondary">{item.reason}</Typography>}<Button size="small" onClick={() => openDirectory(item.path)}>定位目录</Button></CardContent></Card>) : <Typography color="text.secondary">无</Typography>}</Stack></SurfaceSection> }
