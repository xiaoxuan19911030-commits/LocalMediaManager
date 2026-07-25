import DownloadRoundedIcon from '@mui/icons-material/DownloadRounded'
import ArticleRoundedIcon from '@mui/icons-material/ArticleRounded'
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded'
import PauseRoundedIcon from '@mui/icons-material/PauseRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import RestoreRoundedIcon from '@mui/icons-material/RestoreRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import {
  Alert, Box, Button, Checkbox, Chip, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle,
  Divider, FormControlLabel, LinearProgress, MenuItem, Paper, Snackbar, Stack, Table, TableBody, TableCell, TableContainer,
  TableHead, TablePagination, TableRow, TextField, Typography,
} from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { SurfaceSection } from '@/components/ProductComponents'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { MetadataCompletionItem, MetadataCompletionPreview, MetadataCompletionScanCommand } from '@/types/media'

const taskStorageKey = 'lmm.metadataCompletionTaskId.v1'
const fields: { key: keyof MetadataCompletionScanCommand; label: string }[] = [
  { key: 'actors', label: '演员' }, { key: 'genres', label: 'Provider 标签' }, { key: 'poster', label: 'Poster' },
  { key: 'fanart', label: 'Fanart' }, { key: 'nfo', label: 'NFO' }, { key: 'description', label: '简介' },
  { key: 'series', label: '系列' }, { key: 'director', label: '导演' }, { key: 'studio', label: '厂商' },
  { key: 'releaseDate', label: '发行日期' },
]
const defaultOptions: MetadataCompletionScanCommand = {
  actors: true, genres: true, poster: true, fanart: true, nfo: true, description: true,
  series: true, director: true, studio: true, releaseDate: true, concurrency: 4, maxMovies: 20,
}

