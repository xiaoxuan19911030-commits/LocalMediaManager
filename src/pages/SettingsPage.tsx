import BackupRoundedIcon from '@mui/icons-material/BackupRounded'
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import RestoreRoundedIcon from '@mui/icons-material/RestoreRounded'
import SettingsBackupRestoreRoundedIcon from '@mui/icons-material/SettingsBackupRestoreRounded'
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded'
import { Alert, Box, Button, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, FormControlLabel, List, ListItemButton, ListItemText, MenuItem, Paper, Snackbar, Stack, Switch, TextField, Typography } from '@mui/material'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { FormEvent, useCallback, useEffect, useMemo, useState } from 'react'
import { useBlocker, useNavigate } from 'react-router'
import { BrandMark } from '@/components/BrandMark'
import { HealthMeter, SurfaceSection } from '@/components/ProductComponents'
import { DangerConfirmDialog } from '@/components/workspace/DangerConfirmDialog'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { buildInfo } from '@/buildInfo'
import { bridge } from '@/services/bridge'
import { useColorMode } from '@/themes/ThemeContext'
import type { BridgeHealth } from '@/types/media'
import type { BackupValidation, DataSafetyOverview, DiagnosticCheck, MetaTubeSettings, NfoSettings, PlaybackSettings, RatingRetentionSettings, SettingsImportPreview, SettingsSnapshot, SystemDiagnostic, UnifiedSettings } from '@/types/settings'
import type { ImageCachePreview } from '@/types/media'

const categories = [
  ['general', '常规'], ['library', '媒体库'], ['scan', '扫描与导入'], ['metadata', '元数据与同步'],
  ['images', '图片与缓存'], ['playback', '播放器'], ['search', '搜索与筛选'], ['shortcuts', '快捷键'],
  ['appearance', '外观'], ['data', '数据与备份'], ['logs', '日志与诊断'], ['about', '关于'],
] as const

type Category = (typeof categories)[number][0]
type LeaveAction = 'save' | 'discard'
const planned = ['计划支持']
const size = (bytes?: number) => bytes ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : '0 MB'
const stable = (value: unknown) => JSON.stringify(value)
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T

