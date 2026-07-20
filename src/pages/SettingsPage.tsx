import BackupRoundedIcon from '@mui/icons-material/BackupRounded'
import CloudDownloadRoundedIcon from '@mui/icons-material/CloudDownloadRounded'
import CloudOffRoundedIcon from '@mui/icons-material/CloudOffRounded'
import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded'
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import RestoreRoundedIcon from '@mui/icons-material/RestoreRounded'
import VerifiedRoundedIcon from '@mui/icons-material/VerifiedRounded'
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
import type { BackupValidation, DataBackupSettings, DataSafetyOverview, DiagnosticCheck, FfmpegToolStatus, JavBusSettings, LogCleanupPreview, LogCleanupResult, MediaStorageSettings, MovieWallDisplaySettings, PlaybackSettings, ProviderDiagnosticResult, ProviderNetworkSettings, RatingRetentionSettings, ScanSettings, SearchSettings, SettingsSnapshot, SystemDiagnostic, SystemSettings, UnifiedSettings, UpdateCheckResult, WebMetadataSettings } from '@/types/settings'
import type { ImageCachePreview } from '@/types/media'

const categories = [
  ['general', '常规'], ['appearance', '外观'], ['search', '搜索与筛选'], ['metadata', '元数据'],
  ['plugins', '插件中心'], ['mediaStorage', '媒体资源'], ['shortcuts', '快捷键'], ['data', '数据与备份'], ['about', '关于'],
] as const

type Category = (typeof categories)[number][0]
type LeaveAction = 'save' | 'discard'
type LeaveIntent = 'none' | 'route' | 'window'
const size = (bytes?: number) => bytes ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : '0 MB'
const stable = (value: unknown) => JSON.stringify(value)
const clone = <T,>(value: T): T => JSON.parse(JSON.stringify(value)) as T
const mediaStorageFallbackNoticeKey = 'lmm.mediaStorageFallbackNotice.v1'
const pluginDisplayName = (id: string) => ({ private: 'Private', bus: 'BUS', javbus: 'JavBus' }[id.toLowerCase()] || id || '兼容服务')

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
      else if (!mediaStorageError && message.includes('播放器')) setCategory('general')
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
    return <WorkspacePage title="设置" description="设置与数据安全中心" error={error} loading={!error}/>
  }

  return <WorkspacePage title="设置" description="统一管理常规、外观、搜索、元数据、插件、媒体资源、快捷键、数据备份和关于信息。" error={error}
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
        {category === 'metadata' && <MetadataSection/>}
        {category === 'plugins' && <PluginsSection snapshot={snapshot} providerNetwork={draft.providerNetwork} setProviderNetwork={(value) => updateDraft('providerNetwork', value)} javBus={draft.javBus} setJavBus={(value) => updateDraft('javBus', value)} dmm={draft.dmm} setDmm={(value) => updateDraft('dmm', value)} javDb={draft.javDb} setJavDb={(value) => updateDraft('javDb', value)} setNotice={setNotice}/>}
        {category === 'mediaStorage' && <MediaStorageSection mediaStorage={draft.mediaStorage} defaults={defaults.mediaStorage} setMediaStorage={(value) => updateDraft('mediaStorage', value)} setNotice={setNotice}/>}
        {category === 'search' && <SearchSection search={draft.search} setSearch={(value) => updateDraft('search', value)}/>}
        {category === 'shortcuts' && <ShortcutSection system={draft.system} setSystem={(value) => updateDraft('system', value)}/>}
        {category === 'appearance' && <Stack spacing={2}>
          <AppearanceSection mode={draft.appearance.themeMode} setMode={(value) => updateDraft('appearance', { themeMode: value })}/>
          <MovieWallSection value={normalizeMovieWallDisplay(draft.movieWallDisplay ?? defaultMovieWallDisplay)} setValue={(value) => updateDraft('movieWallDisplay', normalizeMovieWallDisplay(value))}/>
        </Stack>}
        {category === 'data' && <DataSection overview={overview} ratingRetention={draft.ratingRetention} setRatingRetention={(value) => updateDraft('ratingRetention', value)} dataBackup={draft.dataBackup} setDataBackup={(value) => updateDraft('dataBackup', value)} backupPath={backupPath} setBackupPath={setBackupPath} restoreMode={restoreMode} setRestoreMode={setRestoreMode} validation={backupValidation} setValidation={setBackupValidation} setNotice={setNotice} onBackup={() => setConfirm('backup')} onRestore={() => setConfirm('restore')}/>}
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
function MetadataSection() {
  return <Stack spacing={2}>
    <SurfaceSection title="同步行为" description="元数据由 MetaTube、NFO 和同步流程统一维护，普通设置页不暴露内部字段。">
      <Stack spacing={1}>
        <StatusBadge tone="info" label="同步元数据：只补缺"/>
        <StatusBadge tone="warning" label="重新刮削：覆盖刮削元数据和刮削资源"/>
        <StatusBadge tone="success" label="收藏、评分、自定义标签、播放记录受保护"/>
        <Typography variant="body2" color="text.secondary">NFO 固定生成独立 .nfo 文件；若元数据错误，请重新刮削或重新导入 NFO。</Typography>
      </Stack>
    </SurfaceSection>
  </Stack>
}