export default function MetadataCompletionPage() {
  const [options, setOptions] = useState(defaultOptions)
  const [taskId, setTaskId] = useState(() => Number(window.localStorage.getItem(taskStorageKey)) || 0)
  const [preview, setPreview] = useState<MetadataCompletionPreview>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirm, setConfirm] = useState<'execute' | 'rollback'>()
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState('all')
  const [page, setPage] = useState(0)
  const [rowsPerPage, setRowsPerPage] = useState(25)
  const [selectedItem, setSelectedItem] = useState<MetadataCompletionItem>()

  const load = useCallback(async () => {
    if (!taskId) return
    try { setPreview(await bridge.metadataCompletion(taskId)); setError('') }
    catch (reason) { setError((reason as Error).message) }
  }, [taskId])

  useEffect(() => { void load() }, [load])
  useEffect(() => {
    if (!taskId || !preview || !['Pending', 'Running'].includes(preview.status)) return
    const timer = window.setInterval(() => { void load() }, 1200)
    return () => window.clearInterval(timer)
  }, [load, preview?.status, taskId])
  useEffect(() => setPage(0), [query, statusFilter])

  const run = async (action: () => Promise<{ message: string }>) => {
    setBusy(true); setError('')
    try { const result = await action(); setNotice(result.message); setConfirm(undefined); await load() }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const start = async () => {
    setBusy(true); setError('')
    try {
      const result = await bridge.startMetadataCompletionDryRun(options)
      window.localStorage.setItem(taskStorageKey, String(result.taskId))
      setTaskId(result.taskId); setPreview(undefined); setNotice(result.message)
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const exportReport = async () => {
    if (!preview?.items.length) return
    setBusy(true); setError('')
    try {
      const result = await bridge.exportMetadataCompletion(preview.taskId)
      setNotice(`${result.message} ${result.csvPath}`)
      await bridge.openDirectory(result.csvPath.replace(/[\\/][^\\/]+$/, ''))
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const filtered = useMemo(() => (preview?.items ?? []).filter(item => {
    if (statusFilter !== 'all' && item.status !== statusFilter) return false
    const haystack = `${item.number} ${item.movieId} ${item.videoPath} ${item.missingFields.join(' ')} ${item.reason}`.toLowerCase()
    return !query.trim() || haystack.includes(query.trim().toLowerCase())
  }), [preview?.items, query, statusFilter])
  const visible = filtered.slice(page * rowsPerPage, page * rowsPerPage + rowsPerPage)

  return <WorkspacePage title="定向元数据补全" description="先分析缺失字段并生成 Dry Run，再按 Provider 能力精准补全；默认保护所有已有数据。"
    primaryActions={[
      { key: 'dry-run', label: '生成 Dry Run', icon: <SearchRoundedIcon/>, onClick: () => void start(), disabled: busy },
      { key: 'refresh', label: '刷新', icon: <RefreshRoundedIcon/>, variant: 'outlined', onClick: () => void load(), disabled: busy || !taskId },
    ]}>
    <Stack spacing={1.5}>
      {error && <Alert severity="error">{error}</Alert>}
      <Alert severity="info">P1 固定选择 20 部。Dry Run 不联网；确认后只执行这 20 部，Merge 始终只填空字段，完成后自动停止。</Alert>

      <SurfaceSection title="补全范围" description="只分析有效 Standard 影片。Local、未分配、番号冲突、低置信度和受保护字段自动排除。">
        <Stack spacing={1.25}>
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(145px,1fr))', gap: .5 }}>
            {fields.map(field => <FormControlLabel key={String(field.key)}
              control={<Checkbox checked={Boolean(options[field.key])} onChange={event => setOptions(value => ({ ...value, [field.key]: event.target.checked }))}/>} label={field.label}/>) }
          </Box>
          <TextField select size="small" label="并发数" value={options.concurrency}
            onChange={event => setOptions(value => ({ ...value, concurrency: Number(event.target.value) }))} sx={{ width: 160 }}>
            {[1, 2, 3, 4, 5, 6, 7, 8].map(value => <MenuItem key={value} value={value}>{value}</MenuItem>)}
          </TextField>
          <Typography variant="caption" color="text.secondary">P1 批次上限：20 部（固定，不可在本阶段提高）</Typography>
        </Stack>
      </SurfaceSection>

      {preview && <>
        <SurfaceSection title="会话状态" description={`任务 #${preview.taskId} · ${stageLabel(preview.stage)} · 创建于 ${formatTime(preview.createdAt)}`}
          action={<Stack direction="row" spacing={1}>
            {['Pending', 'Running'].includes(preview.status) && <Button size="small" variant="outlined" startIcon={<PauseRoundedIcon/>} disabled={busy} onClick={() => void run(() => bridge.pauseMetadataCompletion(preview.taskId))}>暂停</Button>}
            {preview.canResume && <Button size="small" variant="outlined" startIcon={<PlayArrowRoundedIcon/>} disabled={busy} onClick={() => void run(() => bridge.resumeMetadataCompletion(preview.taskId))}>继续</Button>}
            <StatusBadge label={statusLabel(preview.status)} tone={statusTone(preview.status)}/>
          </Stack>}>
          <Stack spacing={1}>
            <LinearProgress variant="determinate" value={preview.progress}/>
            <Typography variant="caption" color="text.secondary">{preview.progress.toFixed(1)}%</Typography>
          </Stack>
        </SurfaceSection>

        <SurfaceSection title="Dry Run 摘要" description="预计值是能力路由的乐观上限，不代表 Provider 一定返回数据。"
          action={<Stack direction="row" spacing={1}>
            <Button size="small" variant="outlined" startIcon={<DownloadRoundedIcon/>} disabled={busy || preview.items.length === 0} onClick={() => void exportReport()}>导出报告</Button>
            <Button size="small" variant="contained" startIcon={<FactCheckRoundedIcon/>} disabled={busy || !preview.canExecute} onClick={() => setConfirm('execute')}>执行补全</Button>
            <Button size="small" color="error" variant="outlined" startIcon={<RestoreRoundedIcon/>} disabled={busy || !preview.canRollback} onClick={() => setConfirm('rollback')}>回滚会话</Button>
          </Stack>}>
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(150px,1fr))', gap: 1 }}>
            <Metric label="扫描 Standard" value={preview.counts.scannedStandardMovies}/>
            <Metric label="不完整" value={preview.counts.incompleteMovies}/>
            <Metric label="合格池" value={preview.counts.eligibleMovies}/>
            <Metric label="P1 固定批次" value={preview.counts.plannedNetworkMovies} tone="warning"/>
            <Metric label="当前完整" value={preview.projection.completeBefore}/>
            <Metric label="乐观预计完整" value={preview.projection.completeProjected} tone="success"/>
            <Metric label="预计耗时" value={`${Math.ceil(preview.projection.estimatedSeconds / 60)} 分钟`}/>
            <Metric label="随机种子" value={preview.selection.seed}/>
          </Box>
          <Stack direction="row" useFlexGap spacing={.75} sx={{ flexWrap: 'wrap', mt: 1.25 }}>
            {Object.entries(preview.selection.balancedCoverage).map(([field, movieId]) => <Chip key={field} size="small" variant="outlined" label={`${fieldLabel(field)} · MovieId ${movieId}`}/>) }
          </Stack>
        </SurfaceSection>

        <SurfaceSection title="字段与 Provider 请求" description="显示首选 Provider 的预计请求；项目计划保留回退链，但字段已补齐后不会继续请求后续来源。">
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1.4fr 1fr' }, gap: 1.25 }}>
            <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(140px,1fr))', gap: 1 }}>
              {Object.entries(preview.counts.missingByField).map(([field, count]) => <Metric key={field} label={`缺 ${fieldLabel(field)}`} value={count}/>) }
            </Box>
            <Stack spacing={1}>{Object.entries(preview.counts.providerRequests).map(([provider, count]) =>
              <Box key={provider} sx={{ p: 1.15, border: 1, borderColor: 'divider', borderRadius: 1.5, display: 'flex', justifyContent: 'space-between' }}>
                <Typography sx={{ fontWeight: 800 }}>{provider}</Typography><Typography>计划 {count} · 实际 {preview.counts.actualProviderRequests[provider] ?? 0}</Typography>
              </Box>)}</Stack>
          </Box>
        </SurfaceSection>

        <SurfaceSection title="补全计划" description="每部影片显示缺失字段、受保护字段、Provider 路由、失败分类和实际写入结果。">
          <Stack spacing={1.25}>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
              <TextField size="small" placeholder="搜索番号、路径、字段或原因" value={query} onChange={event => setQuery(event.target.value)} sx={{ flex: 1 }}/>
              <TextField select size="small" label="状态" value={statusFilter} onChange={event => setStatusFilter(event.target.value)} sx={{ minWidth: 160 }}>
                <MenuItem value="all">全部状态</MenuItem>
                {['Pending', 'Completed', 'Partial', 'Skipped', 'NoResult', 'Failed', 'Conflict'].map(value => <MenuItem key={value} value={value}>{statusLabel(value)}</MenuItem>)}
              </TextField>
            </Stack>
            <TableContainer component={Paper} variant="outlined" sx={{ maxHeight: 600 }}>
              <Table stickyHeader size="small">
                <TableHead><TableRow><TableCell>影片</TableCell><TableCell>缺失字段</TableCell><TableCell>保护</TableCell><TableCell>Provider 路由</TableCell><TableCell>结果</TableCell><TableCell>AddedFields</TableCell><TableCell>状态</TableCell><TableCell>日志</TableCell></TableRow></TableHead>
                <TableBody>{visible.map(item => <CompletionRow key={item.itemId} item={item} onDetails={() => setSelectedItem(item)}/>)}</TableBody>
              </Table>
            </TableContainer>
            <TablePagination component="div" count={filtered.length} page={page} rowsPerPage={rowsPerPage}
              onPageChange={(_, next) => setPage(next)} onRowsPerPageChange={event => { setRowsPerPage(Number(event.target.value)); setPage(0) }}
              rowsPerPageOptions={[10, 25, 50, 100]} labelRowsPerPage="每页"/>
          </Stack>
        </SurfaceSection>

        <SurfaceSection title="安全与限制" description="执行前会再次验证媒体、番号、锁定状态和缺失字段。">
          <Stack spacing={.75}>{preview.warnings.map(warning => <Alert key={warning} severity="info">{warning}</Alert>)}</Stack>
        </SurfaceSection>
      </>}
    </Stack>

    <Dialog open={confirm === 'execute'} onClose={() => !busy && setConfirm(undefined)} maxWidth="sm" fullWidth>
      <DialogTitle>执行定向元数据补全</DialogTitle>
      <DialogContent><DialogContentText>
        将对 {preview?.counts.plannedNetworkMovies ?? 0} 部影片按字段调用计划中的 Provider。已有字段、用户图片、锁定 NFO、用户标签、评分、收藏和播放记录不会被覆盖。
      </DialogContentText></DialogContent>
      <DialogActions><Button onClick={() => setConfirm(undefined)}>取消</Button><Button variant="contained" disabled={busy || !preview} onClick={() => preview && void run(() => bridge.executeMetadataCompletion(preview.taskId, preview.confirmationToken))}>确认执行</Button></DialogActions>
    </Dialog>
    <Dialog open={confirm === 'rollback'} onClose={() => !busy && setConfirm(undefined)} maxWidth="sm" fullWidth>
      <DialogTitle>回滚本次补全</DialogTitle>
      <DialogContent><DialogContentText>只回滚本会话写入的数据库字段和关联。执行期间下载或生成的实体文件按安全规则保留，不会删除。</DialogContentText></DialogContent>
      <DialogActions><Button onClick={() => setConfirm(undefined)}>取消</Button><Button color="error" variant="contained" disabled={busy || !preview} onClick={() => preview && void run(() => bridge.rollbackMetadataCompletion(preview.taskId))}>确认回滚</Button></DialogActions>
    </Dialog>
    <Dialog open={Boolean(selectedItem)} onClose={() => setSelectedItem(undefined)} maxWidth="lg" fullWidth>
      <DialogTitle>{selectedItem?.number} · 补全详情</DialogTitle>
      <DialogContent dividers>
        {selectedItem && <Stack spacing={2}>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3,minmax(0,1fr))' }, gap: 1 }}>
            <Metric label="MovieId" value={selectedItem.movieId}/><Metric label="Provider 尝试" value={selectedItem.attempts}/><Metric label="耗时" value={`${selectedItem.elapsedMilliseconds} ms`}/>
          </Box>
          <SurfaceSection title="Provider 贡献" description="按 Provider 记录真实返回并进入目标字段集合的内容。">
            <Stack spacing={.75}>{Object.entries(selectedItem.providerContributions).map(([provider, values]) => <Box key={provider} sx={{ display: 'flex', gap: 1, alignItems: 'center' }}><Typography sx={{ minWidth: 90, fontWeight: 800 }}>{provider}</Typography><TokenList values={values.map(fieldLabel)} empty="无贡献"/></Box>)}</Stack>
          </SurfaceSection>
          <SurfaceSection title="Before / After" description="执行会话保存的数据库快照；已有值受 fill-empty-only 保护。">
            <Table size="small"><TableHead><TableRow><TableCell>字段</TableCell><TableCell>Before</TableCell><TableCell>After</TableCell></TableRow></TableHead><TableBody>
              {Array.from(new Set([...Object.keys(selectedItem.beforeValues), ...Object.keys(selectedItem.afterValues)])).map(field => <TableRow key={field}><TableCell>{fieldLabel(field)}</TableCell><TableCell sx={{ overflowWrap: 'anywhere' }}>{selectedItem.beforeValues[field] || '—'}</TableCell><TableCell sx={{ overflowWrap: 'anywhere' }}>{selectedItem.afterValues[field] || '—'}</TableCell></TableRow>)}
            </TableBody></Table>
          </SurfaceSection>
          <SurfaceSection title="执行日志" description="包含 Provider、HTTP 线索、重试、落盘和 Database Merge。">
            <Stack divider={<Divider flexItem/>}>{selectedItem.logs.length ? selectedItem.logs.map((log, index) => <Box key={`${log.at}-${index}`} sx={{ py: .75 }}>
              <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}><Typography variant="caption" sx={{ minWidth: 165 }}>{formatTime(log.at)}</Typography><Typography variant="caption" sx={{ minWidth: 140, fontWeight: 800 }}>{log.provider} / {log.stage}</Typography><Typography variant="caption">尝试 {log.attempt || '—'} · HTTP {log.httpStatusCode ?? '—'} · {log.elapsedMilliseconds} ms</Typography></Stack>
              <Typography variant="body2" sx={{ mt: .35, overflowWrap: 'anywhere' }}>{log.message}</Typography>
            </Box>) : <Typography variant="body2" color="text.secondary">Dry Run 尚未执行 Provider。</Typography>}</Stack>
          </SurfaceSection>
        </Stack>}
      </DialogContent>
      <DialogActions><Button onClick={() => setSelectedItem(undefined)}>关闭</Button></DialogActions>
    </Dialog>
    <Snackbar open={Boolean(notice)} autoHideDuration={5000} onClose={() => setNotice('')} message={notice}/>
  </WorkspacePage>
}

