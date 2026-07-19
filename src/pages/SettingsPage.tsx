import BackupRoundedIcon from '@mui/icons-material/BackupRounded'
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import RestoreRoundedIcon from '@mui/icons-material/RestoreRounded'
import SettingsBackupRestoreRoundedIcon from '@mui/icons-material/SettingsBackupRestoreRounded'
import UploadFileRoundedIcon from '@mui/icons-material/UploadFileRounded'
import { Alert, Box, Button, ButtonBase, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, FormControlLabel, List, ListItemButton, ListItemText, MenuItem, Paper, Snackbar, Stack, Switch, TextField, Typography } from '@mui/material'
import { invoke } from '@tauri-apps/api/core'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { FormEvent, useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useBlocker, useNavigate } from 'react-router'
import { BrandMark } from '@/components/BrandMark'
import { HealthMeter, SurfaceSection } from '@/components/ProductComponents'
import { DangerConfirmDialog } from '@/components/workspace/DangerConfirmDialog'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { buildInfo } from '@/buildInfo'
import { defaultMovieWallDisplay, normalizeMovieWallDisplay } from '@/components/workspace/movieWallDisplay'
import { bridge } from '@/services/bridge'
import { useColorMode } from '@/themes/ThemeContext'
import type { BridgeHealth, TaskItem } from '@/types/media'
import type { BackupValidation, DataSafetyOverview, DiagnosticCheck, LogCleanupPreview, LogCleanupResult, MediaStorageSettings, MetaTubeSettings, MovieWallDisplaySettings, NfoSettings, PlaybackSettings, RatingRetentionSettings, ScanSettings, SettingsImportPreview, SettingsSnapshot, SystemDiagnostic, SystemSettings, UnifiedSettings, UpdateCheckResult } from '@/types/settings'
import type { ImageCachePreview } from '@/types/media'

const categories = [
  ['general', '常规'], ['library', '媒体库'], ['scan', '扫描与导入'], ['metadata', '元数据与同步'],
  ['images', '图片与缓存'], ['mediaStorage', '媒体存储'], ['playback', '播放器'], ['search', '搜索与筛选'], ['shortcuts', '快捷键'],
  ['appearance', '外观'], ['data', '数据与备份'], ['logs', '日志与诊断'], ['about', '关于'],
] as const

type Category = (typeof categories)[number][0]
type LeaveAction = 'save' | 'discard'
type LeaveIntent = 'none' | 'route' | 'window'
const size = (bytes?: number) => bytes ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : '0 MB'
const stable = (value: unknown) => JSON.stringify(value)
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T
const mediaStorageFallbackNoticeKey = 'lmm.mediaStorageFallbackNotice.v1'

