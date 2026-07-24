import AutoFixHighRoundedIcon from '@mui/icons-material/AutoFixHighRounded'
import BrokenImageRoundedIcon from '@mui/icons-material/BrokenImageRounded'
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import DescriptionRoundedIcon from '@mui/icons-material/DescriptionRounded'
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded'
import ImageRoundedIcon from '@mui/icons-material/ImageRounded'
import PersonRoundedIcon from '@mui/icons-material/PersonRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import SellRoundedIcon from '@mui/icons-material/SellRounded'
import TaskAltRoundedIcon from '@mui/icons-material/TaskAltRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Box, Button, Card, CardActionArea, CardContent, Checkbox, Chip, InputAdornment, MenuItem, Paper, Snackbar, Stack, Tab, Tabs, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { EmptyState, HealthMeter, StatCard, SurfaceSection } from '@/components/ProductComponents'
import { SmartImage } from '@/components/SmartImage'
import { StatusBadge, type StatusTone } from '@/components/workspace/StatusBadges'
import { WorkspacePage } from '@/components/workspace/Workspace'
import OrganizerPage from '@/pages/OrganizerPage'
import { BRIDGE_ORIGIN, bridge } from '@/services/bridge'
import type { MaintenanceIssue, MaintenanceReport, MetadataOverview } from '@/types/media'

type DataCenterTab = 'overview' | 'problems' | 'duplicates'
type ProblemStatus = 'all' | 'normal' | 'abnormal' | 'auto' | 'manual'
type ProblemType = 'all' | 'missing-cover' | 'missing-fanart' | 'missing-preview' | 'missing-screenshot' | 'missing-gif' | 'missing-nfo' | 'missing-actors' | 'actor-profile-missing' | 'missing-directors' | 'missing-description' | 'missing-tags' | 'missing-series' | 'missing-studios' | 'image-path-invalid' | 'image-file-missing' | 'legacy-data' | 'relation-missing'

const problemTypeLabels: Record<ProblemType, string> = {
  all: '全部问题',
  'missing-cover': '缺封面',
  'missing-fanart': '缺背景图',
  'missing-preview': '缺预览图',
  'missing-screenshot': '缺截图',
  'missing-gif': '缺 GIF',
  'missing-nfo': '缺 NFO',
  'missing-actors': '缺演员',
  'actor-profile-missing': '演员资料缺失',
  'missing-directors': '缺导演',
  'missing-description': '缺简介',
  'missing-tags': '缺标签',
  'missing-series': '缺系列',
  'missing-studios': '缺厂商',
  'image-path-invalid': '图片路径失效',
  'image-file-missing': '图片文件不存在',
  'legacy-data': '历史迁移',
  'relation-missing': '数据库关联缺失',
}

const problemStatusLabels: Record<ProblemStatus, string> = {
  all: '全部',
  normal: '正常',
  abnormal: '异常',
  auto: '可自动处理',
  manual: '需要人工处理',
}

export default function DataCenterPage() {
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const tab = normalizeTab(params.get('tab'))
  const [query, setQuery] = useState(params.get('q') ?? '')
  const setTab = (next: DataCenterTab) => {
    const value = new URLSearchParams(params)
    value.set('tab', next)
    setParams(value, { replace: true })
  }
  const openIssue = (type: ProblemType) => {
    const value = new URLSearchParams(params)
    value.set('tab', 'problems')
    value.set('type', type)
    if (query.trim()) value.set('q', query.trim())
    else value.delete('q')
    setParams(value)
  }
  const openDuplicates = () => {
    const value = new URLSearchParams(params)
    value.set('tab', 'duplicates')
    setParams(value)
  }
  useEffect(() => {
    const value = new URLSearchParams(params)
    if (query.trim()) value.set('q', query.trim())
    else value.delete('q')
    setParams(value, { replace: true })
  }, [query])

  return <WorkspacePage title="数据中心" description="统一查看完整性、诊断资源问题、处理重复影片和维护数据任务。"
    primaryActions={[{ key: 'metadata-repair', label: '元数据修复', icon: <AutoFixHighRoundedIcon/>, variant: 'outlined', onClick: () => navigate('/metadata-repair') }]}>
    <Paper variant="outlined" sx={{ borderRadius: 2.5, mb: 1.5, overflow: 'hidden' }}>
      <Box sx={{ p: 1.25, display: 'flex', gap: 1.25, alignItems: 'center', flexWrap: 'wrap' }}>
        <Tabs value={tab} onChange={(_, value) => setTab(value)} variant="scrollable" scrollButtons="auto" sx={{ minHeight: 40, flex: '1 1 auto' }}>
          <Tab value="overview" label="概览"/>
          <Tab value="problems" label="问题"/>
          <Tab value="duplicates" label="重复影片"/>
        </Tabs>
        <TextField size="small" placeholder={tab === 'duplicates' ? '搜索番号、标题、路径' : '搜索番号、标题、演员、导演、路径'} value={query} onChange={(event) => setQuery(event.target.value)}
          sx={{ width: { xs: '100%', sm: 320 } }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon fontSize="small"/></InputAdornment> } }}/>
      </Box>
    </Paper>
    {tab === 'overview' && <OverviewTab query={query} onOpenIssue={openIssue} onOpenDuplicates={openDuplicates}/>}
    {tab === 'problems' && <ProblemsTab query={query} initialType={normalizeProblemType(params.get('type'))} onClearQuery={() => setQuery('')}/>}
    {tab === 'duplicates' && <OrganizerPage search={query}/>}
  </WorkspacePage>
}