function CompletionRow({ item, onDetails }: { item: MetadataCompletionItem; onDetails: () => void }) {
  return <TableRow hover>
    <TableCell sx={{ minWidth: 220, maxWidth: 330 }}><Typography variant="body2" sx={{ fontWeight: 850 }}>{item.number}</Typography><Typography variant="caption" color="text.secondary">MovieId {item.movieId}</Typography><Typography variant="caption" sx={{ display: 'block', overflowWrap: 'anywhere' }}>{item.videoPath}</Typography></TableCell>
    <TableCell sx={{ minWidth: 180 }}><TokenList values={item.missingFields.map(fieldLabel)}/></TableCell>
    <TableCell><TokenList values={item.protectedFields.map(fieldLabel)} empty="无"/></TableCell>
    <TableCell sx={{ minWidth: 170 }}>{item.providerPlan.length ? item.providerPlan.join(' → ') : '不联网'}</TableCell>
    <TableCell sx={{ minWidth: 260 }}><Typography variant="body2">{item.reason}</Typography>{item.failureCategory && <Typography variant="caption" color="text.secondary">{failureLabel(item.failureCategory)}</Typography>}</TableCell>
    <TableCell><TokenList values={item.addedFields} empty="—"/></TableCell>
    <TableCell><StatusBadge label={statusLabel(item.status)} tone={statusTone(item.status)}/></TableCell>
    <TableCell><Button size="small" startIcon={<ArticleRoundedIcon/>} onClick={onDetails}>查看</Button></TableCell>
  </TableRow>
}

