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
import { Alert, Box, Button, ButtonBase, Card, CardContent, Chip, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, Divider, FormControlLabel, List, ListItemButton, ListItemText, MenuItem, Paper, Snackbar, Stack, Switch, TextField, Typography } from '@mui/material'
import { invoke } from '@tauri-apps/api/core'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { FormEvent, useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { useBlocker, useNavigate } from 'react-router'
import { BrandMark } from '@/components/BrandMark'
import { ProviderPlayground } from '@/components/ProviderPlayground'
import { HealthMeter, SurfaceSection } from '@/components/ProductComponents'
import { DangerConfirmDialog } from '@/components/workspace/DangerConfirmDialog'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { buildInfo } from '@/buildInfo'
import { defaultMovieWallDisplay, normalizeMovieWallDisplay } from '@/components/workspace/movieWallDisplay'
import { bridge } from '@/services/bridge'
import { testMdcPathMapping } from '@/features/mdcPathMapping'
import { useColorMode } from '@/themes/ThemeContext'
import type { BridgeHealth, TaskItem } from '@/types/media'
import type { BackupValidation, DataBackupSettings, DataSafetyOverview, DiagnosticCheck, FfmpegPluginSettings, FfmpegToolStatus, JavBusSettings, LogCleanupPreview, LogCleanupResult, MdcNgSettings, MdcNgToolStatus, MediaStorageSettings, MetaTubeSettings, MovieWallDisplaySettings, PersonDetectionStatus, PlaybackSettings, ProviderDiagnosticResult, ProviderNetworkSettings, RatingRetentionSettings, RenameSettings, ScanSettings, SearchSettings, SettingsSnapshot, SystemDiagnostic, SystemSettings, UnifiedSettings, UpdateCheckResult, WebMetadataSettings } from '@/types/settings'
import type { ImageCachePreview } from '@/types/media'

const categories = [
  ['general', '常规'], ['appearance', '外观'], ['search', '搜索与筛选'], ['metadata', '元数据'],
  ['plugins', '插件中心'], ['rename', '重命名'], ['mediaStorage', '媒体资源'], ['shortcuts', '快捷键'], ['data', '数据与备份'], ['developer', '开发者'], ['about', '关于'],
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
        {category === 'plugins' && <><MdcPathMappingsSection value={draft.mdcNg} onChange={(value) => updateDraft('mdcNg', value)}/><PluginsSection snapshot={snapshot} mdcNg={draft.mdcNg} setMdcNg={(value) => updateDraft('mdcNg', value)} metaTube={draft.metaTube} setMetaTube={(value) => updateDraft('metaTube', value)} providerNetwork={draft.providerNetwork} setProviderNetwork={(value) => updateDraft('providerNetwork', value)} javBus={draft.javBus} setJavBus={(value) => updateDraft('javBus', value)} dmm={draft.dmm} setDmm={(value) => updateDraft('dmm', value)} javDb={draft.javDb} setJavDb={(value) => updateDraft('javDb', value)} minnano={draft.minnano} setMinnano={(value) => updateDraft('minnano', value)} wikipediaJp={draft.wikipediaJp} setWikipediaJp={(value) => updateDraft('wikipediaJp', value)} setNotice={setNotice}/></>}
        {category === 'rename' && <RenameSettingsSection setNotice={setNotice}/>}
        {category === 'mediaStorage' && <MediaStorageSection mediaStorage={draft.mediaStorage} defaults={defaults.mediaStorage} setMediaStorage={(value) => updateDraft('mediaStorage', value)} setNotice={setNotice}/>}
        {category === 'search' && <SearchSection search={draft.search} setSearch={(value) => updateDraft('search', value)}/>}
        {category === 'shortcuts' && <ShortcutSection system={draft.system} setSystem={(value) => updateDraft('system', value)}/>}
        {category === 'appearance' && <Stack spacing={2}>
          <AppearanceSection mode={draft.appearance.themeMode} setMode={(value) => updateDraft('appearance', { themeMode: value })}/>
          <MovieWallSection value={normalizeMovieWallDisplay(draft.movieWallDisplay ?? defaultMovieWallDisplay)} setValue={(value) => updateDraft('movieWallDisplay', normalizeMovieWallDisplay(value))}/>
        </Stack>}
        {category === 'data' && <DataSection overview={overview} ratingRetention={draft.ratingRetention} setRatingRetention={(value) => updateDraft('ratingRetention', value)} dataBackup={draft.dataBackup} setDataBackup={(value) => updateDraft('dataBackup', value)} backupPath={backupPath} setBackupPath={setBackupPath} restoreMode={restoreMode} setRestoreMode={setRestoreMode} validation={backupValidation} setValidation={setBackupValidation} setNotice={setNotice} onBackup={() => setConfirm('backup')} onRestore={() => setConfirm('restore')}/>}
        {category === 'developer' && <ProviderPlayground/>}
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

function PluginsSection({ snapshot, mdcNg, setMdcNg, metaTube, setMetaTube, providerNetwork, setProviderNetwork, javBus, setJavBus, dmm, setDmm, javDb, setJavDb, minnano, setMinnano, wikipediaJp, setWikipediaJp, setNotice }: {
  snapshot: SettingsSnapshot
  mdcNg: MdcNgSettings
  setMdcNg: (value: MdcNgSettings) => void
  metaTube: MetaTubeSettings
  setMetaTube: (value: MetaTubeSettings) => void
  providerNetwork?: ProviderNetworkSettings
  setProviderNetwork: (value: ProviderNetworkSettings) => void
  javBus: JavBusSettings
  setJavBus: (value: JavBusSettings) => void
  dmm: WebMetadataSettings
  setDmm: (value: WebMetadataSettings) => void
  javDb: WebMetadataSettings
  setJavDb: (value: WebMetadataSettings) => void
  minnano: WebMetadataSettings
  setMinnano: (value: WebMetadataSettings) => void
  wikipediaJp: WebMetadataSettings
  setWikipediaJp: (value: WebMetadataSettings) => void
  setNotice: (value: string) => void
}) {
  const [ffmpeg, setFfmpeg] = useState<FfmpegToolStatus>()
  const [ffmpegError, setFfmpegError] = useState('')
  const [ffmpegSettingsOpen, setFfmpegSettingsOpen] = useState(false)
  const [ffmpegSettings, setFfmpegSettings] = useState<FfmpegPluginSettings>()
  const [personDetection, setPersonDetection] = useState<PersonDetectionStatus>()
  const [mdcNgStatus, setMdcNgStatus] = useState<MdcNgToolStatus>()
  const [mdcNgError, setMdcNgError] = useState('')
  const [diagnostics, setDiagnostics] = useState<ProviderDiagnosticResult[]>([])
  const [diagnosticsBusy, setDiagnosticsBusy] = useState(false)
  const [diagnosticsError, setDiagnosticsError] = useState('')
  const network = providerNetwork ?? { proxyMode: 'System', proxyUrl: '', username: '', password: '' }
  const refreshFfmpeg = () => bridge.ffmpegStatus().then(setFfmpeg).catch((reason: Error) => setFfmpegError(reason.message))
  const openFfmpegSettings = () => Promise.all([bridge.ffmpegSettings(), bridge.personDetectionStatus()])
    .then(([settings, detection]) => { setFfmpegSettings(settings); setPersonDetection(detection); setFfmpegSettingsOpen(true) })
    .catch((reason: Error) => setNotice(reason.message))
  const saveFfmpegSettings = () => ffmpegSettings && bridge.saveFfmpegSettings(ffmpegSettings)
    .then(value => { setFfmpegSettings(value); setFfmpegSettingsOpen(false); setNotice('FFmpeg 插件设置已保存') })
    .catch((reason: Error) => setNotice(reason.message))
  const refreshMdcNg = () => bridge.mdcNgStatus().then(status => { setMdcNgStatus(status); setMdcNgError('') }).catch((reason: Error) => setMdcNgError(reason.message))
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
  useEffect(() => { void refreshFfmpeg(); void refreshMdcNg() }, [])
  useEffect(() => { refreshDiagnostics() }, [refreshDiagnostics])
  const resultFor = (provider: string) => diagnostics.find(item => item.provider === provider)
  const statusFor = (provider: string, configured = true) => {
    if (!configured) return '未配置'
    if (diagnosticsBusy) return '检测中'
    const result = resultFor(provider)
    if (!result) return '未检测'
    if (result.reachable) return '已连接'
    const text = result.message.toLowerCase()
    if (text.includes('timeout')) return '连接超时'
    if (text.includes('auth') || text.includes('401') || text.includes('403')) return '认证失败'
    if (text.includes('json') || text.includes('format')) return '返回异常'
    return '不可用'
  }
  const statusTone = (status: string) => status === '已连接' ? 'success' : status === '检测中' ? 'info' : status === '未检测' || status === '未配置' ? 'neutral' : 'error'
  const updateTestResult = (name: string, work: Promise<{ success: boolean; provider: string; message: string; elapsedMilliseconds: number }>) => {
    work.then(result => {
      setDiagnostics(current => [...current.filter(item => item.provider !== result.provider), {
        provider: result.provider,
        reachable: result.success,
        scope: name === 'JavBus' ? 'title / director / series / tags / cover' : 'movie data / actors / tags / images',
        recommendation: result.success ? '当前无需处理' : '请检查配置、代理和服务状态',
        message: result.message,
        testedAt: new Date().toISOString(),
        elapsedMilliseconds: result.elapsedMilliseconds,
        status: result.success ? (result.provider === 'MDC-NG' ? 'Partial' : 'Available') : 'Unavailable',
        lastSuccessfulAt: result.success ? new Date().toISOString() : undefined,
      }])
      setNotice(result.message)
    }).catch((reason: Error) => setDiagnosticsError(reason.message))
  }
  const mdcServiceUrl = mdcNg.serviceUrl || mdcNgStatus?.serviceUrl || 'http://127.0.0.1:5800/'
  const mdcStatusLabel = mdcNgStatus
    ? mdcNgStatus.serviceReachable ? '已连接' : '未连接'
    : mdcNgError ? '未连接' : '检测中'
  const mdcStatusTone: 'success' | 'info' | 'neutral' | 'error' | 'warning' = mdcStatusLabel === '已连接' ? 'success' : mdcStatusLabel === '检测中' ? 'info' : 'error'
  return <Stack spacing={2}>
    <SurfaceSection title="外部刮削服务" description="同步时按 MDC-NG、MetaTube、JavBus 顺序调用；测试操作不会写入影片数据库。">
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', xl: 'repeat(3,minmax(0,1fr))' }, gap: 1.5 }}>
        <Card variant="outlined" sx={{ borderRadius: 2 }}>
          <CardContent>
            <Stack spacing={1.25}>
              <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
                <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
                  {mdcStatusLabel === '已连接' ? <VerifiedRoundedIcon color="success"/> : <CloudOffRoundedIcon color={mdcStatusLabel === '检测中' ? 'info' : 'disabled'}/>}
                  <StatusBadge tone={mdcStatusTone} label={mdcStatusLabel}/>
                  <Chip size="small" label={mdcNgStatus?.version || '版本未知'}/>
                </Stack>
                <FormControlLabel control={<Switch checked={mdcNg.enabled} onChange={event => setMdcNg({ ...mdcNg, enabled: event.target.checked })}/>} label="用于同步" labelPlacement="start" sx={{ m: 0 }}/>
              </Stack>
              <Box>
                <Typography variant="subtitle2">MDC-NG 外部刮削服务</Typography>
                <Typography variant="body2" color="text.secondary">Docker Web 服务，用于影片元数据刮削。</Typography>
              </Box>
              <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>服务地址：{mdcServiceUrl}</Typography>
              <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
                <Button variant="outlined" onClick={() => window.open('http://127.0.0.1:5800/settings/common', '_blank')}>设置</Button>
                <Button variant="outlined" startIcon={<RefreshRoundedIcon/>} disabled={diagnosticsBusy} onClick={() => { void refreshMdcNg(); refreshDiagnostics() }}>刷新检测</Button>
                <Button variant="outlined" onClick={() => updateTestResult('MDC-NG', bridge.testMdcNg(mdcNg))}>测试刮削</Button>
              </Stack>
            </Stack>
          </CardContent>
        </Card>
        <ScraperServiceCard title="MetaTube 刮削服务" description="作为备用刮削服务，用于影片搜索、演员、标签、封面及图片同步。" status={statusFor('MetaTube', Boolean(metaTube.baseUrl.trim()))} tone={statusTone(statusFor('MetaTube', Boolean(metaTube.baseUrl.trim())))} enabled={metaTube.enabled} onEnabledChange={enabled => setMetaTube({ ...metaTube, enabled })} address={metaTube.baseUrl} version="MetaTube API" auth="无需认证" capabilities="演员 / 标签 / 发行日期 / 时长 / 图片 / 简介" diagnostic={resultFor('MetaTube')} actions={<><Button variant="outlined" onClick={() => setNotice('MetaTube 配置就在当前卡片中，修改后请保存设置。')}>打开配置</Button><Button variant="outlined" onClick={() => window.open(metaTube.baseUrl, '_blank')}>打开服务页面</Button><Button variant="outlined" startIcon={<RefreshRoundedIcon/>} disabled={diagnosticsBusy} onClick={refreshDiagnostics}>刷新检测</Button><Button variant="outlined" onClick={() => updateTestResult('MetaTube', bridge.testMetaTube(metaTube))}>测试刮削</Button></>}>
          <TextField size="small" label="服务地址" value={metaTube.baseUrl} onChange={event => setMetaTube({ ...metaTube, baseUrl: event.target.value })}/>
        </ScraperServiceCard>
        <ScraperServiceCard title="JavBus 在线数据源" description="用于影片标题、导演、系列、标签及封面补充。" status={statusFor('JavBus', Boolean((javBus.baseUrl || 'https://www.javbus.com/').trim()))} tone={statusTone(statusFor('JavBus', Boolean((javBus.baseUrl || 'https://www.javbus.com/').trim())))} enabled={javBus.enabled} onEnabledChange={enabled => setJavBus({ ...javBus, enabled })} address={javBus.baseUrl || 'https://www.javbus.com/'} version={network.proxyMode === 'Manual' ? '手动代理' : network.proxyMode === 'Direct' ? '直连' : '系统代理'} auth={javBus.cookie ? '已配置 Cookie' : '未配置 Cookie'} capabilities="标题 / 导演 / 系列 / 类别 / 标签 / 封面" diagnostic={resultFor('JavBus')} actions={<><Button variant="outlined" onClick={() => setNotice('JavBus 网络配置在下方，修改后请保存设置。')}>网络配置</Button><Button variant="outlined" onClick={() => window.open(javBus.baseUrl || 'https://www.javbus.com/', '_blank')}>打开网站</Button><Button variant="outlined" startIcon={<RefreshRoundedIcon/>} disabled={diagnosticsBusy} onClick={refreshDiagnostics}>刷新检测</Button><Button variant="outlined" onClick={() => updateTestResult('JavBus', bridge.testJavBus(javBus))}>测试搜索</Button></>}>
          <TextField size="small" label="当前域名" value={javBus.baseUrl} placeholder="https://www.javbus.com/" onChange={event => setJavBus({ ...javBus, baseUrl: event.target.value })}/>
          <TextField size="small" type="password" label="Cookie" value={javBus.cookie} autoComplete="off" onChange={event => setJavBus({ ...javBus, cookie: event.target.value })}/>
          <MirrorField label="镜像域名" value={javBus.mirrorUrls} onChange={mirrorUrls => setJavBus({ ...javBus, mirrorUrls })}/>
        </ScraperServiceCard>
      </Box>
    </SurfaceSection>
    <SurfaceSection title="来源网络" description="代理和 JavBus 镜像设置会在保存后用于刮削同步。">
      <Stack spacing={1.5}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '180px minmax(0,1fr)' }, gap: 1.5 }}>
          <TextField select size="small" label="代理模式" value={network.proxyMode} onChange={event => setProviderNetwork({ ...network, proxyMode: event.target.value as ProviderNetworkSettings['proxyMode'] })}>
            <MenuItem value="System">系统代理</MenuItem>
            <MenuItem value="Direct">直连</MenuItem>
            <MenuItem value="Manual">手动代理</MenuItem>
          </TextField>
          <TextField size="small" label="手动代理地址" placeholder="http://127.0.0.1:7890" value={network.proxyUrl} disabled={network.proxyMode !== 'Manual'} onChange={event => setProviderNetwork({ ...network, proxyUrl: event.target.value })}/>
        </Box>
        <Alert severity="info">旧的 DMM、JavDB、Minnano、Wikipedia JP 配置会被安全忽略，不参与影片同步。</Alert>
      </Stack>
    </SurfaceSection>
    {diagnosticsError && <Alert severity="warning">{diagnosticsError}</Alert>}
    <SurfaceSection title="FFmpeg 截图工具" description="用于截图、缩略图、预览图、GIF 和视频信息读取。">
      <Stack spacing={1.25}>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
          {ffmpeg?.found ? <VerifiedRoundedIcon color="success"/> : <CloudOffRoundedIcon color="disabled"/>}
          <StatusBadge tone={ffmpeg?.found ? 'success' : 'warning'} label={ffmpeg?.found ? '已检测到' : '未检测到'}/>
          {ffmpeg?.version && <Chip size="small" label={ffmpeg?.version}/>}
          {ffmpeg?.probeVersion && <Chip size="small" label={ffmpeg?.probeVersion}/>}
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{ffmpeg?.message || ffmpegError || '正在检测 FFmpeg...'}</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>插件目录：{ffmpeg?.pluginDirectory || 'plugins\\ffmpeg'}</Typography>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <Button variant="outlined" startIcon={<FolderRoundedIcon/>} onClick={() => {
            const directory = ffmpeg?.pluginDirectory
            if (!directory) return
            bridge.openDirectory(directory).then(result => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))
          }}>打开插件目录</Button>
          <Button variant="outlined" startIcon={<CloudDownloadRoundedIcon/>} onClick={() => window.open('https://www.gyan.dev/ffmpeg/builds/', '_blank')}>下载</Button>
          <Button variant="outlined" startIcon={<RefreshRoundedIcon/>} onClick={refreshFfmpeg}>重新检测</Button>
          <Button variant="outlined" onClick={openFfmpegSettings}>设置</Button>
        </Stack>
      </Stack>
    </SurfaceSection>
    <FfmpegSettingsDialog open={ffmpegSettingsOpen} value={ffmpegSettings} detection={personDetection}
      onChange={setFfmpegSettings} onClose={() => setFfmpegSettingsOpen(false)} onSave={() => void saveFfmpegSettings()}/>
  </Stack>
  const groups = [...new Set(snapshot.servers.map(item => item.pluginId || 'legacy'))]
    .map(id => ({ id, servers: snapshot.servers.filter(item => (item.pluginId || 'legacy') === id) }))
  return <Stack spacing={2}>
    <SurfaceSection title="同步源开关" description="恢复手动启用/停用 Provider；保存设置后生效。网络诊断仍会自动跳过不可达来源。">
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2,minmax(0,1fr))' }, gap: 1.5 }}>
        <ProviderSwitchCard title="MetaTube" description="影片搜索 / 演员 / 标签 / 图片" checked={metaTube.enabled} onChange={enabled => setMetaTube({ ...metaTube, enabled })}/>
        <ProviderSwitchCard title="DMM" description="影片搜索 / 演员 / 标签 / 封面" checked={dmm.enabled} onChange={enabled => setDmm({ ...dmm, enabled })}/>
        <ProviderSwitchCard title="JavDB" description="番号 / 演员 / 标签搜索" checked={javDb.enabled} onChange={enabled => setJavDb({ ...javDb, enabled })}/>
        <ProviderSwitchCard title="JavBus" description="影片标题 / 演员 / 导演 / 系列 / 标签 / 封面" checked={javBus.enabled} onChange={enabled => setJavBus({ ...javBus, enabled })}/>
        <ProviderSwitchCard title="Minnano" description="演员生日 / 身高 / 罩杯" checked={minnano.enabled} onChange={enabled => setMinnano({ ...minnano, enabled })}/>
        <ProviderSwitchCard title="Wikipedia JP" description="演员生日 / 出生地 / 活动时期 / 简介" checked={wikipediaJp.enabled} onChange={enabled => setWikipediaJp({ ...wikipediaJp, enabled })}/>
      </Box>
    </SurfaceSection>
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
          {ffmpeg?.version && <Chip size="small" label={ffmpeg?.version}/>}
          {ffmpeg?.probeVersion && <Chip size="small" label={ffmpeg?.probeVersion}/>}
        </Stack>
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{ffmpeg?.message || ffmpegError || '正在检测 FFmpeg...'}</Typography>
        <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>请将 ffmpeg.exe 与 ffprobe.exe 复制到：{ffmpeg?.pluginDirectory || 'plugins\\ffmpeg'}。升级软件不会删除该目录。</Typography>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <Button variant="outlined" startIcon={<FolderRoundedIcon/>} onClick={() => {
            const directory = ffmpeg?.pluginDirectory
            if (!directory) return
            bridge.openDirectory(directory).then(result => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))
          }}>打开插件目录</Button>
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