function OverviewTab({ query, onOpenIssue, onOpenDuplicates }: { query: string; onOpenIssue: (type: ProblemType) => void; onOpenDuplicates: () => void }) {
  const [overview, setOverview] = useState<MetadataOverview>()
  const [maintenance, setMaintenance] = useState<MaintenanceReport>()
  const [error, setError] = useState('')
  const load = useCallback(() => {
    setError('')
    Promise.all([bridge.metadataOverview(), bridge.maintenanceReport(200, 0)])
      .then(([metadata, report]) => { setOverview(metadata); setMaintenance(report) })
      .catch((reason: Error) => setError(reason.message))
  }, [])
  useEffect(load, [load])
  if (error) return <Alert severity="error">{error}</Alert>
  if (!overview || !maintenance) return <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><Typography color="text.secondary">正在加载数据中心...</Typography></Box>

  const total = Math.max(1, overview.totalMovies)
  const metadataRate = clampPercent(Math.round((overview.completeMovies / total) * 100))
  const imageMissing = maintenance.stats.missingImages
  const imageRate = clampPercent(Math.round(((overview.totalMovies - imageMissing) / total) * 100))
  const duplicateRate = clampPercent(Math.round((maintenance.stats.duplicateMovies / total) * 100))
  const problemRate = clampPercent(Math.round((maintenance.stats.problemMovies / total) * 100))
  const healthScore = clampPercent(Math.round(metadataRate * .35 + imageRate * .35 + (100 - duplicateRate) * .15 + (100 - problemRate) * .15))
  const healthTone = healthScore >= 90 ? 'success' : healthScore >= 70 ? 'warning' : 'error'
  const healthLabel = healthScore >= 90 ? '优秀' : healthScore >= 70 ? '需要关注' : '需要维护'
  const resources = resourceStats(overview, maintenance)
  const filteredResources = filterByQuery(resources, query, (item) => item.label)
  const recommendations = recommendedActions(overview, maintenance, onOpenIssue, onOpenDuplicates).filter((item) => matchesQuery(`${item.label} ${item.description}`, query))

  return <Stack spacing={1.5}>
    <SurfaceSection title="媒体库健康度" description="根据元数据完整率、图片完整率、重复影片和异常影片综合估算。"
      action={<Button size="small" variant="outlined" onClick={() => onOpenIssue('all')}>查看问题</Button>}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(260px,.8fr) minmax(0,1.2fr)' }, gap: 1.5, alignItems: 'center' }}>
        <Box sx={{ p: 1.5, borderRadius: 2, bgcolor: 'action.hover' }}>
          <Typography variant="h3" sx={{ fontWeight: 950, lineHeight: 1 }}>{healthScore}%</Typography>
          <Typography sx={{ mt: .5, fontWeight: 850, color: `${healthTone}.main` }}>{healthLabel}</Typography>
          <HealthMeter label="综合健康度" value={healthScore} detail={`${metadataRate}% 元数据 / ${imageRate}% 图片`} tone={healthTone}/>
        </Box>
        <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(150px,1fr))', gap: 1 }}>
          <ClickableStat label="影片总数" value={overview.totalMovies} icon={<TaskAltRoundedIcon/>}/>
          <ClickableStat label="异常影片" value={maintenance.stats.problemMovies} icon={<WarningAmberRoundedIcon/>} tone="warning.main" onClick={() => onOpenIssue('all')}/>
          <ClickableStat label="重复影片" value={maintenance.stats.duplicateMovies} icon={<ContentCopyRoundedIcon/>} tone="warning.main" onClick={onOpenDuplicates}/>
          <ClickableStat label="图片完整率" value={`${imageRate}%`} icon={<BrokenImageRoundedIcon/>} tone={imageRate >= 90 ? 'success.main' : 'warning.main'} onClick={() => onOpenIssue('missing-cover')}/>
        </Box>
      </Box>
    </SurfaceSection>

    <SurfaceSection title="建议处理" description="只做导航跳转，不自动修复或批量修改数据。">
      {recommendations.length ? <Stack spacing={.85}>
        {recommendations.map((item) => <Card key={item.key} variant="outlined" sx={{ borderRadius: 2 }}>
          <CardActionArea onClick={item.onClick}>
            <CardContent sx={{ p: 1.25, '&:last-child': { pb: 1.25 } }}>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { xs: 'flex-start', sm: 'center' }, justifyContent: 'space-between' }}>
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center', minWidth: 0 }}>
                  <Box sx={{ width: 9, height: 9, borderRadius: 999, bgcolor: `${item.tone}.main`, flex: '0 0 auto' }}/>
                  <Box sx={{ minWidth: 0 }}>
                    <Typography sx={{ fontWeight: 900 }}>{item.label}</Typography>
                    <Typography variant="body2" color="text.secondary" noWrap>{item.description}</Typography>
                  </Box>
                </Stack>
                <Button size="small" variant="outlined" onClick={(event) => { event.stopPropagation(); item.onClick() }}>{item.action}</Button>
              </Stack>
            </CardContent>
          </CardActionArea>
        </Card>)}
      </Stack> : <EmptyState title="暂无匹配建议" description="当前搜索条件下没有需要优先处理的维护项。"/>}
    </SurfaceSection>

    <SurfaceSection title="资源统计" description="点击缺失项进入问题页并自动带入筛选。">
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1 }}>
        {filteredResources.map((item) => {
          const normal = Math.max(0, overview.totalMovies - item.missing)
          const percent = clampPercent(Math.round((normal / total) * 100))
          return <Card key={item.type} variant="outlined" onClick={() => onOpenIssue(item.type)} sx={{ cursor: 'pointer', borderRadius: 2, '&:hover': { borderColor: item.missing ? 'warning.main' : 'success.main', bgcolor: 'action.hover' } }}>
            <CardContent sx={{ p: 1.25, '&:last-child': { pb: 1.25 } }}>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: .85 }}>
                <Box sx={{ color: item.missing ? 'warning.main' : 'success.main', display: 'grid' }}>{item.icon}</Box>
                <Typography sx={{ fontWeight: 850 }}>{item.label}</Typography>
              </Stack>
              <HealthMeter label={`${normal} / ${overview.totalMovies}`} value={percent} detail={`缺失 ${item.missing}`} tone={item.missing ? 'warning' : 'success'}/>
            </CardContent>
          </Card>
        })}
      </Box>
    </SurfaceSection>
  </Stack>
}