async function confirmExitIfTasksRunning() {
  try {
    const tasks: TaskItem[] = await bridge.tasks(100)
    const running = tasks.filter(task => ['Pending', 'Running', 'Paused'].includes(task.status))
    if (running.length === 0) return true
    return window.confirm(`当前仍有 ${running.length} 个后台任务未结束。退出会中断正在运行的任务，确定退出吗？`)
  } catch {
    return true
  }
}

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
  const [logPreview, setLogPreview] = useState<LogCleanupPreview>()
  const [logIncludeAll, setLogIncludeAll] = useState(false)
  const [updateResult, setUpdateResult] = useState<UpdateCheckResult>()
  const [backupPath, setBackupPath] = useState('')
  const [restoreMode, setRestoreMode] = useState('all')
  const [backupValidation, setBackupValidation] = useState<BackupValidation>()
  const [importJson, setImportJson] = useState('')
  const [importPreview, setImportPreview] = useState<SettingsImportPreview>()
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const [confirm, setConfirm] = useState<'backup' | 'cache' | 'restore' | 'thumbs' | 'logs'>()
  const [createMediaRootOpen, setCreateMediaRootOpen] = useState(false)
  const [restoreDefaultsOpen, setRestoreDefaultsOpen] = useState(false)
  const [leavePromptOpen, setLeavePromptOpen] = useState(false)
  const allowWindowCloseRef = useRef(false)
  const closingAppRef = useRef(false)
  const hasUnsavedChangesRef = useRef(false)
  const systemSettingsRef = useRef<SystemSettings | undefined>(undefined)
  const autoUpdateCheckStartedRef = useRef(false)
  const leaveIntentRef = useRef<LeaveIntent>('none')

  const hasUnsavedChanges = Boolean(original && draft && stable(original) !== stable(draft))
  const blocker = useBlocker(hasUnsavedChanges)

  useEffect(() => {
    hasUnsavedChangesRef.current = hasUnsavedChanges
  }, [hasUnsavedChanges])
  useEffect(() => {
    systemSettingsRef.current = draft?.system
  }, [draft?.system])
  useEffect(() => {
    if (!draft?.system.autoCheckUpdates || autoUpdateCheckStartedRef.current) return
    autoUpdateCheckStartedRef.current = true
    bridge.checkUpdates().then(setUpdateResult).catch(() => undefined)
  }, [draft?.system.autoCheckUpdates])

  const load = useCallback(() => {
    setError('')
    return Promise.all([bridge.settings(), bridge.dataSafetyOverview(), bridge.allSettings(), bridge.defaultSettings(), bridge.health()])
      .then(([settings, data, all, defaultValues, healthValue]) => {
        setSnapshot(settings); setOverview(data); setOriginal(clone(all)); setDraft(clone(all)); setDefaults(defaultValues); setHealth(healthValue)
        setMode(all.appearance.themeMode)
        if (all.mediaStorage.usingFallbackDefault && window.localStorage.getItem(mediaStorageFallbackNoticeKey) !== 'shown') {
          setNotice('检测到软件安装在 Windows 受保护目录。媒体资源默认保存到：我的文档\\Local Media Manager\\MediaStorage。你可以随时在：设置 → 媒体存储 修改保存位置。')
          window.localStorage.setItem(mediaStorageFallbackNoticeKey, 'shown')
        }
      })
      .catch((reason: Error) => setError(reason.message))
  }, [setMode])
  useEffect(() => { void load() }, [load])

  useEffect(() => {
    if (blocker.state === 'blocked') {
      if (leaveIntentRef.current === 'window') return
      leaveIntentRef.current = 'route'
      setLeavePromptOpen(true)
    }
  }, [blocker.state])

  useEffect(() => {
    const handler = (event: BeforeUnloadEvent) => {
      if (closingAppRef.current) return
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
      if (allowWindowCloseRef.current) {
        allowWindowCloseRef.current = false
        return
      }
      if (!hasUnsavedChangesRef.current) {
        event.preventDefault()
        const system = systemSettingsRef.current
        if (system?.closeBehavior === 'minimizeToTray') {
          await invoke('hide_main_window').catch(() => getCurrentWindow().hide())
          return
        }
        const canExit = await confirmExitIfTasksRunning()
        if (!canExit) return
        closingAppRef.current = true
        await invoke('close_local_media_manager').catch(() => getCurrentWindow().destroy())
        return
      }
      event.preventDefault()
      leaveIntentRef.current = 'window'
      setLeavePromptOpen(true)
    }).then((dispose) => { unlisten = dispose }).catch(() => undefined)
    return () => { unlisten?.() }
  }, [])

  const currentTitle = categories.find(([key]) => key === category)?.[1] ?? '设置'
  const updateDraft = <K extends keyof UnifiedSettings>(key: K, value: UnifiedSettings[K]) => {
    setDraft(current => current ? { ...current, [key]: value } : current)
    if (key === 'appearance') setMode((value as UnifiedSettings['appearance']).themeMode)
  }

  const saveAll = async (createMissingMediaStorageRoot = false) => {
    if (!draft || !original) return false
    setBusy(true); setError('')
    try {
      const result = await bridge.saveAllSettings(draft, createMissingMediaStorageRoot)
      setOriginal(clone(result.settings))
      setDraft(clone(result.settings))
      setMode(result.settings.appearance.themeMode)
      setCreateMediaRootOpen(false)
      setNotice('设置已保存')
      return true
    } catch (reason) {
      const message = (reason as Error).message
      setError(message)
      const mediaStorageError = message.includes('媒体存储') || message.includes('海报目录') || message.includes('缩略图目录') || message.includes('背景图目录') || message.includes('预览图目录') || message.includes('截图目录') || message.includes('GIF 目录') || message.includes('NFO 目录') || message.includes('影片资源文件夹规则') || message.includes('文件名规则')
      if (mediaStorageError) {
        setCategory('mediaStorage')
      }
      if (message.includes('媒体存储根目录不存在')) {
        setCreateMediaRootOpen(true)
      }
      if (!mediaStorageError && message.includes('MetaTube')) setCategory('metadata')
      else if (!mediaStorageError && message.includes('NFO')) setCategory('metadata')
      else if (!mediaStorageError && message.includes('播放器')) setCategory('playback')
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
  const previewLogs = (includeAll = logIncludeAll) => draft && bridge.logCleanupPreview(draft.system.logRetentionDays, includeAll).then(setLogPreview).catch((reason: Error) => setError(reason.message))
  const cleanupLogs = () => {
    if (!draft || !logPreview) return
    void run(async () => {
      const result: LogCleanupResult = await bridge.cleanupLogs(draft.system.logRetentionDays, logIncludeAll, logPreview.confirmationToken)
      setNotice(`${result.message} 释放 ${size(result.freedBytes)}。`)
      setLogPreview(undefined)
      await previewLogs(logIncludeAll)
    }, '日志清理完成')
  }
  const checkUpdates = () => run(async () => {
    const result = await bridge.checkUpdates()
    setUpdateResult(result)
    setNotice(result.message)
    await load()
  }, '更新检查完成')
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
    allowWindowCloseRef.current = false
    leaveIntentRef.current = 'none'
    if (blocker.state === 'blocked') blocker.reset()
  }
  const finishLeave = async (action: LeaveAction) => {
    const intent = leaveIntentRef.current
    if (action === 'save' && !await saveAll()) {
      if (blocker.state === 'blocked') blocker.reset()
      leaveIntentRef.current = 'none'
      setLeavePromptOpen(false)
      return
    }
    if (action === 'discard' && original) {
      setDraft(clone(original))
      setMode(original.appearance.themeMode)
    }
    setLeavePromptOpen(false)
    if (intent === 'window') {
      leaveIntentRef.current = 'none'
      closingAppRef.current = true
      allowWindowCloseRef.current = true
      try {
        await invoke('close_local_media_manager')
      } catch {
        await getCurrentWindow().destroy()
      }
      return
    }
    leaveIntentRef.current = 'none'
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
        {category === 'general' && <GeneralSection snapshot={snapshot} system={draft.system} setSystem={(value) => updateDraft('system', value)}/>}
        {category === 'library' && <LibrarySection onOpen={() => navigate('/libraries')}/>}
        {category === 'scan' && <ScanSection scan={draft.scan} setScan={(value) => updateDraft('scan', value)}/>}
        {category === 'metadata' && <MetadataSection metaTube={draft.metaTube} setMetaTube={(value) => updateDraft('metaTube', value)} nfo={draft.nfo} setNfo={(value) => updateDraft('nfo', value)} busy={busy} testMetaTube={testMetaTube}/>}
        {category === 'images' && <ImagesSection cachePreview={cachePreview} setCachePreview={setCachePreview} onClean={() => setConfirm('cache')} onThumbs={() => setConfirm('thumbs')}/>}
        {category === 'mediaStorage' && <MediaStorageSection mediaStorage={draft.mediaStorage} defaults={defaults.mediaStorage} setMediaStorage={(value) => updateDraft('mediaStorage', value)} setNotice={setNotice}/>}
        {category === 'playback' && <PlaybackSection playback={draft.playback} setPlayback={(value) => updateDraft('playback', value)}/>}
        {category === 'search' && <PlannedSection labels={['默认搜索范围', '默认排序', '默认卡片/列表模式', '保存页面筛选状态']}/>}
        {category === 'shortcuts' && <ShortcutSection system={draft.system} setSystem={(value) => updateDraft('system', value)}/>}
        {category === 'appearance' && <Stack spacing={2}>
          <AppearanceSection mode={draft.appearance.themeMode} setMode={(value) => updateDraft('appearance', { themeMode: value })}/>
          <MovieWallSection value={normalizeMovieWallDisplay(draft.movieWallDisplay ?? defaultMovieWallDisplay)} setValue={(value) => updateDraft('movieWallDisplay', normalizeMovieWallDisplay(value))}/>
        </Stack>}
        {category === 'data' && <DataSection overview={overview} ratingRetention={draft.ratingRetention} setRatingRetention={(value) => updateDraft('ratingRetention', value)} backupPath={backupPath} setBackupPath={setBackupPath} restoreMode={restoreMode} setRestoreMode={setRestoreMode} validation={backupValidation} setValidation={setBackupValidation} importJson={importJson} setImportJson={setImportJson} importPreview={importPreview} previewImport={previewImport} onBackup={() => setConfirm('backup')} onRestore={() => setConfirm('restore')}/>}
        {category === 'logs' && <LogsSection system={draft.system} setSystem={(value) => updateDraft('system', value)} diagnostics={diagnostics} logPreview={logPreview} includeAll={logIncludeAll} setIncludeAll={setLogIncludeAll} runDiagnostics={() => bridge.settingsDiagnostics().then(setDiagnostics).catch((reason: Error) => setError(reason.message))} previewLogs={() => void previewLogs()}/>}
        {category === 'about' && <AboutSection overview={overview} health={health} system={draft.system} setSystem={(value) => updateDraft('system', value)} updateResult={updateResult} checkUpdates={checkUpdates}/>}
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
    }}/>
    <Dialog open={restoreDefaultsOpen} onClose={() => setRestoreDefaultsOpen(false)}>
      <DialogTitle>恢复默认设置</DialogTitle>
      <DialogContent><DialogContentText>将当前设置恢复为默认值。修改将在点击“保存设置”后生效。</DialogContentText></DialogContent>
      <DialogActions><Button onClick={() => setRestoreDefaultsOpen(false)}>取消</Button><Button variant="contained" onClick={restoreDefaultsToDraft}>恢复默认值</Button></DialogActions>
    </Dialog>
    <Dialog open={createMediaRootOpen} onClose={() => setCreateMediaRootOpen(false)}>
      <DialogTitle>创建媒体存储根目录</DialogTitle>
      <DialogContent><DialogContentText>该目录不存在，是否创建并保存当前设置？只会创建根目录，不会移动、删除或重命名任何现有资源。</DialogContentText></DialogContent>
      <DialogActions>
        <Button onClick={() => setCreateMediaRootOpen(false)}>返回修改</Button>
        <Button variant="contained" disabled={busy} onClick={() => void saveAll(true)}>{busy ? '正在保存...' : '创建并保存'}</Button>
      </DialogActions>
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
    <Dialog open={Boolean(logPreview)} onClose={() => !busy && setLogPreview(undefined)} fullWidth maxWidth="md">
      <DialogTitle>确认清理历史日志</DialogTitle>
      <DialogContent dividers>
        {logPreview && <Stack spacing={1.5}>
          <Alert severity={logPreview.deletableCount ? 'warning' : 'info'}>将删除 {logPreview.deletableCount} 个历史日志，预计释放 {size(logPreview.deletableBytes)}。当前活动日志不会删除。</Alert>
          <Typography variant="body2" color="text.secondary">目录：{logPreview.logDirectory}</Typography>
          <Stack spacing={0.75}>{logPreview.files.slice(0, 24).map(file => <Paper key={file.path} variant="outlined" sx={{ p: 1, borderRadius: 1.5 }}>
            <Stack direction={{ xs: 'column', md: 'row' }} spacing={1} sx={{ justifyContent: 'space-between' }}>
              <Box><Typography sx={{ fontWeight: 800 }}>{file.name}</Typography><Typography variant="caption" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{file.path}</Typography></Box>
              <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}><StatusBadge tone={file.eligible ? 'warning' : 'success'} label={file.reason}/><Typography variant="body2">{size(file.bytes)}</Typography></Stack>
            </Stack>
          </Paper>)}</Stack>
        </Stack>}
      </DialogContent>
      <DialogActions><Button onClick={() => setLogPreview(undefined)} disabled={busy}>取消</Button><Button color="error" variant="contained" disabled={busy || !logPreview?.deletableCount} onClick={cleanupLogs}>{busy ? '清理中...' : '确认清理'}</Button></DialogActions>
    </Dialog>
    <Snackbar open={Boolean(notice)} autoHideDuration={2500} onClose={() => setNotice('')} message={notice}/>
  </WorkspacePage>
}