function PluginsSection({ snapshot, providerNetwork, setProviderNetwork, javBus, setJavBus, dmm, setDmm, javDb, setJavDb, setNotice }: {
  snapshot: SettingsSnapshot
  providerNetwork?: ProviderNetworkSettings
  setProviderNetwork: (value: ProviderNetworkSettings) => void
  javBus: JavBusSettings
  setJavBus: (value: JavBusSettings) => void
  dmm: WebMetadataSettings
  setDmm: (value: WebMetadataSettings) => void
  javDb: WebMetadataSettings
  setJavDb: (value: WebMetadataSettings) => void
  setNotice: (value: string) => void
}) {
  const [ffmpeg, setFfmpeg] = useState<FfmpegToolStatus>()
  const [ffmpegError, setFfmpegError] = useState('')
  const [diagnostics, setDiagnostics] = useState<ProviderDiagnosticResult[]>([])
  const [diagnosticsBusy, setDiagnosticsBusy] = useState(false)
  const [diagnosticsError, setDiagnosticsError] = useState('')
  const network = providerNetwork ?? { proxyMode: 'System', proxyUrl: '', username: '', password: '' }
  const refreshFfmpeg = () => bridge.ffmpegStatus().then(setFfmpeg).catch((reason: Error) => setFfmpegError(reason.message))
  const refreshDiagnostics = useCallback(() => {
    setDiagnosticsBusy(true)
    setDiagnosticsError('')
    bridge.providerDiagnostics()
      .then(results => {
        setDiagnostics(results)
        setNotice('网络来源检测已刷新；同步任务只会调用当前可达的来源。')
      })
      .catch((reason: Error) => setDiagnosticsError(reason.message))
      .finally(() => setDiagnosticsBusy(false))
  }, [setNotice])
  useEffect(() => { void refreshFfmpeg() }, [])
  useEffect(() => { refreshDiagnostics() }, [refreshDiagnostics])
  const groups = [...new Set(snapshot.servers.map(item => item.pluginId || 'legacy'))]
    .map(id => ({ id, servers: snapshot.servers.filter(item => (item.pluginId || 'legacy') === id) }))
  return <Stack spacing={2}>
    <SurfaceSection title="Provider 网络" description="这里只放影响所有同步源的少量网络选项；保存设置后生效，重新检测可立即查看结果。">
      <Stack spacing={1.5}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '180px minmax(0,1fr)' }, gap: 1.5 }}>
          <TextField select size="small" label="代理方式" value={network.proxyMode} onChange={event => setProviderNetwork({ ...network, proxyMode: event.target.value as ProviderNetworkSettings['proxyMode'] })}>
            <MenuItem value="System">系统代理</MenuItem>
            <MenuItem value="Direct">直连</MenuItem>
            <MenuItem value="Manual">手动代理</MenuItem>
          </TextField>
          <TextField size="small" label="手动代理地址" placeholder="http://127.0.0.1:7890" value={network.proxyUrl} disabled={network.proxyMode !== 'Manual'} onChange={event => setProviderNetwork({ ...network, proxyUrl: event.target.value })}/>
        </Box>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3,minmax(0,1fr))' }, gap: 1.5 }}>
          <MirrorField label="JavBus 镜像域名" value={javBus.mirrorUrls} onChange={mirrorUrls => setJavBus({ ...javBus, mirrorUrls })}/>
          <MirrorField label="JavDB 镜像域名" value={javDb.mirrorUrls} onChange={mirrorUrls => setJavDb({ ...javDb, mirrorUrls })}/>
          <MirrorField label="DMM 镜像域名" value={dmm.mirrorUrls} onChange={mirrorUrls => setDmm({ ...dmm, mirrorUrls })}/>
        </Box>
        <Alert severity="info">另一个软件的本地配置可解析出 ProxyConfig、Servers、Cookie 和 Headers；本轮先支持代理与镜像域名，敏感 Cookie/Header 不在界面明文展示。</Alert>
      </Stack>
    </SurfaceSection>
    <ProviderDiagnosticSection
      title="搜索来源诊断"
      description="这里统一展示 MetaTube、DMM、JavDB、JavBus 在当前网络下的最小可达性与搜索链影响。软件启动后会自动检测；同步时只调用当前可达的来源。"
      providers={["MetaTube", "DMM", "JavDB", "JavBus"]}
      diagnostics={diagnostics}
      busy={diagnosticsBusy}
      error={diagnosticsError}
      onRefresh={refreshDiagnostics}
    />
    <ProviderDiagnosticSection
      title="演员信息获取诊断"
      description="这里统一展示 Minnano、Wikipedia JP 在当前网络下的最小可达性与演员资料补全影响。不可达来源会被同步流程跳过。"
      providers={["Minnano", "Wikipedia JP"]}
      diagnostics={diagnostics}
      busy={diagnosticsBusy}
      error={diagnosticsError}
      onRefresh={refreshDiagnostics}
    />
    <SurfaceSection title="FFmpeg 截图工具" description="用于截图、缩略图、预览图、GIF 和视频信息读取；软件默认不内置。">
      <Stack spacing={1.25}>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
          {ffmpeg?.found ? <VerifiedRoundedIcon color="success"/> : <CloudOffRoundedIcon color="disabled"/>}
          <StatusBadge tone={ffmpeg?.found ? 'success' : 'warning'} label={ffmpeg?.found ? '已检测到' : '未检测到'}/>
          {ffmpeg?.version && <Chip size="small" label={ffmpeg.version}/>}
          {ffmpeg?.probeVersion && <Chip size="small" label={ffmpeg.probeVersion}/>}
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{ffmpeg?.message || ffmpegError || '正在检测 FFmpeg...'}</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>请将 ffmpeg.exe 与 ffprobe.exe 复制到：{ffmpeg?.pluginDirectory || 'plugins\\ffmpeg'}。升级软件不会删除该目录。</Typography>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <Button variant="outlined" startIcon={<FolderRoundedIcon/>} onClick={() => ffmpeg && bridge.openDirectory(ffmpeg.pluginDirectory).then(result => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))}>打开插件目录</Button>
          <Button variant="outlined" startIcon={<CloudDownloadRoundedIcon/>} onClick={() => window.open('https://www.gyan.dev/ffmpeg/builds/', '_blank')}>下载</Button>
          <Button variant="outlined" startIcon={<RefreshRoundedIcon/>} onClick={refreshFfmpeg}>刷新检测</Button>
        </Stack>
      </Stack>
    </SurfaceSection>
    <SurfaceSection title="兼容 Provider" description="旧配置中可识别的元数据服务；仅作为兼容状态展示。">
      {groups.length ? <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(240px,1fr))', gap: 1 }}>
        {groups.map(group => <Card key={group.id} variant="outlined"><CardContent sx={{ py: 1.25, '&:last-child': { pb: 1.25 } }}>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
            <ExtensionRoundedIcon color="primary"/>
            <Box sx={{ minWidth: 0, flex: 1 }}><Typography sx={{ fontWeight: 850 }}>{pluginDisplayName(group.id)}</Typography><Typography variant="caption" color="text.secondary">{group.servers.length} 个服务地址</Typography></Box>
            <StatusBadge tone={group.servers.some(server => server.enabled) ? 'success' : 'neutral'} label={group.servers.some(server => server.enabled) ? '已启用' : '未启用'}/>
          </Stack>
        </CardContent></Card>)}
      </Box> : <Alert severity="info">当前没有兼容 Provider 配置。</Alert>}
    </SurfaceSection>
  </Stack>
}