export default function SettingsPage() {
  const navigate = useNavigate()
  const { mode, setMode } = useColorMode()
  const [category, setCategory] = useState<Category>('general')
  const [snapshot, setSnapshot] = useState<SettingsSnapshot>()
  const [health, setHealth] = useState<BridgeHealth>()
  const [overview, setOverview] = useState<DataSafetyOverview>()
  const [original, setOriginal] = useState<UnifiedSettings>()
  const [draft, setDraft] = useState<UnifiedSettings>()
  const [defaults, setDefaults] = useState<UnifiedSettings>()
  const [diagnostics, setDiagnostics] = useState<SystemDiagnostic>()
  const [cachePreview, setCachePreview] = useState<ImageCachePreview>()
  const [backupPath, setBackupPath] = useState('')
  const [restoreMode, setRestoreMode] = useState('all')
  const [backupValidation, setBackupValidation] = useState<BackupValidation>()
  const [importJson, setImportJson] = useState('')
  const [importPreview, setImportPreview] = useState<SettingsImportPreview>()
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirm, setConfirm] = useState<'backup' | 'cache' | 'restore' | 'thumbs' | 'logs'>()
  const [restoreDefaultsOpen, setRestoreDefaultsOpen] = useState(false)
  const [leavePromptOpen, setLeavePromptOpen] = useState(false)
  const [pendingWindowClose, setPendingWindowClose] = useState(false)

  const hasUnsavedChanges = Boolean(original && draft && stable(original) !== stable(draft))
  const blocker = useBlocker(hasUnsavedChanges)

  const load = useCallback(() => {
    setError('')
    return Promise.all([bridge.settings(), bridge.dataSafetyOverview(), bridge.allSettings(), bridge.defaultSettings(), bridge.health()])
      .then(([settings, data, all, defaultValues, healthValue]) => {
        setSnapshot(settings); setOverview(data); setOriginal(clone(all)); setDraft(clone(all)); setDefaults(defaultValues); setHealth(healthValue)
        setMode(all.appearance.themeMode)
      })
      .catch((reason: Error) => setError(reason.message))
  }, [setMode])
  useEffect(() => { void load() }, [load])

  useEffect(() => {
    if (blocker.state === 'blocked') setLeavePromptOpen(true)
  }, [blocker.state])

  useEffect(() => {
    const handler = (event: BeforeUnloadEvent) => {
      if (!hasUnsavedChanges) return
      event.preventDefault()
      event.returnValue = ''
    }
    window.addEventListener('beforeunload', handler)
    return () => window.removeEventListener('beforeunload', handler)
  }, [hasUnsavedChanges])

  useEffect(() => {
    let unlisten: (() => void) | undefined
    getCurrentWindow().onCloseRequested(async (event) => {
      if (!hasUnsavedChanges) return
      event.preventDefault()
      setPendingWindowClose(true)
      setLeavePromptOpen(true)
    }).then((dispose) => { unlisten = dispose }).catch(() => undefined)
    return () => { unlisten?.() }
  }, [hasUnsavedChanges])

  const currentTitle = categories.find(([key]) => key === category)?.[1] ?? '设置'
  const legacyFields = useMemo(() => snapshot?.fields.filter(field => {
    if (category === 'scan') return field.category === 'library' && field.section.includes('Scan')
    if (category === 'search') return false
    if (category === 'images') return field.category === 'metadata' && (field.section.includes('缓存') || field.section.includes('图片'))
    if (category === 'data') return field.category === 'library' && field.section.includes('数据')
    if (category === 'logs') return field.category === 'advanced' && field.section.includes('日志')
    return field.category === category
  }) ?? [], [snapshot, category])

  const updateDraft = <K extends keyof UnifiedSettings>(key: K, value: UnifiedSettings[K]) => {
    setDraft(current => current ? { ...current, [key]: value } : current)
    if (key === 'appearance') setMode((value as UnifiedSettings['appearance']).themeMode)
  }

  const saveAll = async () => {
    if (!draft || !original) return false
    setBusy(true); setError('')
    try {
      const result = await bridge.saveAllSettings(draft)
      setOriginal(clone(result.settings))
      setDraft(clone(result.settings))
      setMode(result.settings.appearance.themeMode)
      setNotice('设置已保存')
      return true
    } catch (reason) {
      setError((reason as Error).message)
      const message = (reason as Error).message
      if (message.includes('MetaTube')) setCategory('metadata')
      else if (message.includes('NFO')) setCategory('metadata')
      else if (message.includes('播放器')) setCategory('playback')
      return false
    } finally {
      setBusy(false)
    }
  }

  const run = async (work: () => Promise<unknown>, message: string) => {
    setBusy(true); setError('')
    try { await work(); setNotice(message); await load() }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false); setConfirm(undefined) }
  }
  const testMetaTube = () => draft && run(async () => { const result = await bridge.testMetaTube(draft.metaTube); if (!result.success) throw new Error(result.message); setNotice(`${result.message}（${result.elapsedMilliseconds} ms）`) }, 'MetaTube 连接正常')
  const previewImport = () => {
    try { bridge.previewSettingsImport(JSON.parse(importJson)).then(setImportPreview).catch((reason: Error) => setError(reason.message)) }
    catch (reason) { setError((reason as Error).message) }
  }

  const restoreDefaultsToDraft = () => {
    if (!defaults) return
    setDraft(clone(defaults))
    setMode(defaults.appearance.themeMode)
    setRestoreDefaultsOpen(false)
    setNotice('默认值已应用到草稿，点击保存设置后生效')
  }

  const closeLeavePrompt = () => {
    setLeavePromptOpen(false)
    setPendingWindowClose(false)
    if (blocker.state === 'blocked') blocker.reset()
  }
  const finishLeave = async (action: LeaveAction) => {
    if (action === 'save' && !await saveAll()) return
    if (action === 'discard' && original) {
      setDraft(clone(original))
      setMode(original.appearance.themeMode)
    }
    setLeavePromptOpen(false)
    if (pendingWindowClose) {
      setPendingWindowClose(false)
      await getCurrentWindow().close()
      return
    }
    if (blocker.state === 'blocked') blocker.proceed()
  }

  if (!snapshot || !overview || !draft || !original || !defaults) {
    return <WorkspacePage title="Settings Center" description="设置与数据安全中心" error={error} loading={!error}/>
  }

  return <WorkspacePage title="Settings Center" description="统一管理设置、数据安全、备份恢复、缓存维护、日志诊断与任务相关入口。" error={error}
    primaryActions={[refreshAction(() => void load())]}>
    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '230px minmax(0,1fr)' }, gap: 2, pb: 10 }}>
      <Paper variant="outlined" sx={{ borderRadius: 3, p: 1, alignSelf: 'start', position: { lg: 'sticky' }, top: 16 }}>
        <List dense>{categories.map(([key, label]) => <ListItemButton key={key} selected={category === key} onClick={() => setCategory(key)} sx={{ borderRadius: 2 }}>
          <ListItemText primary={label}/>
        </ListItemButton>)}</List>
      </Paper>
      <Stack spacing={2}>
        <Paper variant="outlined" sx={{ p: 2, borderRadius: 3 }}>
          <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', justifyContent: 'space-between' }}>
            <Box><Typography variant="h5" sx={{ fontWeight: 900 }}>{currentTitle}</Typography><Typography color="text.secondary">设置会先进入草稿，点击底部“保存设置”后统一写入。</Typography></Box>
            {hasUnsavedChanges ? <StatusBadge tone="warning" label="有未保存更改"/> : <StatusBadge tone="success" label="已保存"/>}
          </Stack>
        </Paper>
        {category === 'general' && <GeneralSection snapshot={snapshot}/>}
        {category === 'library' && <LibrarySection onOpen={() => navigate('/libraries')}/>}
        {category === 'scan' && <PlannedSection labels={['自动读取 NFO', '自动读取本地图片', '忽略隐藏文件', '视频扩展名白名单']} fields={legacyFields}/>}
        {category === 'metadata' && <MetadataSection metaTube={draft.metaTube} setMetaTube={(value) => updateDraft('metaTube', value)} nfo={draft.nfo} setNfo={(value) => updateDraft('nfo', value)} busy={busy} testMetaTube={testMetaTube}/>}
        {category === 'images' && <ImagesSection cachePreview={cachePreview} setCachePreview={setCachePreview} onClean={() => setConfirm('cache')} onThumbs={() => setConfirm('thumbs')}/>}
        {category === 'playback' && <PlaybackSection playback={draft.playback} setPlayback={(value) => updateDraft('playback', value)}/>}
        {category === 'search' && <PlannedSection labels={['默认搜索范围', '默认排序', '默认卡片/列表模式', '保存页面筛选状态']} fields={legacyFields}/>}
        {category === 'shortcuts' && <ShortcutSection/>}
        {category === 'appearance' && <AppearanceSection mode={draft.appearance.themeMode} setMode={(value) => updateDraft('appearance', { themeMode: value })}/>}
        {category === 'data' && <DataSection overview={overview} ratingRetention={draft.ratingRetention} setRatingRetention={(value) => updateDraft('ratingRetention', value)} backupPath={backupPath} setBackupPath={setBackupPath} restoreMode={restoreMode} setRestoreMode={setRestoreMode} validation={backupValidation} setValidation={setBackupValidation} importJson={importJson} setImportJson={setImportJson} importPreview={importPreview} previewImport={previewImport} onBackup={() => setConfirm('backup')} onRestore={() => setConfirm('restore')}/>}
        {category === 'logs' && <LogsSection diagnostics={diagnostics} runDiagnostics={() => bridge.settingsDiagnostics().then(setDiagnostics).catch((reason: Error) => setError(reason.message))} onCleanLogs={() => setConfirm('logs')}/>}
        {category === 'about' && <AboutSection overview={overview} health={health}/>}
        {legacyFields.length > 0 && category !== 'metadata' && category !== 'playback' && category !== 'images' && <LegacyFields fields={legacyFields}/>}
      </Stack>
    </Box>
    <Paper elevation={4} sx={{ position: 'sticky', bottom: 0, zIndex: 2, mt: 2, mx: { xs: -2, md: -3 }, mb: { xs: -2, md: -3 }, px: { xs: 2, md: 3 }, py: 1.5, borderTop: 1, borderColor: 'divider', bgcolor: 'background.paper' }}>
      <Stack direction="row" spacing={1.5} sx={{ justifyContent: 'space-between', alignItems: 'center' }}>
        <Button variant="text" onClick={() => setRestoreDefaultsOpen(true)}>恢复默认</Button>
        <Button variant="contained" disabled={!hasUnsavedChanges || busy} onClick={() => void saveAll()}>{busy ? '正在保存...' : '保存设置'}</Button>
      </Stack>
    </Paper>
    <DangerConfirmDialog open={Boolean(confirm)} title="确认危险操作" description={confirmDescription(confirm)} warnings={confirmWarnings(confirm)} confirmText={confirm === 'restore' ? 'CONFIRM' : undefined} busy={busy} onClose={() => setConfirm(undefined)} onConfirm={() => {
      if (confirm === 'backup') void run(() => bridge.createBackup({ includeConfig: true, includeGeneratedCache: false }), '备份已创建')
      if (confirm === 'cache' && cachePreview) void run(() => bridge.cleanupImageCache(cachePreview.confirmationToken), '缓存已清理')
      if (confirm === 'thumbs') void run(() => bridge.rebuildImageCache(), '缩略图重建任务已创建')
      if (confirm === 'restore') void run(() => bridge.createRestorePlan(backupPath, restoreMode), '恢复计划已创建，请重启后按计划恢复')
      if (confirm === 'logs') void run(() => Promise.resolve(), '日志清理目前为计划支持，未执行删除')
    }}/>
    <Dialog open={restoreDefaultsOpen} onClose={() => setRestoreDefaultsOpen(false)}>
      <DialogTitle>恢复默认设置</DialogTitle>
      <DialogContent><DialogContentText>将当前设置恢复为默认值。修改将在点击“保存设置”后生效。</DialogContentText></DialogContent>
      <DialogActions><Button onClick={() => setRestoreDefaultsOpen(false)}>取消</Button><Button variant="contained" onClick={restoreDefaultsToDraft}>恢复默认值</Button></DialogActions>
    </Dialog>
    <Dialog open={leavePromptOpen} onClose={closeLeavePrompt}>
      <DialogTitle>设置尚未保存</DialogTitle>
      <DialogContent><DialogContentText>你有尚未保存的设置更改。</DialogContentText></DialogContent>
      <DialogActions>
        <Button onClick={() => void finishLeave('discard')}>放弃更改</Button>
        <Button onClick={closeLeavePrompt}>继续编辑</Button>
        <Button variant="contained" disabled={busy} onClick={() => void finishLeave('save')}>{busy ? '正在保存...' : '保存并离开'}</Button>
      </DialogActions>
    </Dialog>
    <Snackbar open={Boolean(notice)} autoHideDuration={2500} onClose={() => setNotice('')} message={notice}/>
  </WorkspacePage>
}