function ProblemsTab({ query, initialType, onClearQuery }: { query: string; initialType: ProblemType; onClearQuery: () => void }) {
  const navigate = useNavigate()
  const [report, setReport] = useState<MaintenanceReport>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [status, setStatus] = useState<ProblemStatus>('all')
  const [type, setType] = useState<ProblemType>(initialType)
  const [selected, setSelected] = useState<number[]>([])
  const [visibleCount, setVisibleCount] = useState(60)
  const [loading, setLoading] = useState(false)
  const load = useCallback(() => {
    setError('')
    setLoading(true)
    bridge.maintenanceReport(500, 0)
      .then((next) => { setReport(next); setNotice(`发现 ${next.issues.length} 个问题`) })
      .catch((reason: Error) => setError(reason.message))
      .finally(() => setLoading(false))
  }, [])
  useEffect(load, [load])
  useEffect(() => setType(initialType), [initialType])
  useEffect(() => { setVisibleCount(60); setSelected([]) }, [query, status, type])

  const allProblems = useMemo(() => (report?.issues ?? []).map(toProblem), [report])
  const problems = useMemo(() => allProblems.filter((item) => {
    if (status === 'normal') return false
    if (status === 'abnormal' && item.status !== '异常') return false
    if (status === 'auto' && !item.supportedActions.some((action) => action.enabled)) return false
    if (status === 'manual' && item.supportedActions.some((action) => action.enabled)) return false
    if (type !== 'all' && item.type !== type) return false
    return matchesQuery(`${item.code} ${item.title} ${item.detail} ${item.path ?? ''} ${item.source} ${item.reason} ${item.stateDescription}`, query)
  }), [allProblems, query, status, type])
  const activeFilterCount = (status === 'all' ? 0 : 1) + (type === 'all' ? 0 : 1) + (query.trim() ? 1 : 0)
  const selectedProblems = problems.filter((item) => item.movieId && selected.includes(item.movieId))
  const selectedTypes = [...new Set(selectedProblems.map((item) => item.type))]
  const isMixedSelection = selectedTypes.length > 1
  const canBatchSync = selectedProblems.length > 0 && !isMixedSelection && selectedProblems.every((item) => canSyncIssue(item.type) && item.movieId)
  const visibleProblems = problems.slice(0, visibleCount)
  const hasMore = visibleProblems.length < problems.length
  const clearFilters = () => { setStatus('all'); setType('all'); onClearQuery() }
  const syncOne = (movieId?: number, source?: 'JavBus') => {
    if (!movieId) return
    bridge.syncMovie(movieId, source).then(result => setNotice(`${result.message} 可在任务中心查看。`)).catch((reason: Error) => setNotice(reason.message))
  }
  const fillActorProfile = (actorId?: number) => {
    if (!actorId) return
    bridge.actorProfilePreview(actorId).then(async preview => {
      if (preview.candidates.length !== 1) { setNotice(preview.warnings[0] || `找到 ${preview.candidates.length} 个候选，请在演员资料中人工确认。`); return }
      const result = await bridge.applyActorProfile(actorId, preview.candidates[0])
      setNotice(result.updatedFields.length ? `已补充：${result.updatedFields.join('、')}` : result.conflicts.length ? '现有资料已保留，检测到来源冲突。' : '没有需要补充的字段。')
      load()
    }).catch((reason: Error) => setNotice(reason.message))
  }
  const syncSelected = () => {
    if (!canBatchSync) return
    const ids = selectedProblems.map((item) => item.movieId!)
    if (!ids.length) return
    bridge.createBatchSync(ids).then(result => { setNotice(`${result.message} 可在任务中心查看。`); setSelected([]) }).catch((reason: Error) => setNotice(reason.message))
  }
  const filters = <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.1} useFlexGap sx={{ alignItems: { xs: 'stretch', lg: 'center' }, flexWrap: 'wrap' }}>
    <TextField select size="small" label="状态" value={status} onChange={(event) => setStatus(event.target.value as ProblemStatus)} sx={{ width: { xs: '100%', sm: 170 } }}>
      <MenuItem value="all">全部</MenuItem>
      <MenuItem value="normal">正常</MenuItem>
      <MenuItem value="abnormal">异常</MenuItem>
      <MenuItem value="auto">可自动修复</MenuItem>
      <MenuItem value="manual">需要人工处理</MenuItem>
    </TextField>
    <TextField select size="small" label="问题类型" value={type} onChange={(event) => setType(event.target.value as ProblemType)} sx={{ width: { xs: '100%', sm: 190 } }}>
      {Object.entries(problemTypeLabels).map(([value, label]) => <MenuItem key={value} value={value}>{label}</MenuItem>)}
    </TextField>
    <Button variant="outlined" disabled={!canBatchSync} onClick={syncSelected}>批量重新同步</Button>
    <Button variant="outlined" disabled={!selected.length} onClick={() => setSelected([])}>取消选择</Button>
    {activeFilterCount > 0 && <Button color="inherit" onClick={clearFilters}>清除筛选</Button>}
  </Stack>
  return <Stack spacing={1.5}>
    <SurfaceSection title="问题" description="发现问题、查看原因并执行现有修复能力。耗时操作继续进入任务中心。" action={<Button size="small" variant="outlined" disabled={loading} onClick={load}>{loading ? '正在扫描问题…' : '扫描问题'}</Button>}>
      {filters}
    </SurfaceSection>
    <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap' }}>
      <Chip size="small" label={`当前：${problemTypeLabels[type]}`}/>
      <Chip size="small" label={`状态：${problemStatusLabels[status]}`}/>
      {query.trim() && <Chip size="small" label={`搜索：${query.trim()}`}/>}
      <Chip size="small" label={`结果：${problems.length}`}/>
      {selectedProblems.length > 0 && <Chip size="small" color="primary" label={`已选择 ${selectedProblems.length} 项`}/>}
    </Stack>
    {selectedProblems.length > 0 && <Alert severity={canBatchSync ? 'info' : 'warning'}>
      {canBatchSync ? `将对 ${selectedProblems.length} 项执行“批量重新同步”，任务会进入任务中心。` : isMixedSelection ? '当前选择包含多种问题类型，请只选择同一种问题后再执行批量操作。' : '当前选择的问题暂不支持批量自动处理。'}
    </Alert>}
    {error && <Alert severity="error">{error}</Alert>}
    {!report && !error ? <Stack spacing={1} sx={{ alignItems: 'center' }}><EmptyState title="尚未完成问题扫描" description="请先扫描媒体库问题。"/><Button variant="contained" onClick={load}>扫描问题</Button></Stack> :
      status === 'normal' ? <EmptyState title="正常影片不在问题列表展示" description="当前页面聚焦异常诊断与修复；概览中可查看正常数量。"/> :
      problems.length ? <Stack spacing={1}>
        {visibleProblems.map((item, index) => <ProblemCard key={`${item.movieId}-${item.type}-${index}`} item={item} selected={Boolean(item.movieId && selected.includes(item.movieId))}
          onSelect={(checked) => item.movieId && setSelected((current) => checked ? [...new Set([...current, item.movieId!])] : current.filter(id => id !== item.movieId))}
          onOpen={() => item.movieId && navigate(`/movies/${item.movieId}`)} onRepair={(source) => syncOne(item.movieId, source)} onProfile={() => fillActorProfile(item.actorId)} onCopyPath={() => copyPath(item.path, setNotice)} onOpenPath={() => openPath(item.path, setNotice)}/>)}
        {hasMore && <Button variant="outlined" onClick={() => setVisibleCount((current) => current + 60)}>加载更多（{visibleProblems.length} / {problems.length}）</Button>}
      </Stack> : <ProblemEmptyState query={query} type={type} reportIssueCount={allProblems.length} onScan={load} onClear={clearFilters}/>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Stack>
}

