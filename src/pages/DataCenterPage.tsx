import BrokenImageRoundedIcon from '@mui/icons-material/BrokenImageRounded'
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import DescriptionRoundedIcon from '@mui/icons-material/DescriptionRounded'
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded'
import ImageRoundedIcon from '@mui/icons-material/ImageRounded'
import PersonRoundedIcon from '@mui/icons-material/PersonRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import SellRoundedIcon from '@mui/icons-material/SellRounded'
import TaskAltRoundedIcon from '@mui/icons-material/TaskAltRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Box, Button, Card, CardContent, Checkbox, Chip, Divider, FormControlLabel, InputAdornment, MenuItem, Paper, Snackbar, Stack, Tab, Tabs, TextField, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { EmptyState, HealthMeter, StatCard, SurfaceSection } from '@/components/ProductComponents'
import { SmartImage } from '@/components/SmartImage'
import { MaintenanceStatusBadge, StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import OrganizerPage from '@/pages/OrganizerPage'
import { BRIDGE_ORIGIN, bridge } from '@/services/bridge'
import type { MaintenanceIssue, MaintenanceReport, MetadataOverview } from '@/types/media'

type DataCenterTab = 'overview' | 'diagnostics' | 'duplicates'
type ProblemStatus = 'all' | 'normal' | 'abnormal' | 'auto' | 'manual'
type ProblemType = 'all' | 'missing-cover' | 'missing-fanart' | 'missing-preview' | 'missing-screenshot' | 'missing-gif' | 'missing-nfo' | 'missing-actors' | 'missing-directors' | 'missing-description' | 'missing-tags' | 'missing-series' | 'missing-studios' | 'image-path-invalid' | 'image-file-missing' | 'legacy-data' | 'relation-missing'

const problemTypeLabels: Record<ProblemType, string> = {
  all: '全部问题',
  'missing-cover': '缺封面',
  'missing-fanart': '缺背景图',
  'missing-preview': '缺预览图',
  'missing-screenshot': '缺截图',
  'missing-gif': '缺 GIF',
  'missing-nfo': '缺 NFO',
  'missing-actors': '缺演员',
  'missing-directors': '缺导演',
  'missing-description': '缺简介',
  'missing-tags': '缺标签',
  'missing-series': '缺系列',
  'missing-studios': '缺厂商',
  'image-path-invalid': '图片路径失效',
  'image-file-missing': '图片文件不存在',
  'legacy-data': '历史迁移数据',
  'relation-missing': '数据库关联缺失',
}

export default function DataCenterPage() {
  const [params, setParams] = useSearchParams()
  const tab = normalizeTab(params.get('tab'))
  const setTab = (next: DataCenterTab) => {
    const value = new URLSearchParams(params)
    value.set('tab', next)
    setParams(value, { replace: true })
  }
  return <WorkspacePage title="数据中心" description="统一查看完整性、诊断资源问题、处理重复影片和维护数据任务。">
    <Paper variant="outlined" sx={{ borderRadius: 2.5, mb: 2, overflow: 'hidden' }}>
      <Tabs value={tab} onChange={(_, value) => setTab(value)} variant="scrollable" scrollButtons="auto">
        <Tab value="overview" label="概览"/>
        <Tab value="diagnostics" label="诊断与修复"/>
        <Tab value="duplicates" label="重复影片"/>
      </Tabs>
    </Paper>
    {tab === 'overview' && <OverviewTab onOpenIssue={(type) => {
      const value = new URLSearchParams(params)
      value.set('tab', 'diagnostics')
      value.set('type', type)
      setParams(value)
    }}/>}
    {tab === 'diagnostics' && <DiagnosticsRepairTab initialType={normalizeProblemType(params.get('type'))}/>}
    {tab === 'duplicates' && <OrganizerPage/>}
  </WorkspacePage>
}

function OverviewTab({ onOpenIssue }: { onOpenIssue: (type: ProblemType) => void }) {
  const [overview, setOverview] = useState<MetadataOverview>()
  const [maintenance, setMaintenance] = useState<MaintenanceReport>()
  const [error, setError] = useState('')
  const load = useCallback(() => {
    setError('')
    Promise.all([bridge.metadataOverview(), bridge.maintenanceReport(20, 0)])
      .then(([metadata, report]) => { setOverview(metadata); setMaintenance(report) })
      .catch((reason: Error) => setError(reason.message))
  }, [])
  useEffect(load, [load])
  if (error) return <Alert severity="error">{error}</Alert>
  if (!overview || !maintenance) return <Box sx={{ minHeight: 280, display: 'grid', placeItems: 'center' }}><Typography color="text.secondary">正在加载数据中心...</Typography></Box>
  const total = Math.max(1, overview.totalMovies)
  const imageMissing = uniqueMissing([overview.missingCover, overview.missingFanart, overview.missingPreview])
  const metadataRate = Math.round((overview.completeMovies / total) * 100)
  const imageRate = Math.round(((overview.totalMovies - imageMissing) / total) * 100)
  const resources = [
    ['封面', 'missing-cover', overview.missingCover, <ImageRoundedIcon/>],
    ['背景图', 'missing-fanart', overview.missingFanart, <ImageRoundedIcon/>],
    ['预览图', 'missing-preview', overview.missingPreview, <ImageRoundedIcon/>],
    ['截图', 'missing-screenshot', overview.missingScreenshots ?? 0, <ImageRoundedIcon/>],
    ['GIF', 'missing-gif', overview.missingGif ?? 0, <ImageRoundedIcon/>],
    ['NFO', 'missing-nfo', overview.missingNfo, <DescriptionRoundedIcon/>],
    ['演员', 'missing-actors', overview.missingActors, <PersonRoundedIcon/>],
    ['导演', 'missing-directors', overview.missingDirectors ?? 0, <PersonRoundedIcon/>],
    ['简介', 'missing-description', overview.missingDescription, <DescriptionRoundedIcon/>],
    ['标签', 'missing-tags', overview.missingTags, <SellRoundedIcon/>],
    ['系列', 'missing-series', overview.missingSeries ?? 0, <SellRoundedIcon/>],
    ['厂商', 'missing-studios', overview.missingStudios ?? 0, <SellRoundedIcon/>],
  ] as const
  return <Stack spacing={2}>
    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(170px,1fr))', gap: 1.25 }}>
      <StatCard label="影片总数" value={maintenance.stats.totalMovies} icon={<TaskAltRoundedIcon/>}/>
      <StatCard label="正常影片" value={maintenance.stats.healthyMovies} icon={<TaskAltRoundedIcon/>} tone="success.main"/>
      <StatCard label="异常影片" value={maintenance.stats.problemMovies} icon={<WarningAmberRoundedIcon/>} tone="warning.main"/>
      <StatCard label="重复影片" value={maintenance.stats.duplicateMovies} icon={<ContentCopyRoundedIcon/>} tone="warning.main"/>
      <StatCard label="元数据完整率" value={`${metadataRate}%`} icon={<FactCheckRoundedIcon/>} tone={metadataRate >= 90 ? 'success.main' : 'warning.main'}/>
      <StatCard label="图片完整率" value={`${imageRate}%`} icon={<BrokenImageRoundedIcon/>} tone={imageRate >= 90 ? 'success.main' : 'warning.main'}/>
    </Box>
    <SurfaceSection title="资源统计" description="点击缺失项进入诊断与修复，并自动带入对应问题类型。">
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1.1 }}>
        {resources.map(([label, type, missing, icon]) => {
          const normal = Math.max(0, overview.totalMovies - missing)
          const percent = Math.round((normal / total) * 100)
          return <Card key={type} variant="outlined" onClick={() => onOpenIssue(type)} sx={{ cursor: 'pointer', borderRadius: 2, '&:hover': { borderColor: missing ? 'warning.main' : 'success.main', bgcolor: 'action.hover' } }}>
            <CardContent sx={{ p: 1.4, '&:last-child': { pb: 1.4 } }}>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
                <Box sx={{ color: missing ? 'warning.main' : 'success.main', display: 'grid' }}>{icon}</Box>
                <Typography sx={{ fontWeight: 850 }}>{label}</Typography>
              </Stack>
              <HealthMeter label={`${normal} / ${overview.totalMovies}`} value={percent} detail={`缺失 ${missing}`} tone={missing ? 'warning' : 'success'}/>
            </CardContent>
          </Card>
        })}
      </Box>
    </SurfaceSection>
  </Stack>
}