function GeneralSection({ snapshot, system, setSystem }: { snapshot: SettingsSnapshot; system: SystemSettings; setSystem: (value: SystemSettings) => void }) {
  return <Stack spacing={2}>
    <SurfaceSection title="常规状态" description="统一设置服务读取结果。"><Stack spacing={1}><StatusBadge tone="success" label={`${snapshot.mappedCount} 个已映射设置`}/><StatusBadge tone={snapshot.errors.length ? 'warning' : 'success'} label={`${snapshot.errors.length} 个读取问题`}/><Typography variant="body2" color="text.secondary">读取时间：{new Date(snapshot.readAt).toLocaleString()}</Typography></Stack></SurfaceSection>
    <SurfaceSection title="系统" description="应用界面语言与窗口关闭行为。语言设置只影响 Local Media Manager 界面，不影响刮削、元数据或字幕语言。">
      <Stack spacing={1.5}>
        <TextField select size="small" label="应用语言" value={system.language} onChange={event => setSystem({ ...system, language: event.target.value as SystemSettings['language'] })}>
          <MenuItem value="system">跟随系统</MenuItem>
          <MenuItem value="zh-CN">简体中文</MenuItem>
        </TextField>
        <Alert severity="info">当前可维护语言为简体中文与跟随系统。英文资源不完整，因此本轮不提供正式 English 选项。</Alert>
        <TextField select size="small" label="关闭主窗口时" value={system.closeBehavior} onChange={event => setSystem({ ...system, closeBehavior: event.target.value as SystemSettings['closeBehavior'] })}>
          <MenuItem value="exit">退出应用</MenuItem>
          <MenuItem value="minimizeToTray">最小化到托盘</MenuItem>
        </TextField>
        <FormControlLabel control={<Switch checked={system.startMinimizedToTray} onChange={event => setSystem({ ...system, startMinimizedToTray: event.target.checked })}/>} label="启动后最小化到托盘（保存后下次启动生效）"/>
        <Typography variant="body2" color="text.secondary">托盘菜单包含显示主窗口、隐藏主窗口和退出。退出会结束主程序及 Bridge 子进程。</Typography>
      </Stack>
    </SurfaceSection>
  </Stack>
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
function MediaStorageSection({ mediaStorage, defaults, setMediaStorage, setNotice }: { mediaStorage: MediaStorageSettings; defaults: MediaStorageSettings; setMediaStorage: (v: MediaStorageSettings) => void; setNotice: (v: string) => void }) {
  const chooseRoot = async () => {
    try {
      const selected = await invoke<string | null>('choose_directory')
      if (selected) setMediaStorage({ ...mediaStorage, rootPath: selected })
    } catch (reason) {
      setNotice((reason as Error).message)
    }
  }
  const openRoot = () => {
    bridge.openDirectory(mediaStorage.rootPath).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))
  }
  const update = <K extends keyof MediaStorageSettings>(key: K, value: MediaStorageSettings[K]) => setMediaStorage({ ...mediaStorage, [key]: value })
  const rows: [keyof MediaStorageSettings, string][] = [
    ['wallCropsDirectory', '影片墙裁切目录'],
    ['postersDirectory', '海报目录'],
    ['thumbnailsDirectory', '缩略图目录'],
    ['fanartDirectory', '背景图目录'],
    ['previewsDirectory', '预览图目录'],
    ['screenshotsDirectory', '截图目录'],
    ['gifDirectory', 'GIF 目录'],
    ['nfoDirectory', 'NFO 目录'],
  ]
  const previewRows = buildMediaStoragePreview(mediaStorage)
  return <Stack spacing={2}>
    {mediaStorage.usingFallbackDefault && <Alert severity="info">检测到默认数据目录不可写，当前默认使用“我的文档\Local Media Manager\MediaStorage”。</Alert>}
    <SurfaceSection title="媒体资源根目录" description="用于保存海报、缩略图、背景图、预览图、截图、GIF 和 NFO。修改后不会自动移动现有文件。">
      <Stack spacing={1.5}>
        <TextField size="small" label="根目录路径" value={mediaStorage.rootPath} onChange={event => update('rootPath', event.target.value)} helperText="建议使用独立数据目录，不要放在程序安装目录、resources 或 Web assets 内。"/>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <Button variant="outlined" onClick={chooseRoot}>浏览文件夹</Button>
          <Button variant="outlined" onClick={openRoot}>打开文件夹</Button>
          <Button variant="text" onClick={() => update('rootPath', defaults.rootPath)}>使用默认路径</Button>
        </Stack>
      </Stack>
    </SurfaceSection>
    <SurfaceSection title="资源目录结构" description="编辑相对目录名；不支持绝对路径、盘符、.. 或 Windows 保留设备名。">
      <Stack spacing={1.25}>
        {rows.map(([key, label]) => <TextField key={key} size="small" label={label} value={mediaStorage[key]} onChange={event => update(key, event.target.value)} />)}
      </Stack>
    </SurfaceSection>
    <SurfaceSection title="文件命名规则" description="本轮只支持 {MovieCode} 和 {MovieTitle}，默认仅使用 {MovieCode}。">
      <Stack spacing={1.5}>
        <TextField size="small" label="影片资源文件夹规则" value={mediaStorage.movieFolderTemplate} onChange={event => update('movieFolderTemplate', event.target.value)} />
        <TextField size="small" label="文件名规则" value={mediaStorage.fileNameTemplate} onChange={event => update('fileNameTemplate', event.target.value)} />
        <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 2, bgcolor: 'action.hover' }}>
          <Typography sx={{ fontWeight: 800, mb: 1 }}>路径预览</Typography>
          <Stack spacing={0.75}>{previewRows.map(row => <Typography key={row.label} variant="body2" sx={{ overflowWrap: 'anywhere' }}><Box component="span" sx={{ fontWeight: 750 }}>{row.label}：</Box>{row.path}</Typography>)}</Stack>
        </Paper>
      </Stack>
    </SurfaceSection>
  </Stack>
}
function PlaybackSection({ playback, setPlayback }: { playback: PlaybackSettings; setPlayback: (v: PlaybackSettings) => void }) {
  return <SurfaceSection title="播放器" description="复用现有播放服务，不新增播放器内核。"><Stack spacing={1.5}><FormControlLabel control={<Switch checked={playback.useSystemDefault} onChange={event => setPlayback({ ...playback, useSystemDefault: event.target.checked })}/>} label="使用系统默认播放器"/><TextField disabled={playback.useSystemDefault} size="small" label="外部播放器路径" value={playback.playerPath} onChange={event => setPlayback({ ...playback, playerPath: event.target.value })}/></Stack></SurfaceSection>
}
function MovieWallSection({ value, setValue }: { value: MovieWallDisplaySettings; setValue: (value: MovieWallDisplaySettings) => void }) {
  const sizes: { key: MovieWallDisplaySettings['posterSize']; label: string; width: number; height: number }[] = [
    { key: 'small', label: '小', width: 46, height: 64 },
    { key: 'medium', label: '中', width: 58, height: 72 },
    { key: 'large', label: '大', width: 78, height: 64 },
  ]
  const orientations: { key: MovieWallDisplaySettings['posterOrientation']; label: string; description: string; width: number; height: number }[] = [
    { key: 'landscape', label: '横版海报', description: '显示横幅大图', width: 140, height: 78 },
    { key: 'portrait', label: '竖版海报', description: '显示竖向封面图', width: 66, height: 96 },
  ]
  return <SurfaceSection title="影片墙显示" description="统一调整影片墙卡片密度和海报比例，所有复用 MovieWall 的页面同步生效。">
    <Stack spacing={3}>
      <Box>
        <Typography sx={{ fontWeight: 850, mb: 0.5 }}>影片卡片大小</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>影响卡片视图的海报尺寸和每页可见密度；不会改变当前搜索、筛选、排序或页码。</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3, minmax(0, 1fr))' }, gap: 1.5 }}>
          {sizes.map(option => <VisualOptionCard key={option.key} selected={value.posterSize === option.key} label={option.label} onClick={() => setValue({ ...value, posterSize: option.key })}>
            <PreviewRail><PreviewPoster width={option.width} height={option.height}/></PreviewRail>
          </VisualOptionCard>)}
        </Box>
      </Box>
      <Box>
        <Typography sx={{ fontWeight: 850, mb: 0.5 }}>海报方向</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>只影响卡片视图；列表视图保持现有布局。</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 1.5 }}>
          {orientations.map(option => <VisualOptionCard key={option.key} selected={value.posterOrientation === option.key} label={option.label} description={option.description} onClick={() => setValue({ ...value, posterOrientation: option.key })}>
            <PreviewRail><PreviewPoster width={option.width} height={option.height}/></PreviewRail>
          </VisualOptionCard>)}
        </Box>
      </Box>
      <Typography variant="body2" color="text.secondary">默认值：竖版海报 / 中。点击“保存设置”后持久化，重启后恢复。</Typography>
    </Stack>
  </SurfaceSection>
}