function ProblemCard({ item, selected, onSelect, onOpen, onRepair, onProfile, onCopyPath, onOpenPath }: { item: ProblemItem; selected: boolean; onSelect: (checked: boolean) => void; onOpen: () => void; onRepair: (source?: 'JavBus') => void; onProfile: () => void; onCopyPath: () => void; onOpenPath: () => void }) {
  return <Card variant="outlined" sx={{ borderRadius: 2, borderLeft: 4, borderLeftColor: `${item.reasonTone}.main` }}>
    <CardContent sx={{ p: 1.25, '&:last-child': { pb: 1.25 } }}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'auto minmax(0,1fr)', md: '58px minmax(170px,.75fr) minmax(0,1.4fr) minmax(220px,1fr) minmax(210px,.8fr)' }, gap: 1.25, alignItems: 'center' }}>
        <Stack direction="row" spacing={.5} sx={{ alignItems: 'center' }}>
          {item.movieId && <Checkbox checked={selected} onChange={(event) => onSelect(event.target.checked)}/>}
          <ProblemPoster movieId={item.movieId} title={item.code || item.title}/>
        </Stack>
        <Stack sx={{ minWidth: 0 }}>
          <Typography sx={{ fontWeight: 900 }} noWrap>{item.code || item.title || '未命名影片'}</Typography>
          <Typography variant="body2" color="text.secondary" noWrap>{item.title || '无标题'}</Typography>
          <Stack direction="row" spacing={.5} useFlexGap sx={{ flexWrap: 'wrap', mt: .6 }}>
            <StatusBadge label={item.source} tone="neutral"/>
            <StatusBadge label={item.status} tone={item.supportedActions.some((action) => action.enabled) ? 'info' : 'warning'}/>
          </Stack>
        </Stack>
        <Stack spacing={.45} sx={{ minWidth: 0 }}>
          <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap' }}>
            <StatusBadge label={problemTypeLabels[item.type]} tone={item.severityTone}/>
            <StatusBadge label={item.reasonLabel} tone={item.reasonTone}/>
          </Stack>
          <InfoLine label="问题" value={problemTypeLabels[item.type]}/>
          <InfoLine label="原因" value={item.reason}/>
          <InfoLine label="状态" value={item.stateDescription}/>
          <InfoLine label="来源" value={item.source}/>
          {item.path && <InfoLine label="路径" value={item.path} mono/>}
        </Stack>
        <Stack spacing={.35} sx={{ minWidth: 0 }}>
          <Typography variant="body2" sx={{ fontWeight: 850 }}>建议</Typography>
          <Typography variant="body2" color="text.secondary">{item.suggestion}</Typography>
        </Stack>
        <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', justifyContent: 'flex-end' }}>
          {item.movieId && <Button size="small" variant="outlined" onClick={onOpen}>查看影片</Button>}
          {item.supportedActions.map((action) => <Tooltip key={action.label} title={action.disabledReason ?? ''}>
            <span><Button size="small" variant={action.primary ? 'contained' : 'outlined'} color={action.color ?? 'primary'} disabled={!action.enabled} onClick={action.kind === 'sync' ? () => onRepair() : action.kind === 'javbus' ? () => onRepair('JavBus') : action.kind === 'profile' ? onProfile : action.kind === 'copy-path' ? onCopyPath : onOpenPath}>{action.label}</Button></span>
          </Tooltip>)}
          {item.unsupportedActions.map((action) => <Tooltip key={action.label} title={action.reason}>
            <span><Button size="small" color="inherit" disabled>{action.label}</Button></span>
          </Tooltip>)}
        </Stack>
      </Box>
    </CardContent>
  </Card>
}