function ProviderSwitchCard({ title, description, checked, onChange }: { title: string; description: string; checked: boolean; onChange: (checked: boolean) => void }) {
  return <Card variant="outlined" sx={{ borderRadius: 2.5 }}>
    <CardContent sx={{ py: 1.5, '&:last-child': { pb: 1.5 } }}>
      <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
        <Box sx={{ minWidth: 0, flex: 1 }}>
          <Typography sx={{ fontWeight: 900 }}>{title}</Typography>
          <Typography variant="body2" color="text.secondary">{description}</Typography>
        </Box>
        <FormControlLabel
          control={<Switch checked={checked} onChange={event => onChange(event.target.checked)}/>}
          label={checked ? '启用' : '停用'}
          labelPlacement="start"
          sx={{ m: 0 }}
        />
      </Stack>
    </CardContent>
  </Card>
}

function ScraperServiceCard({ title, description, status, tone, enabled, onEnabledChange, address, version, auth, capabilities, diagnostic, actions, children }: {
  title: string
  description: string
  status: string
  tone: 'success' | 'info' | 'neutral' | 'error' | 'warning'
  enabled: boolean
  onEnabledChange: (enabled: boolean) => void
  address: string
  version: string
  auth: string
  capabilities: string
  diagnostic?: ProviderDiagnosticResult
  actions: ReactNode
  children?: ReactNode
}) {
  return <SurfaceSection title={title} description={description}>
    <Stack spacing={1.25}>
      <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center', justifyContent: 'space-between' }}>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
          {status === '已连接' || status === 'API 已连接' ? <VerifiedRoundedIcon color="success"/> : <CloudOffRoundedIcon color={status === '检测中' ? 'info' : 'disabled'}/>}
          <StatusBadge tone={tone} label={status}/>
          <Chip size="small" label={version}/>
          <Chip size="small" label={auth}/>
        </Stack>
        <FormControlLabel control={<Switch checked={enabled} onChange={event => onEnabledChange(event.target.checked)}/>} label="用于同步" labelPlacement="start" sx={{ m: 0 }}/>
      </Stack>
      <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>地址：{address}</Typography>
      <Typography variant="body2" color="text.secondary">可用能力：{capabilities}</Typography>
      <Typography variant="body2" color="text.secondary">最近检测：{diagnostic?.testedAt ? new Date(diagnostic.testedAt).toLocaleString() : '未检测'}</Typography>
      <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>{diagnostic?.message || '软件会自动检测来源；同步时只调用已启用且可用的来源。'}</Typography>
      {children && <Stack spacing={1}>{children}</Stack>}
      <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>{actions}</Stack>
    </Stack>
  </SurfaceSection>
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
  const status = !result && busy ? '检测中' : result ? providerReadinessLabel(result.status) : '待检测'
  const recommendation = result?.recommendation || '启动后自动检测；同步前会按检测结果过滤来源。'
  const message = result?.message || '尚未返回检测结果。'
  const tone = result?.status === 'Available' ? 'success' : result?.status === 'Partial' ? 'warning' : result ? 'error' : 'neutral'
  return <Card variant="outlined" sx={{ borderRadius: 2.5 }}>
    <CardContent>
      <Stack spacing={1.1}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
          <Typography sx={{ fontWeight: 900 }}>{provider}</Typography>
          <StatusBadge tone={tone} label={status}/>
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
function MdcPathMappingsSection({ value, onChange }: { value: MdcNgSettings; onChange: (value: MdcNgSettings) => void }) {
  const mappings = value.pathMappings ?? []
  const [testPath, setTestPath] = useState('')
  const [testFileExists, setTestFileExists] = useState<boolean>()
  const testResult = testMdcPathMapping(testPath, mappings)
  const update = (index: number, patch: Partial<(typeof mappings)[number]>) => onChange({ ...value, pathMappings: mappings.map((item, itemIndex) => itemIndex === index ? { ...item, ...patch } : item) })
  const chooseMappingDirectory = async (index: number) => {
    const selected = await invoke<string | null>('choose_directory')
    if (selected) update(index, { localPathPrefix: selected })
  }
  const chooseTestFile = async () => {
    const selected = await invoke<string | null>('choose_file')
    if (!selected) return
    setTestPath(selected)
    setTestFileExists(true)
  }
  return <SurfaceSection title="MDC-NG 路径映射" description="当 MDC-NG 运行在 Docker 中时，需要把本机媒体目录映射为容器内目录。该路径必须与 Docker Volume 配置一致。">
    <Stack spacing={1}>
      {mappings.map((mapping, index) => <Stack key={index} direction={{ xs: 'column', md: 'row' }} spacing={1} sx={{ alignItems: { md: 'center' } }}>
        <TextField size="small" label="Windows 路径" value={mapping.localPathPrefix} onChange={event => update(index, { localPathPrefix: event.target.value })}/>
        <Button variant="outlined" startIcon={<FolderRoundedIcon/>} onClick={() => chooseMappingDirectory(index)}>选择</Button>
        <TextField size="small" label="容器路径" value={mapping.providerPathPrefix} onChange={event => update(index, { providerPathPrefix: event.target.value })}/>
        <FormControlLabel control={<Switch checked={mapping.enabled} onChange={event => update(index, { enabled: event.target.checked })}/>} label="启用"/>
        <Button color="error" onClick={() => onChange({ ...value, pathMappings: mappings.filter((_, itemIndex) => itemIndex !== index).map((item, order) => ({ ...item, order })) })}>删除</Button>
      </Stack>)}
      <Button variant="outlined" onClick={() => onChange({ ...value, pathMappings: [...mappings, { localPathPrefix: '', providerPathPrefix: '/', enabled: true, order: mappings.length }] })}>添加映射</Button>
      <Divider sx={{ my: 1 }}/>
      <Typography sx={{ fontWeight: 850 }}>测试路径映射</Typography>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={1}>
        <TextField fullWidth size="small" label="Windows 文件路径" value={testPath} onChange={event => { setTestPath(event.target.value); setTestFileExists(undefined) }}/>
        <Button variant="outlined" startIcon={<FolderRoundedIcon/>} onClick={chooseTestFile}>选择文件</Button>
        <Button variant="contained" disabled={!testPath.trim()} onClick={() => bridge.pathExists(testPath).then(result => setTestFileExists(result.exists))}>测试</Button>
      </Stack>
      {testPath && <Alert severity={testResult.status === 'matched' ? testFileExists === false ? 'warning' : 'success' : 'warning'}>
        <Stack spacing={.5}>
          <Typography variant="body2">原始路径：{testPath}</Typography>
          <Typography variant="body2">命中规则：{testResult.mapping ? `${testResult.mapping.localPathPrefix} → ${testResult.mapping.providerPathPrefix}` : '未找到映射'}</Typography>
          <Typography variant="body2">转换结果：{testResult.providerPath ?? '—'}</Typography>
          <Typography variant="body2">状态：{testResult.status === 'matched' ? testFileExists === false ? '本地文件不存在' : testFileExists === true ? '映射成功' : '等待本地文件检查' : '未找到映射'}</Typography>
        </Stack>
      </Alert>}
    </Stack>
  </SurfaceSection>
}

function providerReadinessLabel(value: ProviderDiagnosticResult['status']) {
  return ({ Available: '可用', Partial: '部分可用', Unavailable: '不可用', AuthenticationRequired: '需要认证', RateLimited: '已限速', PathMappingMissing: '缺少路径映射', ConfigurationError: '配置错误' } as const)[value] ?? value
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

function FfmpegSettingsDialog({ open, value, detection, onChange, onClose, onSave }: {
  open: boolean
  value?: FfmpegPluginSettings
  detection?: PersonDetectionStatus
  onChange: (value: FfmpegPluginSettings) => void
  onClose: () => void
  onSave: () => void
}) {
  const set = <K extends keyof FfmpegPluginSettings>(key: K, next: FfmpegPluginSettings[K]) => value && onChange({ ...value, [key]: next })
  return <Dialog open={open} onClose={onClose} fullWidth maxWidth="md">
    <DialogTitle>FFmpeg 插件设置</DialogTitle>
    <DialogContent dividers>
      {!value ? <Typography color="text.secondary">正在加载...</Typography> : <Stack spacing={2}>
        <TextField label="FFmpeg.exe 路径" value={value.executablePath} onChange={event => set('executablePath', event.target.value)} helperText="留空时自动检测插件目录和系统 PATH。"/>
        <TextField type="number" label="线程数" value={value.threadCount} onChange={event => set('threadCount', Number(event.target.value))} slotProps={{ htmlInput: { min: 1, max: 64 } }}/>
        <Divider/><Typography variant="subtitle2">截图</Typography>
        <FormControlLabel control={<Switch checked={value.autoScreenshotAfterLocalImport} onChange={event => set('autoScreenshotAfterLocalImport', event.target.checked)}/>} label="导入普通媒体后自动生成截图（当前仅保存配置，尚未接入导入流程）"/>
        <FormControlLabel control={<Switch checked={value.skipWhenScreenshotsExist} onChange={event => set('skipWhenScreenshotsExist', event.target.checked)}/>} label="已有截图时跳过"/>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(3,minmax(0,1fr))' }, gap: 1.5 }}>
          <TextField type="number" label="候选截图数量" value={value.candidateCount} onChange={event => set('candidateCount', Number(event.target.value))} slotProps={{ htmlInput: { min: 6, max: 30 } }}/>
          <TextField type="number" label="最终保留数量" value={value.retainedCount} onChange={event => set('retainedCount', Number(event.target.value))} slotProps={{ htmlInput: { min: 1, max: 30 } }}/>
          <TextField type="number" label="最大截图尝试次数" value={value.maximumAttempts} onChange={event => set('maximumAttempts', Number(event.target.value))} slotProps={{ htmlInput: { min: 6, max: 100 } }}/>
        </Box>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 140px 1fr 140px' }, gap: 1.5 }}>
          <TextField type="number" label="跳过开头" value={value.skipStartValue} onChange={event => set('skipStartValue', Number(event.target.value))}/>
          <TextField select label="单位" value={value.skipStartUnit} onChange={event => set('skipStartUnit', event.target.value as 'Percent' | 'Minutes')}><MenuItem value="Percent">百分比</MenuItem><MenuItem value="Minutes">分钟</MenuItem></TextField>
          <TextField type="number" label="跳过结尾" value={value.skipEndValue} onChange={event => set('skipEndValue', Number(event.target.value))}/>
          <TextField select label="单位" value={value.skipEndUnit} onChange={event => set('skipEndUnit', event.target.value as 'Percent' | 'Minutes')}><MenuItem value="Percent">百分比</MenuItem><MenuItem value="Minutes">分钟</MenuItem></TextField>
        </Box>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <FormControlLabel control={<Switch checked={value.filterBlackFrames} onChange={event => set('filterBlackFrames', event.target.checked)}/>} label="过滤黑屏"/>
          <FormControlLabel control={<Switch checked={value.filterDarkFrames} onChange={event => set('filterDarkFrames', event.target.checked)}/>} label="过滤过暗"/>
          <FormControlLabel control={<Switch checked={value.filterBlurredFrames} onChange={event => set('filterBlurredFrames', event.target.checked)}/>} label="过滤模糊"/>
          <FormControlLabel control={<Switch checked={value.filterDuplicateFrames} onChange={event => set('filterDuplicateFrames', event.target.checked)}/>} label="过滤重复截图"/>
          <FormControlLabel control={<Switch checked={value.filterNoPerson} onChange={event => set('filterNoPerson', event.target.checked)}/>} label="过滤无人场景"/>
        </Stack>
        <Alert severity={detection?.available ? 'success' : 'warning'}>{detection?.available ? '人物检测可用，检测完全在本机执行。' : detection?.unavailableReason || '人物检测不可用，将自动降级为基础画质过滤。'}</Alert>
      </Stack>}
    </DialogContent>
    <DialogActions><Button onClick={onClose}>取消</Button><Button variant="contained" disabled={!value} onClick={onSave}>保存插件设置</Button></DialogActions>
  </Dialog>
}

const renameTokens = ['{VID}', '{Label}', '{ActorNames}', '{Title}', '{VideoType}', '{Year}', '{Runtime}', '{Country}', '{Director}', '{Series}', '{Category}', '{Publisher}', '{Rating}', '{ReleaseDate}']
const renameSeparators = [' - ', '-', '_', ' ', '·', ',', '，']

function RenameSettingsSection({ setNotice }: { setNotice: (value: string) => void }) {
  const [value, setValue] = useState<RenameSettings>()
  const inputRef = useRef<HTMLInputElement>(null)
  useEffect(() => { bridge.renameSettings().then(setValue).catch((reason: Error) => setNotice(reason.message)) }, [setNotice])
  if (!value) return <SurfaceSection title="重命名" description="正在读取重命名设置。"><Typography color="text.secondary">正在加载...</Typography></SurfaceSection>
  const insert = (token: string) => {
    const input = inputRef.current
    const start = input?.selectionStart ?? value.template.length
    const end = input?.selectionEnd ?? start
    const template = value.template.slice(0, start) + token + value.template.slice(end)
    setValue({ ...value, template })
    requestAnimationFrame(() => { input?.focus(); input?.setSelectionRange(start + token.length, start + token.length) })
  }
  const preview = value.template
    .replaceAll('{VID}', 'SONE-454').replaceAll('{Label}', '收藏').replaceAll('{ActorNames}', '演员甲 - 演员乙')
    .replaceAll('{Title}', '示例标题').replaceAll('{VideoType}', 'MP4').replaceAll('{Year}', '2026')
    .replaceAll('{Runtime}', '120min').replaceAll('{Country}', '日本').replaceAll('{Director}', '导演甲')
    .replaceAll('{Series}', '系列甲').replaceAll('{Category}', '类别甲').replaceAll('{Publisher}', '发行商甲')
    .replaceAll('{Rating}', '8.5').replaceAll('{ReleaseDate}', '2026-07-23')
    .replace(/\s*\+\s*/g, value.informationSeparator).replace(/(?:\s+-\s+){2,}/g, value.informationSeparator).trim()
  return <Stack spacing={2}>
    <SurfaceSection title="基础" description="重命名继续使用 Dry Run、冲突预检和确认执行。">
      <Stack spacing={1}>
        <FormControlLabel control={<Switch checked={value.trimTitle} onChange={event => setValue({ ...value, trimTitle: event.target.checked })}/>} label="去除标题两边空格"/>
        <FormControlLabel control={<Switch checked={value.renameAfterFavorite} onChange={event => setValue({ ...value, renameAfterFavorite: event.target.checked })}/>} label="添加已收藏标签后自动重命名"/>
      </Stack>
    </SurfaceSection>
    <SurfaceSection title="规则" description="Standard 的 VID 使用标准大写番号；Local 的 VID 为空并自动清理多余分隔符。">
      <Stack spacing={1.5}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2,minmax(0,1fr))' }, gap: 1.5 }}>
          <TextField select label="信息分隔符" value={value.informationSeparator} onChange={event => setValue({ ...value, informationSeparator: event.target.value })}>{renameSeparators.map(item => <MenuItem key={`info-${item}`} value={item}>{item === ' ' ? '空格' : item}</MenuItem>)}</TextField>
          <TextField select label="标签 / 演员 / 类别分隔符" value={value.listSeparator} onChange={event => setValue({ ...value, listSeparator: event.target.value })}>{renameSeparators.map(item => <MenuItem key={`list-${item}`} value={item}>{item === ' ' ? '空格' : item}</MenuItem>)}</TextField>
        </Box>
        <TextField label="重命名规则" value={value.template} inputRef={inputRef} onChange={event => setValue({ ...value, template: event.target.value })}/>
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>{renameTokens.map(token => <Button size="small" variant="outlined" key={token} onClick={() => insert(token)}>{token}</Button>)}</Stack>
        <TextField label="改名预览" value={`${preview}.mp4`} slotProps={{ input: { readOnly: true } }}/>
        <Box><Button variant="contained" onClick={() => bridge.saveRenameSettings(value).then(saved => { setValue(saved); setNotice('重命名设置已保存') }).catch((reason: Error) => setNotice(reason.message))}>保存重命名设置</Button></Box>
      </Stack>
    </SurfaceSection>
  </Stack>
}