function MirrorField({ label, value, onChange }: { label: string; value?: string[]; onChange: (value: string[]) => void }) {
  return <TextField
    size="small"
    label={label}
    placeholder="https://example.com/, https://example2.com/"
    value={(value ?? []).join(', ')}
    onChange={event => onChange(event.target.value.split(',').map(item => item.trim()).filter(Boolean))}
    helperText="多个地址用英文逗号分隔"
  />
}
function ProviderDiagnosticSection({ title, description, providers, diagnostics, busy, error, onRefresh }: {
  title: string
  description: string
  providers: string[]
  diagnostics: ProviderDiagnosticResult[]
  busy: boolean
  error: string
  onRefresh: () => void
}) {
  return <SurfaceSection title={title} description={description}>
    <Stack spacing={1.5}>
      <Stack direction="row" spacing={1} useFlexGap sx={{ justifyContent: 'space-between', alignItems: 'center', flexWrap: 'wrap' }}>
        <Alert severity="info" sx={{ flex: 1 }}>默认配置已内置；网络正常的来源会参与同步，网络连不上的来源会自动跳过。</Alert>
        <Button variant="outlined" startIcon={<RefreshRoundedIcon/>} disabled={busy} onClick={onRefresh}>{busy ? '检测中...' : '重新检测'}</Button>
      </Stack>
      {error && <Alert severity="warning">{error}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2,minmax(0,1fr))' }, gap: 1.5 }}>
        {providers.map(provider => <ProviderDiagnosticCard key={provider} provider={provider} result={diagnostics.find(item => item.provider === provider)} busy={busy}/>) }
      </Box>
    </Stack>
  </SurfaceSection>
}