function TokenList({ values, empty = '—' }: { values: string[]; empty?: string }) {
  return values.length ? <Stack direction="row" useFlexGap spacing={.5} sx={{ flexWrap: 'wrap' }}>{values.map(value => <Chip key={value} size="small" variant="outlined" label={value}/>)}</Stack> : <Typography variant="caption" color="text.secondary">{empty}</Typography>
}
function Metric({ label, value, tone }: { label: string; value: number | string; tone?: 'success' | 'warning' }) {
  return <Box sx={{ p: 1.15, border: 1, borderColor: 'divider', borderRadius: 1.5 }}><Typography variant="h6" sx={{ fontWeight: 900, color: tone ? `${tone}.main` : 'text.primary' }}>{value}</Typography><Typography variant="caption" color="text.secondary">{label}</Typography></Box>
}
function fieldLabel(value: string) { return ({ Actors: '演员', Genres: 'Provider 标签', Poster: 'Poster', Fanart: 'Fanart', NFO: 'NFO', Description: '简介', Series: '系列', Director: '导演', Studio: '厂商', ReleaseDate: '发行日期', Code: '番号', Title: '标题' } as Record<string, string>)[value] ?? value }
function statusLabel(value: string) { return ({ Pending: '待执行', Running: '执行中', PreviewReady: '计划已生成', Completed: '已完成', CompletedWithErrors: '完成但有未补齐', Partial: '部分补全', Skipped: '已跳过', NoResult: '无结果', Failed: '失败', Conflict: '冲突', Paused: '已暂停' } as Record<string, string>)[value] ?? value }
function stageLabel(value: string) { return ({ Analyze: '等待分析', Analyzing: '分析缺失字段', PreviewReady: '等待确认', Execute: '等待执行', Executing: '按字段补全', Completed: '完成', CompletedWithErrors: '完成但有未补齐', RolledBack: '已回滚', Failed: '失败' } as Record<string, string>)[value] ?? value }
function failureLabel(value: string) { return ({ NoData: 'Provider 无数据', Network: '网络失败', Timeout: '超时', RateLimit: 'Provider 限速', CodeInvalid: '番号无法识别', CodeConflict: '番号冲突', MultipleNumbers: '多个番号候选', MergeSkipped: 'Merge 未写入', ExistingProtected: '已有字段保护', Locked: '人工锁定', ProviderEmpty: 'Provider 返回字段不完整', ProviderConflict: '多 Provider 冲突', MissingMedia: '媒体不可访问', ProviderDisabled: 'Provider 未启用' } as Record<string, string>)[value] ?? value }
function statusTone(value: string): 'success' | 'info' | 'warning' | 'error' | 'neutral' { return value === 'Completed' ? 'success' : value === 'PreviewReady' || value === 'Running' ? 'info' : value === 'Failed' ? 'error' : ['Partial', 'Skipped', 'NoResult', 'Conflict', 'Paused', 'CompletedWithErrors'].includes(value) ? 'warning' : 'neutral' }
function formatTime(value: string) { const date = new Date(value); return Number.isNaN(date.getTime()) ? value : date.toLocaleString() }
