import CancelRoundedIcon from '@mui/icons-material/CancelRounded'
import DownloadRoundedIcon from '@mui/icons-material/DownloadRounded'
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import RestoreRoundedIcon from '@mui/icons-material/RestoreRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import {
  Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle,
  FormControlLabel, LinearProgress, MenuItem, Paper, Snackbar, Stack, Table, TableBody, TableCell, TableContainer,
  TableHead, TablePagination, TableRow, TextField, Typography,
} from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { SurfaceSection } from '@/components/ProductComponents'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { MetadataRepairCandidate, MetadataRepairPreview, MetadataRepairProjection, MetadataRepairScanCommand } from '@/types/media'

const taskStorageKey = 'lmm.metadataRepairTaskId.v1'
const defaultOptions: MetadataRepairScanCommand = {
  poster: true,
  fanart: true,
  preview: true,
  screenshot: true,
  nfo: true,
  repairInvalidPaths: true,
  registerUnregistered: true,
}

export default function MetadataRepairPage() {
  const [options, setOptions] = useState(defaultOptions)
  const [taskId, setTaskId] = useState<number>(() => Number(window.localStorage.getItem(taskStorageKey)) || 0)
  const [preview, setPreview] = useState<MetadataRepairPreview>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirm, setConfirm] = useState<'execute' | 'rollback'>()
  const [statusFilter, setStatusFilter] = useState('all')
  const [typeFilter, setTypeFilter] = useState('all')
  const [query, setQuery] = useState('')
  const [page, setPage] = useState(0)
  const [rowsPerPage, setRowsPerPage] = useState(25)

  const load = useCallback(async () => {
    if (!taskId) return
    try {
      const value = await bridge.metadataRepair(taskId)
      setPreview(value)
      setError('')
    } catch (reason) {
      setError((reason as Error).message)
    }
  }, [taskId])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    if (!taskId || !preview || !['Pending', 'Running'].includes(preview.status)) return
    const timer = window.setInterval(() => { void load() }, 1200)
    return () => window.clearInterval(timer)
  }, [load, preview?.status, taskId])
  useEffect(() => setPage(0), [query, statusFilter, typeFilter])

  const start = async () => {
    setBusy(true); setError('')
    try {
      const result = await bridge.startMetadataRepairDryRun(options)
      window.localStorage.setItem(taskStorageKey, String(result.taskId))
      setTaskId(result.taskId)
      setPreview(undefined)
      setNotice(result.message)
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const cancel = async () => {
    if (!taskId) return
    setBusy(true)
    try { const result = await bridge.cancelMetadataRepair(taskId); setNotice(result.message); await load() }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const execute = async () => {
    if (!preview?.canExecute) return
    setBusy(true); setError('')
    try {
      const result = await bridge.executeMetadataRepair(preview.taskId, preview.confirmationToken)
      setNotice(result.message); setConfirm(undefined); await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const rollback = async () => {
    if (!preview?.canRollback) return
    setBusy(true); setError('')
    try {
      const result = await bridge.rollbackMetadataRepair(preview.taskId)
      setNotice(result.message); setConfirm(undefined); await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const exportReport = async () => {
    if (!preview?.items.length) return
    setBusy(true); setError('')
    try {
      const result = await bridge.exportMetadataRepair(preview.taskId)
      setNotice(`${result.message} ${result.csvPath}`)
      await bridge.openDirectory(result.csvPath.replace(/[\\/][^\\/]+$/, ''))
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const filtered = useMemo(() => (preview?.items ?? []).filter(item => {
    if (statusFilter !== 'all' && item.status !== statusFilter) return false
    if (typeFilter !== 'all' && item.resourceType !== typeFilter) return false
    const haystack = `${item.number} ${item.movieId} ${item.videoPath} ${item.currentDatabasePath ?? ''} ${item.candidatePath ?? ''} ${item.evidence} ${item.reason}`.toLowerCase()
    return !query.trim() || haystack.includes(query.trim().toLowerCase())
  }), [preview?.items, query, statusFilter, typeFilter])
  const visible = filtered.slice(page * rowsPerPage, page * rowsPerPage + rowsPerPage)
  const running = Boolean(preview && ['Pending', 'Running'].includes(preview.status))

  return <WorkspacePage
    title="元数据修复"
    description="只扫描 Standard 影片已有的图片与 NFO。必须先完成 Dry Run 并确认计划，才会修改数据库关联。"
    primaryActions={[
      { key: 'scan', label: running ? '扫描中' : '开始扫描', icon: <SearchRoundedIcon/>, variant: 'contained', disabled: busy || running, onClick: () => void start() },
      { key: 'refresh', label: '查看计划', icon: <RefreshRoundedIcon/>, disabled: !taskId || busy, onClick: () => void load() },
      { key: 'execute', label: '执行安全修复', icon: <PlayArrowRoundedIcon/>, disabled: !preview?.canExecute || busy, onClick: () => setConfirm('execute') },
    ]}
    secondaryActions={[
      { key: 'cancel', label: '取消', icon: <CancelRoundedIcon/>, color: 'warning', disabled: !running || busy, onClick: () => void cancel() },
      { key: 'export', label: '导出报告', icon: <DownloadRoundedIcon/>, disabled: !preview?.items.length || busy, onClick: () => void exportReport() },
      { key: 'rollback', label: '回滚本次修复', icon: <RestoreRoundedIcon/>, color: 'error', disabled: !preview?.canRollback || busy, onClick: () => setConfirm('rollback') },
    ]}>
    <Stack spacing={1.5}>
      {error && <Alert severity="error">{error}</Alert>}
      <SurfaceSection title="扫描范围" description="Local、未分配、失效媒体、低置信度番号和番号重识别不一致影片会自动排除。">
        <Stack spacing={1.25}>
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
            {(['poster', 'fanart', 'preview', 'screenshot', 'nfo'] as const).map(key => <FormControlLabel key={key}
              control={<Checkbox checked={options[key]} onChange={event => setOptions({ ...options, [key]: event.target.checked })}/>} label={resourceLabel(key)}/>) }
          </Stack>
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
            <FormControlLabel control={<Checkbox checked={options.repairInvalidPaths} onChange={event => setOptions({ ...options, repairInvalidPaths: event.target.checked })}/>} label="修正失效路径"/>
            <FormControlLabel control={<Checkbox checked={options.registerUnregistered} onChange={event => setOptions({ ...options, registerUnregistered: event.target.checked })}/>} label="登记未登记资源"/>
          </Stack>
          <Alert severity="info">扫描不会下载、生成、移动、复制、重命名或删除任何文件，也不会调用 Provider。</Alert>
        </Stack>
      </SurfaceSection>

      {preview && <>
        <SurfaceSection title="执行进度" description={`任务 #${preview.taskId} · ${stageLabel(preview.stage)}`}>
          <Stack spacing={1}>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
              <StatusBadge label={statusLabel(preview.status)} tone={statusTone(preview.status)}/>
              <Typography variant="body2" color="text.secondary">{preview.progress.toFixed(0)}%</Typography>
            </Stack>
            <LinearProgress variant="determinate" value={Math.max(0, Math.min(100, preview.progress))}/>
          </Stack>
        </SurfaceSection>

        {(preview.status === 'PreviewReady' || preview.items.length > 0 || preview.after) && <>
          <SurfaceSection title="Dry Run 汇总" description="预计值只计算高置信度安全项目；冲突和低置信度项目不计入收益。">
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(145px,1fr))', gap: 1 }}>
              <Metric label="Standard 扫描" value={preview.counts.scannedStandardMovies}/>
              <Metric label="符合安全范围" value={preview.counts.eligibleMovies}/>
              <Metric label="排除：媒体缺失" value={preview.counts.excludedMissingMedia}/>
              <Metric label="排除：番号低置信度" value={preview.counts.excludedLowConfidence}/>
              <Metric label="排除：番号不一致" value={preview.counts.excludedCodeMismatch}/>
              <Metric label="未登记 Poster" value={preview.counts.unregisteredPoster}/>
              <Metric label="未登记 Fanart" value={preview.counts.unregisteredFanart}/>
              <Metric label="未登记 Preview" value={preview.counts.unregisteredPreview}/>
              <Metric label="未登记 Screenshot" value={preview.counts.unregisteredScreenshot}/>
              <Metric label="未登记 NFO" value={preview.counts.unregisteredNfo}/>
              <Metric label="可安全修复" value={preview.counts.safeRepairs} tone="success"/>
              <Metric label="多候选冲突" value={preview.counts.conflicts} tone="warning"/>
              <Metric label="低置信度" value={preview.counts.lowConfidence} tone="warning"/>
              <Metric label="有效记录跳过" value={preview.counts.existingValidSkipped}/>
              <Metric label="记录异常" value={preview.counts.invalidDatabaseRecords} tone="error"/>
              <Metric label="实体缺失" value={preview.counts.missingPhysicalFiles} tone="error"/>
              <Metric label="无法匹配" value={preview.counts.unmatchedResources}/>
              {!preview.dryRun && <Metric label="已应用" value={preview.counts.applied} tone="success"/>}
              {!preview.dryRun && <Metric label="安全跳过" value={preview.counts.skipped}/>}
              {!preview.dryRun && <Metric label="失败" value={preview.counts.failed} tone="error"/>}
            </Box>
          </SurfaceSection>

          <SurfaceSection title={preview.after ? '修复前后统计' : '预计覆盖变化'} description="分母沿用 MetadataHealthSummary 的 Standard 口径。">
            <Projection before={preview.before} projected={preview.after ?? preview.projected}/>
          </SurfaceSection>

          <SurfaceSection title="修复计划" description="每项均显示当前路径、候选实体、匹配证据和置信度；安全执行不会覆盖有效资源。">
            <Stack spacing={1.25}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
                <TextField size="small" placeholder="搜索番号、路径或证据" value={query} onChange={event => setQuery(event.target.value)} sx={{ flex: 1 }}/>
                <TextField select size="small" label="资源类型" value={typeFilter} onChange={event => setTypeFilter(event.target.value)} sx={{ minWidth: 150 }}>
                  <MenuItem value="all">全部资源</MenuItem>
                  {['Poster', 'Fanart', 'Preview', 'Screenshot', 'NFO'].map(type => <MenuItem key={type} value={type}>{type}</MenuItem>)}
                </TextField>
                <TextField select size="small" label="计划状态" value={statusFilter} onChange={event => setStatusFilter(event.target.value)} sx={{ minWidth: 150 }}>
                  <MenuItem value="all">全部状态</MenuItem>
                  <MenuItem value="Safe">安全修复</MenuItem>
                  <MenuItem value="Applied">已应用</MenuItem>
                  <MenuItem value="Conflict">多候选冲突</MenuItem>
                  <MenuItem value="LowConfidence">低置信度</MenuItem>
                  <MenuItem value="Skipped">安全跳过</MenuItem>
                </TextField>
              </Stack>
              <TableContainer component={Paper} variant="outlined" sx={{ maxHeight: 560 }}>
                <Table stickyHeader size="small">
                  <TableHead><TableRow>
                    <TableCell>影片</TableCell><TableCell>资源</TableCell><TableCell>当前数据库路径</TableCell>
                    <TableCell>候选实体路径</TableCell><TableCell>匹配依据</TableCell><TableCell align="right">置信度</TableCell><TableCell>动作</TableCell><TableCell>覆盖</TableCell><TableCell>状态</TableCell>
                  </TableRow></TableHead>
                  <TableBody>{visible.map(item => <CandidateRow key={item.itemId} item={item}/>)}</TableBody>
                </Table>
              </TableContainer>
              <TablePagination component="div" count={filtered.length} page={page} rowsPerPage={rowsPerPage}
                onPageChange={(_, next) => setPage(next)} onRowsPerPageChange={event => { setRowsPerPage(Number(event.target.value)); setPage(0) }}
                rowsPerPageOptions={[10, 25, 50, 100]} labelRowsPerPage="每页"/>
            </Stack>
          </SurfaceSection>

          <SurfaceSection title="安全说明" description="执行前会再次验证影片范围、候选文件指纹和现有资源状态。">
            <Stack spacing={.75}>{preview.warnings.map(warning => <Alert key={warning} severity="info">{warning}</Alert>)}</Stack>
          </SurfaceSection>
        </>}
      </>}
    </Stack>

    <Dialog open={confirm === 'execute'} onClose={() => !busy && setConfirm(undefined)} maxWidth="sm" fullWidth>
      <DialogTitle>执行安全修复</DialogTitle>
      <DialogContent><DialogContentText>
        将按当前 Dry Run 计划写入 {preview?.counts.safeRepairs ?? 0} 个安全项目。冲突和低置信度项目不会执行；所有写入位于一个事务，并生成可回滚审计记录。
      </DialogContentText></DialogContent>
      <DialogActions><Button onClick={() => setConfirm(undefined)}>取消</Button><Button variant="contained" startIcon={<FactCheckRoundedIcon/>} disabled={busy} onClick={() => void execute()}>确认执行</Button></DialogActions>
    </Dialog>
    <Dialog open={confirm === 'rollback'} onClose={() => !busy && setConfirm(undefined)} maxWidth="sm" fullWidth>
      <DialogTitle>回滚本次修复</DialogTitle>
      <DialogContent><DialogContentText>只恢复本 Repair Session 修改的数据库关联，不移动、删除或改写磁盘文件。若关联在修复后又被用户修改，回滚会拒绝覆盖。</DialogContentText></DialogContent>
      <DialogActions><Button onClick={() => setConfirm(undefined)}>取消</Button><Button color="error" variant="contained" startIcon={<RestoreRoundedIcon/>} disabled={busy} onClick={() => void rollback()}>确认回滚</Button></DialogActions>
    </Dialog>
    <Snackbar open={Boolean(notice)} autoHideDuration={5000} onClose={() => setNotice('')} message={notice}/>
  </WorkspacePage>
}

function CandidateRow({ item }: { item: MetadataRepairCandidate }) {
  return <TableRow hover>
    <TableCell sx={{ minWidth: 220, maxWidth: 340 }}><Typography variant="body2" sx={{ fontWeight: 800 }}>{item.number}</Typography><Typography variant="caption" color="text.secondary">MovieId {item.movieId}</Typography><Typography variant="caption" sx={{ display: 'block', overflowWrap: 'anywhere' }}>{item.videoPath}</Typography></TableCell>
    <TableCell>{item.resourceType}</TableCell>
    <PathCell value={item.currentDatabasePath}/><PathCell value={item.candidatePath}/>
    <TableCell sx={{ minWidth: 210 }}><Typography variant="body2">{item.evidence}</Typography><Typography variant="caption" color="text.secondary">{item.reason}</Typography></TableCell>
    <TableCell align="right">{item.confidence}</TableCell>
    <TableCell><Chip size="small" variant="outlined" label={actionLabel(item.action)}/></TableCell>
    <TableCell>{item.willOverwrite ? '是' : '否'}</TableCell>
    <TableCell><StatusBadge label={repairStatusLabel(item.status)} tone={item.status === 'Safe' || item.status === 'Applied' ? 'success' : item.status === 'Skipped' ? 'neutral' : 'warning'}/></TableCell>
  </TableRow>
}

function PathCell({ value }: { value?: string }) {
  return <TableCell sx={{ minWidth: 230, maxWidth: 360 }}><Typography variant="caption" sx={{ overflowWrap: 'anywhere' }}>{value || '—'}</Typography></TableCell>
}

function Metric({ label, value, tone }: { label: string; value: number; tone?: 'success' | 'warning' | 'error' }) {
  return <Box sx={{ p: 1.15, border: 1, borderColor: 'divider', borderRadius: 1.5 }}>
    <Typography variant="h6" sx={{ fontWeight: 900, color: tone ? `${tone}.main` : 'text.primary' }}>{value}</Typography>
    <Typography variant="caption" color="text.secondary">{label}</Typography>
  </Box>
}

function Projection({ before, projected }: { before: MetadataRepairProjection; projected: MetadataRepairProjection }) {
  const rows: [string, keyof MetadataRepairProjection][] = [
    ['完整影片', 'completeMovies'], ['Poster', 'posterMovies'], ['Fanart', 'fanartMovies'], ['Preview', 'previewMovies'],
    ['Screenshot', 'screenshotMovies'], ['NFO', 'nfoMovies'], ['异常资源', 'invalidResourceRecords'], ['未登记资源', 'unregisteredResources'],
  ]
  return <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(170px,1fr))', gap: 1 }}>
    {rows.map(([label, key]) => <Box key={key} sx={{ p: 1.15, border: 1, borderColor: 'divider', borderRadius: 1.5 }}>
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'baseline' }}><Typography sx={{ fontWeight: 850 }}>{before[key]}</Typography><Typography color="text.secondary">→</Typography><Typography sx={{ fontWeight: 900, color: projected[key] === before[key] ? 'text.primary' : 'success.main' }}>{projected[key]}</Typography></Stack>
    </Box>)}
  </Box>
}

function resourceLabel(key: keyof Pick<MetadataRepairScanCommand, 'poster' | 'fanart' | 'preview' | 'screenshot' | 'nfo'>) {
  return ({ poster: 'Poster', fanart: 'Fanart', preview: 'Preview', screenshot: 'Screenshot', nfo: 'NFO' })[key]
}
function statusLabel(value: string) { return ({ Pending: '等待扫描', Running: '执行中', PreviewReady: '计划已生成', Completed: '已完成', CompletedWithErrors: '完成但统计刷新异常', Failed: '失败', Cancelled: '已取消' } as Record<string, string>)[value] ?? value }
function stageLabel(value: string) { return ({ Scan: '等待扫描', AnalyzingCurrentHealth: '读取当前状态', IndexingStorage: '建立目录缓存', Scanning: '扫描资源', PreviewReady: '等待确认', Apply: '等待执行', Applying: '写入事务', Recalculating: '重新统计', HealthRefreshFailed: '统计刷新失败', Completed: '完成', RolledBack: '已回滚', Failed: '失败', Cancelled: '取消' } as Record<string, string>)[value] ?? value }
function statusTone(value: string): 'success' | 'info' | 'warning' | 'error' | 'neutral' { return value === 'Completed' ? 'success' : value === 'CompletedWithErrors' ? 'warning' : value === 'Failed' ? 'error' : value === 'Cancelled' ? 'warning' : value === 'PreviewReady' ? 'info' : 'neutral' }
function repairStatusLabel(value: string) { return ({ Safe: '安全修复', Applied: '已应用', Conflict: '多候选冲突', LowConfidence: '低置信度', Skipped: '安全跳过' } as Record<string, string>)[value] ?? value }
function actionLabel(value: string) { return ({ RegisterImage: '登记图片', RepairImagePath: '修正图片路径', MarkMissing: '标记实体缺失', LinkNfo: '关联 NFO', RepairNfoPath: '修正 NFO 路径', ManualReview: '人工核对' } as Record<string, string>)[value] ?? value }