function ProviderDiagnosticCard({ provider, result, busy }: { provider: string; result?: ProviderDiagnosticResult; busy: boolean }) {
  const reachable = result?.reachable
  const status = !result && busy ? '检测中' : reachable ? '可达' : result ? '不可达' : '待检测'
  const recommendation = result?.recommendation || '启动后自动检测；同步前会按检测结果过滤来源。'
  const message = result?.message || '尚未返回检测结果。'
  return <Card variant="outlined" sx={{ borderRadius: 2.5 }}>
    <CardContent>
      <Stack spacing={1.1}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
          <Typography sx={{ fontWeight: 900 }}>{provider}</Typography>
          <StatusBadge tone={reachable ? 'success' : result ? 'error' : 'neutral'} label={status}/>
        </Stack>
        <Typography variant="body2" color="text.secondary">影响范围 <Box component="span" sx={{ fontFamily: 'monospace' }}>{result?.scope || '—'}</Box></Typography>
        <Typography variant="body2" color="text.secondary">建议动作　{recommendation}</Typography>
        <Typography variant="caption" color="text.secondary" sx={{
          overflowWrap: 'anywhere',
          display: '-webkit-box',
          WebkitLineClamp: 3,
          WebkitBoxOrient: 'vertical',
          overflow: 'hidden',
        }}>{message}</Typography>
      </Stack>
    </CardContent>
  </Card>
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
  const imageSources: { key: MovieWallDisplaySettings['wallImageSource']; label: string }[] = [
    { key: 'poster', label: '海报' },
    { key: 'thumbnail', label: '缩略图' },
    { key: 'fanart', label: '背景图' },
  ]
  return <SurfaceSection title="影片墙显示" description="统一调整影片墙卡片密度和海报比例，所有复用 MovieWall 的页面同步生效。">
    <Stack spacing={2}>
      <Box>
        <Typography sx={{ fontWeight: 850, mb: 0.5 }}>影片卡片大小</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>影响卡片视图的海报尺寸和每页可见密度。</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3, minmax(0, 1fr))' }, gap: 1.5 }}>
          {sizes.map(option => <VisualOptionCard key={option.key} selected={value.posterSize === option.key} label={option.label} onClick={() => setValue({ ...value, posterSize: option.key })}>
            <PreviewRail><PreviewPoster width={option.width} height={option.height}/></PreviewRail>
          </VisualOptionCard>)}
        </Box>
      </Box>
      <Box>
        <Typography sx={{ fontWeight: 850, mb: 0.5 }}>海报方向</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>只影响卡片视图；列表视图保持现有布局。</Typography>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 1.5 }}>
          {orientations.map(option => <VisualOptionCard key={option.key} selected={value.posterOrientation === option.key} label={option.label} description={option.description} onClick={() => setValue({ ...value, posterOrientation: option.key })}>
            <PreviewRail><PreviewPoster width={option.width} height={option.height}/></PreviewRail>
          </VisualOptionCard>)}
        </Box>
      </Box>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(3,minmax(0,1fr))' }, gap: 1.5 }}>
        <TextField select size="small" label="展示墙图片来源" value={value.wallImageSource} onChange={event => setValue({ ...value, wallImageSource: event.target.value as MovieWallDisplaySettings['wallImageSource'] })}>
          {imageSources.map(option => <MenuItem key={option.key} value={option.key}>{option.label}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="详情页图片来源" value={value.detailImageSource} onChange={event => setValue({ ...value, detailImageSource: event.target.value as MovieWallDisplaySettings['detailImageSource'] })}>
          {imageSources.map(option => <MenuItem key={option.key} value={option.key}>{option.label}</MenuItem>)}
        </TextField>
        <TextField select size="small" label="默认视图" value={value.defaultViewMode} onChange={event => setValue({ ...value, defaultViewMode: event.target.value as MovieWallDisplaySettings['defaultViewMode'] })}>
          <MenuItem value="grid">卡片</MenuItem>
          <MenuItem value="list">列表</MenuItem>
        </TextField>
      </Box>
      <Typography variant="body2" color="text.secondary">默认值：展示墙海报、详情页背景图、卡片视图。</Typography>
    </Stack>
  </SurfaceSection>
}

function VisualOptionCard({ selected, label, description, onClick, children }: { selected: boolean; label: string; description?: string; onClick: () => void; children: ReactNode }) {
  return <ButtonBase onClick={onClick} sx={{ display: 'block', width: '100%', textAlign: 'inherit', borderRadius: 2 }}>
    <Paper variant="outlined" sx={{
      p: 1.5,
      minHeight: 124,
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
function SearchSection({ search, setSearch }: { search: SearchSettings; setSearch: (value: SearchSettings) => void }) {
  return <Stack spacing={2}>
    <SurfaceSection title="搜索与筛选" description="只保留影响影片墙初始体验的正式设置。">
      <Stack spacing={1.5}>
        <TextField select size="small" label="默认排序" value={search.defaultSort} onChange={event => setSearch({ ...search, defaultSort: event.target.value })}>
          <MenuItem value="newest">最近加入</MenuItem>
          <MenuItem value="oldest">最早加入</MenuItem>
          <MenuItem value="release">发行日期</MenuItem>
          <MenuItem value="rating">评分优先</MenuItem>
          <MenuItem value="code">番号</MenuItem>
          <MenuItem value="title">标题</MenuItem>
        </TextField>
        <TextField select size="small" label="默认筛选" value={search.defaultFilter} onChange={() => setSearch({ ...search, defaultFilter: 'all' })}>
          <MenuItem value="all">全部</MenuItem>
        </TextField>
      </Stack>
    </SurfaceSection>
  </Stack>
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
function DataSection({ overview, ratingRetention, setRatingRetention, dataBackup, setDataBackup, backupPath, setBackupPath, restoreMode, setRestoreMode, validation, setValidation, setNotice, onBackup, onRestore }: {
  overview: DataSafetyOverview
  ratingRetention: RatingRetentionSettings
  setRatingRetention: (v: RatingRetentionSettings) => void
  dataBackup: DataBackupSettings
  setDataBackup: (v: DataBackupSettings) => void
  backupPath: string
  setBackupPath: (v: string) => void
  restoreMode: string
  setRestoreMode: (v: string) => void
  validation?: BackupValidation
  setValidation: (v?: BackupValidation) => void
  setNotice: (v: string) => void
  onBackup: () => void
  onRestore: () => void
}) {
  const chooseBackup = async () => {
    try {
      const selected = await invoke<string | null>('choose_file')
      if (!selected) return
      setBackupPath(selected)
      const result = await bridge.validateBackup(selected)
      setValidation(result)
    } catch (reason) {
      setNotice((reason as Error).message)
    }
  }
  return <Stack spacing={2}>
    <SurfaceSection title="自动备份" description="软件退出时判断是否到期，到期后自动备份数据库与配置。">
      <Stack spacing={1.5}>
        <FormControlLabel control={<Switch checked={dataBackup.enabled} onChange={event => setDataBackup({ ...dataBackup, enabled: event.target.checked })}/>} label="启用自动备份"/>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2,minmax(0,1fr))' }, gap: 1.5 }}>
          <TextField select size="small" label="备份频率" value={dataBackup.frequencyDays} onChange={event => setDataBackup({ ...dataBackup, frequencyDays: Number(event.target.value) as DataBackupSettings['frequencyDays'] })}>
            <MenuItem value={1}>每天</MenuItem>
            <MenuItem value={3}>每 3 天</MenuItem>
            <MenuItem value={7}>每 7 天</MenuItem>
          </TextField>
          <TextField select size="small" label="保留数量" value={dataBackup.retentionCount} onChange={event => setDataBackup({ ...dataBackup, retentionCount: Number(event.target.value) as DataBackupSettings['retentionCount'] })}>
            <MenuItem value={5}>5</MenuItem>
            <MenuItem value={10}>10</MenuItem>
            <MenuItem value={20}>20</MenuItem>
          </TextField>
        </Box>
        <Typography variant="body2" color="text.secondary">最近备份：{overview.lastBackupAt ? new Date(overview.lastBackupAt).toLocaleString() : '暂无'}</Typography>
      </Stack>
    </SurfaceSection>
    <SurfaceSection title="备份与恢复" description="立即备份会保存数据库和配置；恢复备份会先生成恢复计划，避免运行中替换数据库。">
      <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
        <Button variant="contained" startIcon={<BackupRoundedIcon/>} onClick={onBackup}>立即备份</Button>
        <Button variant="outlined" onClick={chooseBackup}>选择备份文件</Button>
        <TextField select size="small" label="恢复范围" value={restoreMode} onChange={event => setRestoreMode(event.target.value)} sx={{ width: 140 }}>
          <MenuItem value="all">全部</MenuItem>
          <MenuItem value="database">仅数据库</MenuItem>
          <MenuItem value="settings">仅设置</MenuItem>
        </TextField>
        <Button color="error" variant="outlined" startIcon={<RestoreRoundedIcon/>} disabled={!validation?.valid} onClick={onRestore}>恢复备份</Button>
      </Stack>
      {backupPath && <Typography variant="body2" color="text.secondary" sx={{ mt: 1, overflowWrap: 'anywhere' }}>已选择：{backupPath}</Typography>}
      {validation && <Alert severity={validation.valid ? 'success' : 'error'} sx={{ mt: 1 }}>{validation.valid ? '备份校验通过' : validation.errors.join('；')}</Alert>}
    </SurfaceSection>
    <SurfaceSection title="个人数据保护" description="评分和收藏属于用户个人数据，不会被重新刮削覆盖。">
      <FormControlLabel control={<Switch checked={ratingRetention.enabled} onChange={event => setRatingRetention({ enabled: event.target.checked })}/>} label="自动保留并恢复已删除影片评分"/>
    </SurfaceSection>
  </Stack>
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
    <SurfaceSection title="关于" description="本地优先、可维护的现代媒体管理工具。"><Stack spacing={1}><BrandMark/><Typography>软件版本：{buildInfo.version}</Typography><Typography>构建时间：{buildInfo.buildTime}</Typography><HealthMeter label="数据库状态" value={overview.databaseBytes > 0 ? 100 : 0} detail={health?.writeEnabled ? '正常' : '只读'} tone={health?.writeEnabled ? 'success' : 'warning'}/></Stack></SurfaceSection>
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
  if (value === 'cache') return ['只删除 .lmm-cache 中的生成缓存。', '不删除海报、背景图、预览图。']
  return []
}

function buildMediaStoragePreview(settings: MediaStorageSettings) {
  const movieFolder = renderMediaTemplate(settings.movieFolderTemplate)
  const fileName = renderMediaTemplate(settings.fileNameTemplate)
  const root = settings.rootPath || '<RootPath>'
  const join = (...parts: string[]) => parts.map(part => part.trim().replace(/^\\+|\\+$/g, '')).filter(Boolean).join('\\')
  return [
    { label: '卡图裁切', path: join(root, settings.wallCropsDirectory, movieFolder, `${fileName}.jpg`) },
    { label: '海报', path: join(root, settings.postersDirectory, movieFolder, `${fileName}.jpg`) },
    { label: '缩略图', path: join(root, settings.thumbnailsDirectory, movieFolder, `${fileName}.jpg`) },
    { label: '背景图', path: join(root, settings.fanartDirectory, movieFolder, `${fileName}.jpg`) },
    { label: '预览图', path: join(root, settings.previewsDirectory, movieFolder, `${fileName}.jpg`) },
    { label: '截图', path: join(root, settings.screenshotsDirectory, movieFolder, `${fileName}_001.jpg`) },
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