function VisualOptionCard({ selected, label, description, onClick, children }: { selected: boolean; label: string; description?: string; onClick: () => void; children: ReactNode }) {
  return <ButtonBase onClick={onClick} sx={{ display: 'block', width: '100%', textAlign: 'inherit', borderRadius: 2 }}>
    <Paper variant="outlined" sx={{
      p: 1.5,
      minHeight: 150,
      borderRadius: 2,
      borderColor: selected ? 'primary.main' : 'divider',
      bgcolor: selected ? 'primary.main' : 'background.paper',
      color: selected ? 'primary.contrastText' : 'text.primary',
      boxShadow: selected ? theme => `0 0 0 1px ${theme.palette.primary.main} inset` : undefined,
    }}>
      {children}
      <Typography sx={{ mt: 1.25, fontWeight: 900, textAlign: 'center' }}>{label}</Typography>
      {description && <Typography variant="caption" sx={{ display: 'block', textAlign: 'center', color: selected ? 'primary.contrastText' : 'text.secondary', opacity: selected ? 0.78 : 1 }}>{description}</Typography>}
    </Paper>
  </ButtonBase>
}

function PreviewRail({ children }: { children: ReactNode }) {
  return <Box sx={{ height: 78, borderRadius: 1.5, bgcolor: theme => theme.palette.mode === 'dark' ? 'rgba(255,255,255,.06)' : 'rgba(0,0,0,.06)', display: 'grid', placeItems: 'center', overflow: 'hidden' }}>{children}</Box>
}