function ProblemPoster({ movieId, title }: { movieId?: number; title: string }) {
  return <Box sx={{ width: 46, height: 62, borderRadius: 1.25, overflow: 'hidden', bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}>
    {movieId ? <SmartImage src={`${BRIDGE_ORIGIN}/api/images/${movieId}/primary?variant=thumbnail`} alt={title}/> : <BrokenImageRoundedIcon color="disabled"/>}
  </Box>
}

function InfoLine({ label, value, mono = false }: { label: string; value?: string; mono?: boolean }) {
  if (!value) return null
  return <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere', fontFamily: mono ? 'monospace' : undefined }}>
    <Box component="span" sx={{ color: 'text.primary', fontWeight: 800 }}>{label}：</Box>{value}
  </Typography>
}

function ProblemEmptyState({ query, type, reportIssueCount, onScan, onClear }: { query: string; type: ProblemType; reportIssueCount: number; onScan: () => void; onClear: () => void }) {
  if (reportIssueCount === 0) {
    return <Stack spacing={1} sx={{ alignItems: 'center' }}><EmptyState title="未发现问题" description="当前媒体库没有已识别的问题项。"/><Button variant="outlined" onClick={onScan}>重新扫描问题</Button></Stack>
  }
  if (!query.trim() && type !== 'all') {
    return <EmptyState title={`未发现“${problemTypeLabels[type]}”问题`} description="当前媒体库中没有符合该条件的项目。"/>
  }
  return <Stack spacing={1} sx={{ alignItems: 'center' }}><EmptyState title="没有匹配的结果" description="请调整搜索词或清除筛选条件。"/><Button variant="outlined" onClick={onClear}>清除筛选</Button></Stack>
}

function ClickableStat({ label, value, icon, tone = 'primary.main', onClick }: { label: string; value: string | number; icon: ReactNode; tone?: string; onClick?: () => void }) {
  const content = <StatCard label={label} value={value} icon={icon} tone={tone}/>
  return onClick ? <Box onClick={onClick} sx={{ cursor: 'pointer' }}>{content}</Box> : content
}

interface ProblemItem {
  movieId?: number
  actorId?: number
  code: string
  title: string
  severityTone: StatusTone
  type: ProblemType
  status: string
  detail: string
  path?: string
  reason: string
  reasonLabel: string
  reasonTone: StatusTone
  stateDescription: string
  suggestion: string
  source: string
  supportedActions: ProblemAction[]
  unsupportedActions: UnsupportedProblemAction[]
}

interface ProblemAction {
  label: string
  kind: 'sync' | 'javbus' | 'profile' | 'copy-path' | 'open-path'
  enabled: boolean
  primary?: boolean
  color?: 'primary' | 'inherit'
  disabledReason?: string
}

interface UnsupportedProblemAction {
  label: string
  reason: string
}

function toProblem(item: MaintenanceIssue): ProblemItem {
  const inferred = inferProblemType(item)
  const legacy = isLegacyIssue(item)
  const fileMissing = isFileMissingIssue(item)
  const relationMissing = ['missing-actors', 'missing-directors', 'missing-tags', 'missing-series', 'missing-studios'].includes(inferred)
  const type = legacy ? 'legacy-data' : fileMissing ? 'image-file-missing' : relationMissing ? 'relation-missing' : inferred
  const name = item.detail?.split(' · ')[0]?.split(' 路 ')[0] || item.title
  const code = item.detail?.match(/[A-Z]{2,10}-\d{2,6}/i)?.[0] ?? name
  const reason = reasonFor(item, type)
  const source = sourceFor(item, type)
  const actions = actionsFor(item, type)
  return {
    movieId: item.movieId,
    actorId: item.actorId,
    code,
    title: name,
    severityTone: item.severity === 'error' ? 'error' : item.severity === 'warning' ? 'warning' : 'info',
    type,
    status: '异常',
    detail: item.detail,
    path: item.path,
    reason,
    reasonLabel: reasonLabel(type),
    reasonTone: reasonTone(type),
    stateDescription: stateDescription(item, type),
    suggestion: repairSuggestion(type),
    source,
    supportedActions: actions.supported,
    unsupportedActions: actions.unsupported,
  }
}

function inferProblemType(item: MaintenanceIssue): ProblemType {
  const text = `${item.category} ${item.title} ${item.detail}`.toLowerCase()
  if (text.includes('演员资料缺失')) return 'actor-profile-missing'
  if (text.includes('fanart') || text.includes('背景')) return 'missing-fanart'
  if (text.includes('preview') || text.includes('预览')) return 'missing-preview'
  if (text.includes('screenshot') || text.includes('截图')) return 'missing-screenshot'
  if (text.includes('gif')) return 'missing-gif'
  if (text.includes('nfo')) return 'missing-nfo'
  if (text.includes('演员')) return 'missing-actors'
  if (text.includes('导演')) return 'missing-directors'
  if (text.includes('简介') || text.includes('description') || text.includes('metadata')) return 'missing-description'
  if (text.includes('标签')) return 'missing-tags'
  if (text.includes('系列')) return 'missing-series'
  if (text.includes('厂商')) return 'missing-studios'
  if (text.includes('图片') || text.includes('封面')) return 'missing-cover'
  if (text.includes('关联')) return 'relation-missing'
  return 'all'
}

function reasonFor(item: MaintenanceIssue, type: ProblemType) {
  if (type === 'legacy-data') return item.path ? '历史迁移路径失效。' : '历史迁移数据未包含此字段。'
  if (type === 'image-file-missing') return '数据库有记录，但本地文件不存在。'
  if (type === 'image-path-invalid') return '文件路径不可访问。'
  if (type === 'relation-missing') return '数据库缺少对应关联记录。'
  if (type === 'missing-nfo') return 'NFO 未生成。'
  if (type === 'missing-cover' || type === 'missing-fanart' || type === 'missing-preview' || type === 'missing-screenshot' || type === 'missing-gif') return '图片索引中没有可用记录。'
  if (type.startsWith('missing-')) return '数据库没有记录，或当前刮削数据源未返回。'
  return item.detail || item.title || '暂时无法确定。'
}

function reasonLabel(type: ProblemType) {
  if (type === 'legacy-data') return '历史迁移'
  if (type === 'relation-missing') return '关联缺失'
  if (type === 'image-path-invalid') return '路径不可访问'
  if (type === 'image-file-missing') return '文件不存在'
  if (type === 'missing-cover' || type === 'missing-fanart' || type === 'missing-preview' || type === 'missing-screenshot' || type === 'missing-gif') return '图片索引失效'
  if (type === 'missing-nfo') return 'NFO 未生成'
  if (type === 'all') return '未知'
  return '数据库记录缺失'
}

function reasonTone(type: ProblemType): StatusTone {
  if (type === 'legacy-data') return 'warning'
  if (type === 'relation-missing') return 'warning'
  if (type === 'image-file-missing' || type === 'image-path-invalid') return 'error'
  if (type === 'all') return 'neutral'
  return 'info'
}

function stateDescription(item: MaintenanceIssue, type: ProblemType) {
  if (type === 'image-file-missing') return '数据库记录存在；本地文件不存在。'
  if (type === 'image-path-invalid') return '数据库记录存在；路径当前不可访问。'
  if (type === 'legacy-data') return item.path ? '数据库仍记录旧版路径；运行时不再使用旧图。' : '历史迁移数据中缺少该字段。'
  if (type === 'relation-missing') return '影片存在，但关联表中没有对应关系。'
  if (type === 'missing-nfo') return '数据库未记录可用 NFO 文件。'
  if (item.path) return '影片记录存在，相关资源未完整登记。'
  return '数据库当前未提供可用记录。'
}

function sourceFor(item: MaintenanceIssue, type: ProblemType) {
  const text = `${item.category} ${item.title} ${item.detail} ${item.path ?? ''}`
  if (/JVDIO|Jvedio|BigPic|SmallPic|ExtraPic|Legacy/i.test(text)) return 'Jvedio5'
  if (type === 'missing-nfo') return 'NFO'
  if (type.startsWith('missing-') || type === 'relation-missing') return '数据库'
  if (type === 'image-file-missing' || type === 'image-path-invalid') return '本地文件'
  return '数据库'
}

function repairSuggestion(type: ProblemType) {
  if (type === 'missing-cover') return '重新获取封面；当前可先重新同步元数据。'
  if (type === 'missing-fanart') return '重新检测本地背景图；仍未找到时重新获取背景图。'
  if (type === 'missing-preview') return '重新检测本地图片；仍未找到时重新生成预览图。'
  if (type === 'missing-screenshot') return '提交生成截图任务。'
  if (type === 'missing-gif') return '提交生成 GIF 任务。'
  if (type === 'missing-nfo') return '根据当前数据库元数据生成 NFO。'
  if (type === 'missing-directors') return '重新同步导演信息。'
  if (type === 'missing-actors') return '重新建立演员关联或重新同步演员信息。'
  if (type === 'legacy-data') return '重新定位文件或重新获取对应资源。'
  if (type === 'relation-missing') return '重新建立关联。'
  if (type === 'image-file-missing' || type === 'image-path-invalid') return '重新检测本地图片路径；必要时重新获取对应资源。'
  if (type.startsWith('missing-')) return '重新同步缺失元数据。'
  return '扫描问题后按问题类型处理。'
}

function canSyncIssue(type: ProblemType) {
  return !['image-path-invalid', 'image-file-missing', 'legacy-data', 'missing-nfo', 'missing-screenshot', 'missing-gif', 'missing-preview', 'all'].includes(type)
}

function actionsFor(item: MaintenanceIssue, type: ProblemType) {
  const hasMovie = Boolean(item.movieId)
  const hasPath = Boolean(item.path)
  const supported: ProblemAction[] = [
    ...(type === 'actor-profile-missing' ? [{ label: '补全演员资料', kind: 'profile' as const, enabled: Boolean(item.actorId), primary: true, disabledReason: '仅处理明确匹配的单一候选' }] : []),
    { label: '重新同步', kind: 'sync', enabled: hasMovie && canSyncIssue(type), primary: true, disabledReason: '当前问题暂不支持通过元数据同步直接处理' },
    { label: '使用 JavBus 补充', kind: 'javbus', enabled: hasMovie && canJavBusRepair(type), disabledReason: 'JavBus 仅用于补充缺失元数据和封面' },
    { label: '打开所在文件夹', kind: 'open-path', enabled: hasPath, color: 'inherit', disabledReason: '没有可打开的相关路径' },
    { label: '复制路径', kind: 'copy-path', enabled: hasPath, color: 'inherit', disabledReason: '没有可复制的相关路径' },
  ]
  const unsupported: UnsupportedProblemAction[] = unsupportedActionsFor(type)
  return { supported, unsupported }
}

function canJavBusRepair(type: ProblemType) {
  return ['missing-cover', 'missing-directors', 'missing-actors', 'missing-series', 'missing-studios', 'missing-tags', 'missing-description'].includes(type)
}

function unsupportedActionsFor(type: ProblemType): UnsupportedProblemAction[] {
  const unsupportedReason = '暂不支持：当前没有稳定的单项修复接口，需复用已有同步或详情页流程。'
  if (type === 'missing-cover') return [{ label: '重新获取封面', reason: unsupportedReason }]
  if (type === 'missing-fanart') return [{ label: '重新检测', reason: unsupportedReason }, { label: '重新获取背景图', reason: unsupportedReason }]
  if (type === 'missing-preview') return [{ label: '重新检测', reason: unsupportedReason }]
  if (type === 'missing-nfo') return [{ label: '生成 NFO', reason: '暂不支持：NFO 生成需要沿用详情页预览和确认流程。' }]
  if (type === 'missing-actors') return [{ label: '重建演员关联', reason: unsupportedReason }]
  if (type === 'missing-directors') return [{ label: '重新获取导演', reason: unsupportedReason }]
  if (type === 'relation-missing') return [{ label: '重建关联', reason: unsupportedReason }]
  if (type === 'image-file-missing' || type === 'image-path-invalid' || type === 'legacy-data') return [{ label: '重新检测', reason: unsupportedReason }]
  return [{ label: '忽略问题', reason: '暂不支持：忽略状态尚未进入已验证的任务闭环。' }]
}

function copyPath(path: string | undefined, setNotice: (value: string) => void) {
  if (!path) { setNotice('没有可复制的路径'); return }
  navigator.clipboard.writeText(path).then(() => setNotice('已复制路径')).catch(() => setNotice('复制路径失败'))
}

function openPath(path: string | undefined, setNotice: (value: string) => void) {
  if (!path) { setNotice('没有可打开的路径'); return }
  const target = path.includes('.') ? path : `${path}\\`
  const action = path.includes('.') ? bridge.revealFile(target) : bridge.openDirectory(target)
  action.then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))
}