function DiagnosticsRepairTab({ initialType }: { initialType: ProblemType }) {
  const navigate = useNavigate()
  const [report, setReport] = useState<MaintenanceReport>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [status, setStatus] = useState<ProblemStatus>('all')
  const [type, setType] = useState<ProblemType>(initialType)
  const [search, setSearch] = useState('')
  const [selected, setSelected] = useState<number[]>([])
  const load = useCallback(() => {
    setError('')
    bridge.maintenanceReport(200, 0).then(setReport).catch((reason: Error) => setError(reason.message))
  }, [])
  useEffect(load, [load])
  useEffect(() => setType(initialType), [initialType])
  const problems = useMemo(() => (report?.issues ?? []).map(toProblem).filter((item) => {
    if (status === 'normal') return false
    if (status === 'abnormal' && item.status !== '异常') return false
    if (status === 'auto' && item.repairDisabled) return false
    if (status === 'manual' && !item.repairDisabled) return false
    if (type !== 'all' && item.type !== type) return false
    const text = `${item.code} ${item.title} ${item.detail} ${item.path ?? ''}`.toLowerCase()
    return !search.trim() || text.includes(search.trim().toLowerCase())
  }), [report, search, status, type])
  const activeFilterCount = (status === 'all' ? 0 : 1) + (type === 'all' ? 0 : 1) + (search.trim() ? 1 : 0)
  const selectedProblems = problems.filter((item) => item.movieId && selected.includes(item.movieId))
  const canBatchSync = selectedProblems.some((item) => !item.repairDisabled)
  const syncOne = (movieId?: number) => {
    if (!movieId) return
    bridge.syncMovie(movieId).then(result => setNotice(`${result.message} 可在任务中心查看。`)).catch((reason: Error) => setNotice(reason.message))
  }
  const syncSelected = () => {
    const ids = selectedProblems.filter((item) => !item.repairDisabled && item.movieId).map((item) => item.movieId!)
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
    <TextField size="small" placeholder="按番号或标题搜索" value={search} onChange={(event) => setSearch(event.target.value)} sx={{ flex: '1 1 260px' }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon fontSize="small"/></InputAdornment> } }}/>
    <Button variant="outlined" disabled={!canBatchSync} onClick={syncSelected}>批量同步缺失元数据</Button>
    <Button variant="outlined" disabled={!selected.length} onClick={() => setSelected([])}>取消选择</Button>
  </Stack>
  return <WorkspacePage title="诊断与修复" description="问题原因、路径和建议操作集中显示；耗时修复继续进入任务中心。" filters={filters} activeFilterCount={activeFilterCount} onClearFilters={() => { setStatus('all'); setType('all'); setSearch('') }} loading={!report && !error} error={error} primaryActions={[refreshAction(load, '重新诊断')]}>
    {status === 'normal' ? <EmptyState title="正常影片不在此列表展示" description="当前页面聚焦异常诊断与修复；概览中可查看正常数量。"/> : problems.length ? <Stack spacing={1.1}>
      {problems.map((item, index) => <Card key={`${item.movieId}-${item.type}-${index}`} variant="outlined" sx={{ borderRadius: 2 }}>
        <CardContent sx={{ p: 1.35, '&:last-child': { pb: 1.35 } }}>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'auto minmax(0,1fr)', md: '58px minmax(170px,.9fr) minmax(0,1.4fr) minmax(220px,1fr) auto' }, gap: 1.25, alignItems: 'center' }}>
            <Stack direction="row" spacing={.5} sx={{ alignItems: 'center' }}>
              {item.movieId && <Checkbox checked={selected.includes(item.movieId)} onChange={(event) => setSelected((current) => event.target.checked ? [...new Set([...current, item.movieId!])] : current.filter(id => id !== item.movieId))}/>}
              <ProblemPoster movieId={item.movieId} title={item.code || item.title}/>
            </Stack>
            <Stack sx={{ minWidth: 0 }}>
              <Typography sx={{ fontWeight: 900 }} noWrap>{item.code || item.title || '未命名影片'}</Typography>
              <Typography variant="body2" color="text.secondary" noWrap>{item.title || '无标题'}</Typography>
            </Stack>
            <Stack spacing={.5} sx={{ minWidth: 0 }}>
              <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap' }}>
                <MaintenanceStatusBadge severity={item.severity} label={problemTypeLabels[item.type]}/>
                <StatusBadge label={item.status} tone={item.repairDisabled ? 'warning' : 'info'}/>
              </Stack>
              <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>原因：{item.reason}</Typography>
              {item.path && <Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>路径：{item.path}</Typography>}
            </Stack>
            <Stack spacing={.35} sx={{ minWidth: 0 }}>
              <Typography variant="body2" sx={{ fontWeight: 800 }}>建议操作</Typography>
              <Typography variant="body2" color="text.secondary">{item.suggestion}</Typography>
            </Stack>
            <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap', justifyContent: 'flex-end' }}>
              {item.movieId && <Button size="small" variant="outlined" onClick={() => navigate(`/movies/${item.movieId}`)}>查看影片</Button>}
              <Button size="small" variant="contained" disabled={item.repairDisabled || !item.movieId} onClick={() => syncOne(item.movieId)}>单项修复</Button>
              <Button size="small" color="inherit" disabled>忽略此问题</Button>
            </Stack>
          </Box>
        </CardContent>
      </Card>)}
    </Stack> : <EmptyState title="没有匹配的问题" description="当前筛选条件下没有诊断项。"/>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </WorkspacePage>
}