function PreviewPoster({ width, height }: { width: number; height: number }) {
  return <Box sx={{ width, height, borderRadius: 1, bgcolor: 'primary.light' }}/>
}
function ScanSection({ scan, setScan }: { scan: ScanSettings; setScan: (value: ScanSettings) => void }) {
  const updateMinimumSize = (value: string) => {
    const parsed = Number(value)
    setScan({ ...scan, minFileSizeMb: Number.isFinite(parsed) ? parsed : 0 })
  }
  return <SurfaceSection title="扫描设置" description="控制媒体库扫描时如何识别影片文件。设置保存后，下次扫描开始生效。">
    <Stack spacing={1.5}>
      <TextField
        type="number"
        size="small"
        label="最小影片文件大小"
        value={scan.minFileSizeMb}
        onChange={event => updateMinimumSize(event.target.value)}
        slotProps={{ htmlInput: { min: 0, max: 1048576, step: 1 } }}
        helperText="单位 MB。扫描时会忽略小于此大小的视频文件；0 表示不按大小跳过。"
      />
      <Alert severity="info">番号识别为扫描固定行为，不再提供旧版开关。</Alert>
    </Stack>
  </SurfaceSection>
}
function PlannedSection({ labels }: { labels: string[] }) {
  return <SurfaceSection title="规划项" description="这些偏好暂未形成正式设置；不会显示旧版内部配置字段。"><Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>{labels.map(label => <Chip key={label} label={label} variant="outlined"/>)}<StatusBadge tone="neutral" label="未启用"/></Stack></SurfaceSection>
}
function ShortcutSection({ system, setSystem }: { system: SystemSettings; setSystem: (value: SystemSettings) => void }) {
  const keys = [
    ['←', '影片墙上一页', '输入框、弹窗、菜单打开时不触发'],
    ['→', '影片墙下一页', '输入框、弹窗、菜单打开时不触发'],
    ['Ctrl+G', '聚焦页码输入', '仅影片墙分页出现时可用'],
    ['Enter', '确认搜索或页码输入', '仅当前输入控件内生效'],
    ['Esc', '清空搜索、取消页码输入或关闭弹层', '优先交给当前弹层/输入框处理'],
    ['Ctrl+F', '聚焦搜索', '作为可管理快捷键列入；当前页面级搜索框不会抢输入焦点'],
    ['Delete', '危险删除', '默认不启用，只能通过 Safe Delete 确认流程'],
  ]
  return <SurfaceSection title="快捷键" description="本轮提供快捷键说明、总开关和恢复默认；不提供复杂按键录制器。">
    <Stack spacing={1.5}>
      <FormControlLabel control={<Switch checked={system.globalShortcutsEnabled} onChange={event => setSystem({ ...system, globalShortcutsEnabled: event.target.checked })}/>} label="启用全局快捷键"/>
      <Stack spacing={1}>{keys.map(([key, label, detail]) => <Card key={key} variant="outlined"><CardContent sx={{ py: 1, '&:last-child': { pb: 1 } }}><Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ justifyContent: 'space-between', alignItems: { sm: 'center' } }}><Box><Typography sx={{ fontWeight: 850 }}>{label}</Typography><Typography variant="caption" color="text.secondary">{detail}</Typography></Box><StatusBadge label={key} tone={system.globalShortcutsEnabled ? 'info' : 'neutral'}/></Stack></CardContent></Card>)}</Stack>
      <Stack direction="row" spacing={1}><Button variant="outlined" onClick={() => setSystem({ ...system, globalShortcutsEnabled: true })}>恢复默认</Button><StatusBadge tone="success" label="输入框焦点保护已启用"/></Stack>
    </Stack>
  </SurfaceSection>
}
function AppearanceSection({ mode, setMode }: { mode: 'light' | 'dark'; setMode: (value: 'light' | 'dark') => void }) {
  return <SurfaceSection title="外观" description="主题预览立即生效，点击保存设置后持久化。"><Stack direction="row" spacing={1}><Button variant={mode === 'light' ? 'contained' : 'outlined'} startIcon={<LightModeRoundedIcon/>} onClick={() => setMode('light')}>浅色</Button><Button variant={mode === 'dark' ? 'contained' : 'outlined'} startIcon={<DarkModeRoundedIcon/>} onClick={() => setMode('dark')}>深色</Button></Stack></SurfaceSection>
}
function DataSection({ overview, ratingRetention, setRatingRetention, backupPath, setBackupPath, restoreMode, setRestoreMode, validation, setValidation, importJson, setImportJson, importPreview, previewImport, onBackup, onRestore }: { overview: DataSafetyOverview; ratingRetention: RatingRetentionSettings; setRatingRetention: (v: RatingRetentionSettings) => void; backupPath: string; setBackupPath: (v: string) => void; restoreMode: string; setRestoreMode: (v: string) => void; validation?: BackupValidation; setValidation: (v: BackupValidation) => void; importJson: string; setImportJson: (v: string) => void; importPreview?: SettingsImportPreview; previewImport: () => void; onBackup: () => void; onRestore: () => void }) {
  return <Stack spacing={2}><SurfaceSection title="数据位置" description="不会把原始影片和原始图片打包进备份。"><Stack spacing={1}><Typography>数据库：{overview.databasePath}</Typography><Typography>数据库大小：{size(overview.databaseBytes)}</Typography><Typography>配置库：{overview.configDatabasePath}</Typography><Typography>备份目录：{overview.backupDirectory}</Typography><Typography>缓存目录：{overview.cacheDirectory}</Typography><Typography>日志目录：{overview.logDirectory}</Typography><Typography>最近备份：{overview.lastBackupAt ? new Date(overview.lastBackupAt).toLocaleString() : '暂无'}</Typography></Stack></SurfaceSection><SurfaceSection title="评分保留" description="删除已评分影片时保存番号与评分；以后重新导入相同番号时自动恢复。"><FormControlLabel control={<Switch checked={ratingRetention.enabled} onChange={event => setRatingRetention({ enabled: event.target.checked })}/>} label="自动保留并恢复已删除影片评分"/></SurfaceSection><SurfaceSection title="备份与恢复计划" description="恢复采用计划文件，避免运行中热替换数据库。"><Stack spacing={1.5}><Button variant="contained" startIcon={<BackupRoundedIcon/>} onClick={onBackup}>创建手动备份</Button><TextField size="small" label="备份路径" value={backupPath} onChange={event => setBackupPath(event.target.value)}/><Stack direction="row" spacing={1}><Button variant="outlined" onClick={() => bridge.validateBackup(backupPath).then(setValidation)}>校验备份</Button><TextField select size="small" label="恢复模式" value={restoreMode} onChange={event => setRestoreMode(event.target.value)} sx={{ width: 150 }}><MenuItem value="all">全部</MenuItem><MenuItem value="database">仅数据库</MenuItem><MenuItem value="settings">仅设置</MenuItem></TextField><Button color="error" variant="outlined" startIcon={<RestoreRoundedIcon/>} disabled={!validation?.valid} onClick={onRestore}>创建恢复计划</Button></Stack>{validation && <Alert severity={validation.valid ? 'success' : 'error'}>{validation.valid ? '备份校验通过' : validation.errors.join('；')}</Alert>}</Stack></SurfaceSection><SurfaceSection title="配置导入导出" description="导出会剔除敏感字段；导入先预览差异，不直接覆盖。"><Stack spacing={1.5}><Button startIcon={<SettingsBackupRestoreRoundedIcon/>} onClick={() => bridge.exportSettings().then(result => setImportJson(JSON.stringify(result, null, 2)))}>导出当前设置</Button><TextField multiline minRows={6} label="导入 JSON / 导出预览" value={importJson} onChange={event => setImportJson(event.target.value)}/><Button startIcon={<UploadFileRoundedIcon/>} variant="outlined" onClick={previewImport}>预览导入差异</Button>{importPreview && <Alert severity={importPreview.valid ? 'info' : 'warning'}>{importPreview.valid ? `可识别 ${importPreview.changes.length} 项：${importPreview.categories.join('、')}` : importPreview.warnings.join('；')}</Alert>}</Stack></SurfaceSection></Stack>
}
function LogsSection({ system, setSystem, diagnostics, logPreview, includeAll, setIncludeAll, runDiagnostics, previewLogs }: {
  system: SystemSettings
  setSystem: (value: SystemSettings) => void
  diagnostics?: SystemDiagnostic
  logPreview?: LogCleanupPreview
  includeAll: boolean
  setIncludeAll: (value: boolean) => void
  runDiagnostics: () => void
  previewLogs: () => void
}) {
  return <Stack spacing={2}>
    <SurfaceSection title="日志清理" description="只清理应用日志目录中的历史日志，不删除数据库、配置、任务记录或当前正在写入的日志。">
      <Stack spacing={1.5}>
        <TextField select size="small" label="日志保留时间" value={system.logRetentionDays} onChange={event => setSystem({ ...system, logRetentionDays: Number(event.target.value) as SystemSettings['logRetentionDays'] })}>
          <MenuItem value={0}>永久保留</MenuItem>
          {[7, 14, 30, 90].map(days => <MenuItem key={days} value={days}>{days} 天</MenuItem>)}
        </TextField>
        <FormControlLabel control={<Switch checked={includeAll} onChange={event => setIncludeAll(event.target.checked)}/>} label="手动清理全部可安全删除的历史日志"/>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <Button variant="contained" onClick={previewLogs}>预览日志清理</Button>
          <Button startIcon={<RefreshRoundedIcon/>} variant="outlined" onClick={runDiagnostics}>执行基础诊断</Button>
        </Stack>
        {logPreview && <Alert severity={logPreview.deletableCount ? 'warning' : 'info'}>
          日志目录：{logPreview.logDirectory}。共 {logPreview.fileCount} 个日志，{size(logPreview.totalBytes)}；可删除 {logPreview.deletableCount} 个，预计释放 {size(logPreview.deletableBytes)}。活动日志保留：{logPreview.activeLogs.join('、') || '无'}。
        </Alert>}
      </Stack>
    </SurfaceSection>
    <SurfaceSection title="诊断" description="诊断包不包含影片、原始图片、Cookie、Token 或完整个人路径列表。">
      <Stack spacing={1}>{diagnostics?.checks.map(check => <DiagnosticRow key={check.key} check={check}/>)}</Stack>
    </SurfaceSection>
  </Stack>
}
function DiagnosticRow({ check }: { check: DiagnosticCheck }) {
  return <Card variant="outlined"><CardContent sx={{ py: 1, '&:last-child': { pb: 1 } }}><Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}><StatusBadge tone={check.status === 'success' ? 'success' : check.status === 'error' ? 'error' : 'warning'} label={check.label}/><Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{check.detail}</Typography></Stack></CardContent></Card>
}
function AboutSection({ overview, health, system, setSystem, updateResult, checkUpdates }: {
  overview: DataSafetyOverview
  health?: BridgeHealth
  system: SystemSettings
  setSystem: (value: SystemSettings) => void
  updateResult?: UpdateCheckResult
  checkUpdates: () => void
}) {
  return <Stack spacing={2}>
    <SurfaceSection title="关于" description="本地优先、可维护的现代媒体管理工具。"><Stack spacing={1}><BrandMark/><Typography>Version: {buildInfo.version}</Typography><Typography>Commit: {buildInfo.commit}</Typography><Typography>Build: {buildInfo.buildTime}</Typography><Typography>Bridge: {health?.version ?? 'unknown'} · {health?.writeEnabled ? '写入已启用' : '只读或会话未启用'}</Typography><Typography color="text.secondary">数据目录：{overview.databasePath}</Typography><HealthMeter label="数据安全中心" value={100} detail="已启用" tone="success"/></Stack></SurfaceSection>
    <SurfaceSection title="检查更新" description="当前仅检查 GitHub Release 并打开官方下载页面，不执行自动下载安装。网络失败不会影响应用启动。">
      <Stack spacing={1.5}>
        <FormControlLabel control={<Switch checked={system.autoCheckUpdates} onChange={event => setSystem({ ...system, autoCheckUpdates: event.target.checked })}/>} label="启动后自动检查更新"/>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <Button variant="contained" onClick={checkUpdates}>检查更新</Button>
          <Button variant="outlined" onClick={() => window.open(updateResult?.releaseUrl || 'https://github.com/xiaoxuan19911030-commits/LocalMediaManager/releases', '_blank')}>打开发布页面</Button>
        </Stack>
        <Typography variant="body2" color="text.secondary">最近检查：{system.lastUpdateCheckAt ? new Date(system.lastUpdateCheckAt).toLocaleString() : '暂无'}</Typography>
        {updateResult && <Alert severity={updateResult.status === 'update-available' ? 'warning' : updateResult.status === 'network-error' ? 'info' : 'success'}>
          {updateResult.message}{updateResult.latestVersion ? ` 最新版本：${updateResult.latestVersion}` : ''}
        </Alert>}
        {updateResult?.releaseNotes && <TextField multiline minRows={4} label="更新说明" value={updateResult.releaseNotes} slotProps={{ input: { readOnly: true } }}/>}
      </Stack>
    </SurfaceSection>
  </Stack>
}
function confirmDescription(value?: string) {
  if (value === 'backup') return '将创建数据库和配置备份，默认不包含原始影片与原始图片。'
  if (value === 'cache') return '将清理应用生成的可重建图片缓存，不会删除源图。'
  if (value === 'restore') return '将创建恢复计划。数据库替换会在重启流程中完成。'
  if (value === 'thumbs') return '将创建全量 Thumbnail 重建任务。'
  return '将执行维护操作。'
}
function confirmWarnings(value?: string) {
  if (value === 'restore') return ['恢复前会先创建当前状态安全备份。', '应用可能需要重启。', '输入 CONFIRM 后才会创建计划。']
  if (value === 'cache') return ['只删除 .lmm-cache 中的生成缓存。', '不删除 Poster、Fanart、ExtraPic。']
  return []
}