function GeneralSection({ snapshot }: { snapshot: SettingsSnapshot }) {
  return <SurfaceSection title="常规状态" description="统一设置服务读取结果。"><Stack spacing={1}><StatusBadge tone="success" label={`${snapshot.mappedCount} 个已映射设置`}/><StatusBadge tone={snapshot.errors.length ? 'warning' : 'success'} label={`${snapshot.errors.length} 个读取问题`}/><Typography variant="body2" color="text.secondary">读取时间：{new Date(snapshot.readAt).toLocaleString()}</Typography></Stack></SurfaceSection>
}
function LibrarySection({ onOpen }: { onOpen: () => void }) {
  return <SurfaceSection title="媒体库设置" description="媒体库 CRUD 复用现有安全页面；删除只删除配置，不删除磁盘文件。"><Button variant="contained" startIcon={<FolderRoundedIcon/>} onClick={onOpen}>打开媒体库管理</Button></SurfaceSection>
}
function MetadataSection({ metaTube, setMetaTube, nfo, setNfo, testMetaTube, busy }: { metaTube: MetaTubeSettings; setMetaTube: (v: MetaTubeSettings) => void; nfo: NfoSettings; setNfo: (v: NfoSettings) => void; testMetaTube: () => void; busy: boolean }) {
  return <Stack spacing={2}><SurfaceSection title="MetaTube Provider" description="现有 MetaTube 同步设置，非破坏性写入。"><Stack spacing={1.5}><FormControlLabel control={<Switch checked={metaTube.enabled} onChange={event => setMetaTube({ ...metaTube, enabled: event.target.checked })}/>} label="启用 MetaTube"/><TextField label="服务地址" size="small" value={metaTube.baseUrl} onChange={event => setMetaTube({ ...metaTube, baseUrl: event.target.value })}/><TextField type="number" label="请求超时（秒）" size="small" value={metaTube.timeoutSeconds} onChange={event => setMetaTube({ ...metaTube, timeoutSeconds: Number(event.target.value) || 30 })}/><Button disabled={busy} variant="outlined" onClick={testMetaTube}>测试连接</Button></Stack></SurfaceSection><SurfaceSection title="NFO" description="导入只补空字段；导出遵守锁定与冲突策略。"><Stack spacing={1.5}><TextField size="small" label="NFO 输出目录" value={nfo.outputDirectory} onChange={event => setNfo({ ...nfo, outputDirectory: event.target.value })}/><FormControlLabel control={<Switch checked={nfo.exportPolicy === 'SeparateFile'} onChange={event => setNfo({ ...nfo, exportPolicy: event.target.checked ? 'SeparateFile' : 'SkipExisting' })}/>} label="冲突时另存为 .lmm.nfo"/><FormControlLabel control={<Switch checked={nfo.includeImages} onChange={event => setNfo({ ...nfo, includeImages: event.target.checked })}/>} label="导出图片引用"/></Stack></SurfaceSection></Stack>
}
function ImagesSection({ cachePreview, setCachePreview, onClean, onThumbs }: { cachePreview?: ImageCachePreview; setCachePreview: (v: ImageCachePreview) => void; onClean: () => void; onThumbs: () => void }) {
  return <SurfaceSection title="图片与缓存" description="清理只影响 .lmm-cache 中可重建缩略图，不删除源图。"><Stack spacing={1.5}>{cachePreview && <Alert severity="warning">预计可清理 {cachePreview.entries} 条，{size(cachePreview.bytes)}，缺失记录 {cachePreview.missingEntries} 条。</Alert>}<Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}><Button variant="outlined" onClick={() => bridge.imageCachePreview().then(setCachePreview)}>检查缓存</Button><Button color="error" variant="outlined" disabled={!cachePreview} onClick={onClean}>清理缓存</Button><Button variant="outlined" onClick={onThumbs}>重建 Thumbnail</Button></Stack></Stack></SurfaceSection>
}
function PlaybackSection({ playback, setPlayback }: { playback: PlaybackSettings; setPlayback: (v: PlaybackSettings) => void }) {
  return <SurfaceSection title="播放器" description="复用现有播放服务，不新增播放器内核。"><Stack spacing={1.5}><FormControlLabel control={<Switch checked={playback.useSystemDefault} onChange={event => setPlayback({ ...playback, useSystemDefault: event.target.checked })}/>} label="使用系统默认播放器"/><TextField disabled={playback.useSystemDefault} size="small" label="外部播放器路径" value={playback.playerPath} onChange={event => setPlayback({ ...playback, playerPath: event.target.value })}/></Stack></SurfaceSection>
}
function PlannedSection({ labels, fields }: { labels: string[]; fields: unknown[] }) {
  return <SurfaceSection title="计划与兼容设置" description="可读取的旧配置会显示在下方；暂无后端能力的选项只标记计划支持。"><Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>{labels.map(label => <Chip key={label} label={label} variant="outlined"/>)}<StatusBadge tone={fields.length ? 'info' : 'neutral'} label={`${fields.length} 个兼容字段`}/></Stack></SurfaceSection>
}
function ShortcutSection() {
  const keys = ['上一页', '下一页', '聚焦搜索', '播放', '打开详情', '收藏', '同步信息', '切换视图', '返回', '随机影片']
  return <SurfaceSection title="快捷键" description="应用内快捷键管理；冲突检测和保存为计划支持。"><Stack spacing={1}>{keys.map(key => <Card key={key} variant="outlined"><CardContent sx={{ py: 1, '&:last-child': { pb: 1 } }}><Stack direction="row" sx={{ justifyContent: 'space-between' }}><Typography>{key}</Typography><StatusBadge label={planned[0]} tone="neutral"/></Stack></CardContent></Card>)}</Stack></SurfaceSection>
}
function AppearanceSection({ mode, setMode }: { mode: 'light' | 'dark'; setMode: (value: 'light' | 'dark') => void }) {
  return <SurfaceSection title="外观" description="主题预览立即生效，点击保存设置后持久化。"><Stack direction="row" spacing={1}><Button variant={mode === 'light' ? 'contained' : 'outlined'} startIcon={<LightModeRoundedIcon/>} onClick={() => setMode('light')}>浅色</Button><Button variant={mode === 'dark' ? 'contained' : 'outlined'} startIcon={<DarkModeRoundedIcon/>} onClick={() => setMode('dark')}>深色</Button></Stack></SurfaceSection>
}
function DataSection({ overview, ratingRetention, setRatingRetention, backupPath, setBackupPath, restoreMode, setRestoreMode, validation, setValidation, importJson, setImportJson, importPreview, previewImport, onBackup, onRestore }: { overview: DataSafetyOverview; ratingRetention: RatingRetentionSettings; setRatingRetention: (v: RatingRetentionSettings) => void; backupPath: string; setBackupPath: (v: string) => void; restoreMode: string; setRestoreMode: (v: string) => void; validation?: BackupValidation; setValidation: (v: BackupValidation) => void; importJson: string; setImportJson: (v: string) => void; importPreview?: SettingsImportPreview; previewImport: () => void; onBackup: () => void; onRestore: () => void }) {
  return <Stack spacing={2}><SurfaceSection title="数据位置" description="不会把原始影片和原始图片打包进备份。"><Stack spacing={1}><Typography>数据库：{overview.databasePath}</Typography><Typography>数据库大小：{size(overview.databaseBytes)}</Typography><Typography>配置库：{overview.configDatabasePath}</Typography><Typography>备份目录：{overview.backupDirectory}</Typography><Typography>缓存目录：{overview.cacheDirectory}</Typography><Typography>日志目录：{overview.logDirectory}</Typography><Typography>最近备份：{overview.lastBackupAt ? new Date(overview.lastBackupAt).toLocaleString() : '暂无'}</Typography></Stack></SurfaceSection><SurfaceSection title="评分保留" description="删除已评分影片时保存番号与评分；以后重新导入相同番号时自动恢复。"><FormControlLabel control={<Switch checked={ratingRetention.enabled} onChange={event => setRatingRetention({ enabled: event.target.checked })}/>} label="自动保留并恢复已删除影片评分"/></SurfaceSection><SurfaceSection title="备份与恢复计划" description="恢复采用计划文件，避免运行中热替换数据库。"><Stack spacing={1.5}><Button variant="contained" startIcon={<BackupRoundedIcon/>} onClick={onBackup}>创建手动备份</Button><TextField size="small" label="备份路径" value={backupPath} onChange={event => setBackupPath(event.target.value)}/><Stack direction="row" spacing={1}><Button variant="outlined" onClick={() => bridge.validateBackup(backupPath).then(setValidation)}>校验备份</Button><TextField select size="small" label="恢复模式" value={restoreMode} onChange={event => setRestoreMode(event.target.value)} sx={{ width: 150 }}><MenuItem value="all">全部</MenuItem><MenuItem value="database">仅数据库</MenuItem><MenuItem value="settings">仅设置</MenuItem></TextField><Button color="error" variant="outlined" startIcon={<RestoreRoundedIcon/>} disabled={!validation?.valid} onClick={onRestore}>创建恢复计划</Button></Stack>{validation && <Alert severity={validation.valid ? 'success' : 'error'}>{validation.valid ? '备份校验通过' : validation.errors.join('；')}</Alert>}</Stack></SurfaceSection><SurfaceSection title="配置导入导出" description="导出会剔除敏感字段；导入先预览差异，不直接覆盖。"><Stack spacing={1.5}><Button startIcon={<SettingsBackupRestoreRoundedIcon/>} onClick={() => bridge.exportSettings().then(result => setImportJson(JSON.stringify(result, null, 2)))}>导出当前设置</Button><TextField multiline minRows={6} label="导入 JSON / 导出预览" value={importJson} onChange={event => setImportJson(event.target.value)}/><Button startIcon={<UploadFileRoundedIcon/>} variant="outlined" onClick={previewImport}>预览导入差异</Button>{importPreview && <Alert severity={importPreview.valid ? 'info' : 'warning'}>{importPreview.valid ? `可识别 ${importPreview.changes.length} 项：${importPreview.categories.join('、')}` : importPreview.warnings.join('；')}</Alert>}</Stack></SurfaceSection></Stack>
}
function LogsSection({ diagnostics, runDiagnostics, onCleanLogs }: { diagnostics?: SystemDiagnostic; runDiagnostics: () => void; onCleanLogs: () => void }) {
  return <SurfaceSection title="日志与诊断" description="诊断包不包含影片、原始图片、Cookie、Token 或完整个人路径列表。"><Stack spacing={1.5}><Stack direction="row" spacing={1}><Button startIcon={<RefreshRoundedIcon/>} variant="contained" onClick={runDiagnostics}>执行基础诊断</Button><Button color="error" variant="outlined" onClick={onCleanLogs}>清理旧日志</Button></Stack>{diagnostics?.checks.map(check => <DiagnosticRow key={check.key} check={check}/>)}</Stack></SurfaceSection>
}
function DiagnosticRow({ check }: { check: DiagnosticCheck }) {
  return <Card variant="outlined"><CardContent sx={{ py: 1, '&:last-child': { pb: 1 } }}><Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}><StatusBadge tone={check.status === 'success' ? 'success' : check.status === 'error' ? 'error' : 'warning'} label={check.label}/><Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{check.detail}</Typography></Stack></CardContent></Card>
}
function AboutSection({ overview, health }: { overview: DataSafetyOverview; health?: BridgeHealth }) {
  return <SurfaceSection title="关于" description="本地优先、可维护的现代媒体管理工具。"><Stack spacing={1}><BrandMark/><Typography>Version: {buildInfo.version}</Typography><Typography>Commit: {buildInfo.commit}</Typography><Typography>Build: {buildInfo.buildTime}</Typography><Typography>Bridge: {health?.version ?? 'unknown'} · {health?.writeEnabled ? '写入已启用' : '只读或会话未启用'}</Typography><Typography color="text.secondary">数据目录：{overview.databasePath}</Typography><HealthMeter label="数据安全中心" value={100} detail="已启用" tone="success"/></Stack></SurfaceSection>
}
function LegacyFields({ fields }: { fields: { key: string; label: string; value: unknown; readStatus: string; requiresRestart: boolean; safeToWrite: boolean }[] }) {
  return <SurfaceSection title="已读取的兼容设置" description="这些字段来自现有配置体系；不可安全写入的字段仅展示。"><Stack spacing={1}>{fields.slice(0, 24).map(field => <Card key={field.key} variant="outlined"><CardContent sx={{ py: 1, '&:last-child': { pb: 1 } }}><Stack direction={{ xs: 'column', md: 'row' }} spacing={1} sx={{ justifyContent: 'space-between' }}><Box><Typography sx={{ fontWeight: 750 }}>{field.label}</Typography><Typography variant="caption" color="text.secondary">{field.key}</Typography></Box><Stack direction="row" spacing={1}><StatusBadge tone={field.readStatus === 'ok' ? 'success' : 'warning'} label={field.readStatus}/>{field.requiresRestart && <StatusBadge tone="warning" label="需重启"/>}<Typography variant="body2">{String(field.value ?? '')}</Typography></Stack></Stack></CardContent></Card>)}</Stack></SurfaceSection>
}
function confirmDescription(value?: string) {
  if (value === 'backup') return '将创建数据库和配置备份，默认不包含原始影片与原始图片。'
  if (value === 'cache') return '将清理应用生成的可重建图片缓存，不会删除源图。'
  if (value === 'restore') return '将创建恢复计划。数据库替换会在重启流程中完成。'
  if (value === 'thumbs') return '将创建全量 Thumbnail 重建任务。'
  if (value === 'logs') return '日志清理当前仅做计划提示，不执行删除。'
  return '将执行维护操作。'
}
function confirmWarnings(value?: string) {
  if (value === 'restore') return ['恢复前会先创建当前状态安全备份。', '应用可能需要重启。', '输入 CONFIRM 后才会创建计划。']
  if (value === 'cache') return ['只删除 .lmm-cache 中的生成缓存。', '不删除 Poster、Fanart、ExtraPic。']
  return []
}