function isLegacyIssue(item: MaintenanceIssue) {
  return /JVDIO|Jvedio|BigPic|SmallPic|ExtraPic|Legacy/i.test(`${item.path ?? ''} ${item.detail}`)
}

function isFileMissingIssue(item: MaintenanceIssue) {
  return Boolean(item.path && /不存在|Missing|缺失/i.test(`${item.title} ${item.detail}`))
}

function resourceStats(overview: MetadataOverview, maintenance: MaintenanceReport) {
  return [
    { label: '封面', type: 'missing-cover' as const, missing: overview.missingCover, icon: <ImageRoundedIcon/> },
    { label: '背景图', type: 'missing-fanart' as const, missing: overview.missingFanart, icon: <ImageRoundedIcon/> },
    { label: '预览图', type: 'missing-preview' as const, missing: overview.missingPreview, icon: <ImageRoundedIcon/> },
    { label: '截图', type: 'missing-screenshot' as const, missing: overview.missingScreenshots ?? 0, icon: <ImageRoundedIcon/> },
    { label: 'GIF', type: 'missing-gif' as const, missing: overview.missingGif ?? 0, icon: <ImageRoundedIcon/> },
    { label: 'NFO', type: 'missing-nfo' as const, missing: overview.missingNfo, icon: <DescriptionRoundedIcon/> },
    { label: '演员', type: 'missing-actors' as const, missing: overview.missingActors, icon: <PersonRoundedIcon/> },
    { label: '导演', type: 'missing-directors' as const, missing: overview.missingDirectors ?? 0, icon: <PersonRoundedIcon/> },
    { label: '简介', type: 'missing-description' as const, missing: overview.missingDescription, icon: <DescriptionRoundedIcon/> },
    { label: 'Provider 标签', type: 'missing-tags' as const, missing: overview.missingTags, icon: <SellRoundedIcon/> },
    { label: '系列', type: 'missing-series' as const, missing: overview.missingSeries ?? 0, icon: <SellRoundedIcon/> },
    { label: '厂商', type: 'missing-studios' as const, missing: overview.missingStudios ?? 0, icon: <SellRoundedIcon/> },
  ]
}