function buildMediaStoragePreview(settings: MediaStorageSettings) {
  const movieFolder = renderMediaTemplate(settings.movieFolderTemplate)
  const fileName = renderMediaTemplate(settings.fileNameTemplate)
  const root = settings.rootPath || '<RootPath>'
  const join = (...parts: string[]) => parts.map(part => part.trim().replace(/^\\+|\\+$/g, '')).filter(Boolean).join('\\')
  return [
    { label: 'WallCrop', path: join(root, settings.wallCropsDirectory, movieFolder, `${fileName}.jpg`) },
    { label: 'Poster', path: join(root, settings.postersDirectory, movieFolder, `${fileName}.jpg`) },
    { label: 'Thumbnail', path: join(root, settings.thumbnailsDirectory, movieFolder, `${fileName}.jpg`) },
    { label: 'Fanart', path: join(root, settings.fanartDirectory, movieFolder, `${fileName}.jpg`) },
    { label: 'Preview', path: join(root, settings.previewsDirectory, movieFolder, `${fileName}.jpg`) },
    { label: 'Screenshot', path: join(root, settings.screenshotsDirectory, movieFolder, `${fileName}_001.jpg`) },
    { label: 'GIF', path: join(root, settings.gifDirectory, movieFolder, `${fileName}_001.gif`) },
    { label: 'NFO', path: join(root, settings.nfoDirectory, movieFolder, `${fileName}.nfo`) },
  ]
}

function renderMediaTemplate(template: string) {
  return (template || '')
    .replaceAll('{MovieCode}', 'ABC-123')
    .replaceAll('{MovieTitle}', 'Example Movie')
    .trim() || '<empty>'
}