function ProblemPoster({ movieId, title }: { movieId?: number; title: string }) {
  return <Box sx={{ width: 46, height: 62, borderRadius: 1.25, overflow: 'hidden', bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}>
    {movieId ? <SmartImage src={`${BRIDGE_ORIGIN}/api/images/${movieId}/primary?variant=thumbnail`} alt={title}/> : <BrokenImageRoundedIcon color="disabled"/>}
  </Box>
}

function toProblem(item: MaintenanceIssue) {
  const type = inferProblemType(item)
  const name = item.detail?.split(' · ')[0] || item.title
  const code = item.detail?.match(/[A-Z]{2,10}-\d{2,6}/i)?.[0] ?? name
  const legacy = Boolean(item.path?.match(/JVDIO|Jvedio|BigPic|SmallPic|ExtraPic/i) || item.detail?.match(/JVDIO|Jvedio|BigPic|SmallPic|ExtraPic|Legacy/i))
  const pathMissing = Boolean(item.path && /不存在|Missing|缺失/i.test(`${item.title} ${item.detail}`))
  const relationMissing = ['missing-actors', 'missing-directors', 'missing-tags', 'missing-series', 'missing-studios'].includes(type)
  const repairDisabled = type === 'image-path-invalid' || type === 'image-file-missing' || type === 'legacy-data' || type === 'relation-missing'
  return {
    movieId: item.movieId,
    code,
    title: name,
    severity: item.severity,
    type: legacy ? 'legacy-data' as ProblemType : pathMissing ? 'image-file-missing' as ProblemType : type,
    status: '异常',
    detail: item.detail,
    path: item.path,
    reason: legacy ? '历史迁移图片或旧路径不再作为运行时资源使用。' : pathMissing ? '数据库存在路径信息，但文件当前不可访问。' : relationMissing ? '数据库缺少对应关联记录。' : item.detail || item.title,
    suggestion: repairDisabled ? '暂不支持一键修复；建议重新同步或重新刮削后复查。' : repairSuggestion(type),
    repairDisabled,
  }
}

function inferProblemType(item: MaintenanceIssue): ProblemType {
  const text = `${item.category} ${item.title} ${item.detail}`.toLowerCase()
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

function repairSuggestion(type: ProblemType) {
  if (type === 'missing-nfo') return '重新生成 NFO 或同步缺失元数据。'
  if (type === 'missing-screenshot') return '提交生成截图任务。'
  if (type === 'missing-gif') return '提交生成 GIF 任务。'
  if (type.startsWith('missing-')) return '重新同步缺失元数据。'
  return '重新诊断后按问题类型处理。'
}

function normalizeTab(value: string | null): DataCenterTab {
  return value === 'diagnostics' || value === 'duplicates' ? value : 'overview'
}

function normalizeProblemType(value: string | null): ProblemType {
  return value && value in problemTypeLabels ? value as ProblemType : 'all'
}

function uniqueMissing(values: number[]) {
  return Math.max(...values, 0)
}