function recommendedActions(overview: MetadataOverview, maintenance: MaintenanceReport, onOpenIssue: (type: ProblemType) => void, onOpenDuplicates: () => void) {
  const values = [
    { key: 'fanart', label: `缺背景图（${overview.missingFanart}）`, description: '背景图缺失会影响详情和展示体验。', action: '立即查看', tone: 'warning' as const, weight: overview.missingFanart, onClick: () => onOpenIssue('missing-fanart') },
    { key: 'duplicates', label: `重复影片（${maintenance.stats.duplicateMovies}）`, description: '重复影片会占用空间并影响整理。', action: '立即查看', tone: 'warning' as const, weight: maintenance.stats.duplicateMovies, onClick: onOpenDuplicates },
    { key: 'legacy', label: '历史迁移数据', description: '检查旧路径、旧图片记录和迁移遗留项。', action: '建议重新同步', tone: 'info' as const, weight: maintenance.issues.filter(isLegacyIssue).length, onClick: () => onOpenIssue('legacy-data') },
    { key: 'nfo', label: `缺 NFO（${overview.missingNfo}）`, description: 'NFO 用于元数据交换和恢复。', action: '立即查看', tone: 'info' as const, weight: overview.missingNfo, onClick: () => onOpenIssue('missing-nfo') },
  ]
  return values.filter(item => item.weight > 0).sort((a, b) => b.weight - a.weight).slice(0, 4)
}

function normalizeTab(value: string | null): DataCenterTab {
  if (value === 'diagnostics' || value === 'problems') return 'problems'
  return value === 'duplicates' ? 'duplicates' : 'overview'
}

function normalizeProblemType(value: string | null): ProblemType {
  return value && value in problemTypeLabels ? value as ProblemType : 'all'
}

function matchesQuery(text: string, query: string) {
  const clean = query.trim().toLowerCase()
  return !clean || text.toLowerCase().includes(clean)
}

function filterByQuery<T>(items: T[], query: string, text: (item: T) => string) {
  return items.filter(item => matchesQuery(text(item), query))
}

function clampPercent(value: number) {
  return Math.max(0, Math.min(100, value))
}
