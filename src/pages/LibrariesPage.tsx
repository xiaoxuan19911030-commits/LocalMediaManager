import AddRoundedIcon from '@mui/icons-material/AddRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import DeleteSweepRoundedIcon from '@mui/icons-material/DeleteSweepRounded'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import PlaylistAddRoundedIcon from '@mui/icons-material/PlaylistAddRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import StorageRoundedIcon from '@mui/icons-material/StorageRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Autocomplete, Box, Button, Card, CardContent, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, MenuItem, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { invoke } from '@tauri-apps/api/core'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState, HealthMeter, StatCard } from '@/components/ProductComponents'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { LibraryDeletePreview, LibraryFolderInput, LibraryInput, LibraryMissingCleanupPreview, MediaLibrary } from '@/types/media'

const blankFolder = (): LibraryFolderInput => ({ path: '', includeSubfolders: true, enabled: true, scanMode: 'normal', excludePatterns: [] })
const blankLibrary = (): LibraryInput => ({ name: '', description: '', enabled: true, folders: [blankFolder()], libraryType: 'Standard' })
const maxSourceFolders = 3

export default function LibrariesPage() {
  const navigate = useNavigate()
  const [libraries, setLibraries] = useState<MediaLibrary[]>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [editor, setEditor] = useState<{ id?: number; value: LibraryInput }>()
  const [deleting, setDeleting] = useState<LibraryDeletePreview>()
  const [cleaningMissing, setCleaningMissing] = useState<LibraryMissingCleanupPreview>()
  const [busy, setBusy] = useState(false)
  const [scanning, setScanning] = useState<number>()

  const load = useCallback(() => { setError(''); return bridge.libraries().then(setLibraries).catch((reason: Error) => setError(reason.message)) }, [])
  useEffect(() => { void load() }, [load])

  const totals = useMemo(() => ({
    movies: libraries?.reduce((sum, item) => sum + item.movieCount, 0) ?? 0,
    missing: libraries?.reduce((sum, item) => sum + item.missingCount, 0) ?? 0,
    folders: libraries?.reduce((sum, item) => sum + item.folders.length, 0) ?? 0,
  }), [libraries])

  const openEdit = (library: MediaLibrary) => setEditor({ id: library.id, value: {
    name: library.name,
    description: library.description || '',
    enabled: library.enabled,
    libraryType: library.libraryType,
    folders: library.folders.slice(0, maxSourceFolders).map(folder => ({ path: folder.path, enabled: true, includeSubfolders: folder.includeSubfolders, scanMode: 'normal', excludePatterns: folder.excludePatterns || [] })),
  } })
  const updateEditor = (value: Partial<LibraryInput>) => setEditor(current => current ? ({ ...current, value: { ...current.value, ...value } }) : current)
  const updateFolder = (index: number, value: Partial<LibraryFolderInput>) => setEditor(current => current ? ({ ...current, value: { ...current.value, folders: current.value.folders.map((folder, folderIndex) => folderIndex === index ? { ...folder, ...value } : folder) } }) : current)
  const save = async () => {
    if (!editor) return
    setBusy(true); setError(''); setNotice('')
    try {
      const clean = normalizeLibraryInput(editor.value)
      const result = editor.id ? await bridge.updateLibrary(editor.id, clean) : await bridge.createLibrary(clean)
      setNotice(result.message); setEditor(undefined); await load()
    }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }
  const scan = async (libraryId: number, fullScan: boolean) => {
    setScanning(libraryId); setError(''); setNotice('')
    try { const result = await bridge.scanLibrary(libraryId, fullScan, true); setNotice(`${result.message}（任务 #${result.taskId}）`) }
    catch (reason) { setError((reason as Error).message) }
    finally { setScanning(undefined) }
  }
  const previewDelete = async (libraryId: number) => { setError(''); try { setDeleting(await bridge.previewDeleteLibrary(libraryId)) } catch (reason) { setError((reason as Error).message) } }
  const confirmDelete = async () => {
    if (!deleting) return
    setBusy(true); setError('')
    try { const result = await bridge.deleteLibrary(deleting.libraryId, deleting.confirmationToken); setNotice(result.message); setDeleting(undefined); await load() }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }
  const previewMissingCleanup = async (libraryId: number) => {
    setError('')
    try { setCleaningMissing(await bridge.previewLibraryMissingCleanup(libraryId)) }
    catch (reason) { setError((reason as Error).message) }
  }
  const confirmMissingCleanup = async () => {
    if (!cleaningMissing) return
    setBusy(true); setError(''); setNotice('')
    try {
      const result = await bridge.cleanupLibraryMissing(cleaningMissing.libraryId, cleaningMissing.confirmationToken)
      setNotice(result.message); setCleaningMissing(undefined); await load()
    }
    catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const stats = libraries && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(190px,1fr))', gap: 1.5 }}>
    <StatCard label="媒体库" value={libraries.length} icon={<StorageRoundedIcon/>}/>
    <StatCard label="关联影片" value={totals.movies} icon={<StorageRoundedIcon/>} tone="success.main"/>
    <StatCard label="文件缺失" value={totals.missing} icon={<WarningAmberRoundedIcon/>} tone="warning.main"/>
  </Box>

  return <WorkspacePage title="媒体库" description="管理媒体库状态，并通过任务中心执行标准扫描。" stats={stats} loading={!libraries && !error} error={error}
    primaryActions={[{ key: 'new', label: '新建媒体库', icon: <AddRoundedIcon/>, variant: 'contained', onClick: () => setEditor({ value: blankLibrary() }) }, refreshAction(() => void load())]}>
    {notice && <Alert severity="success" onClose={() => setNotice('')} sx={{ mb: 2 }}>{notice}</Alert>}
    {libraries && (libraries.length ? <Stack spacing={1.5}>{libraries.map(library => {
      const health = Math.round((library.movieCount - library.missingCount) / Math.max(1, library.movieCount) * 100)
      return <Card key={library.id}><CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'start', gap: 2, mb: 2, flexWrap: 'wrap' }}>
          <Box sx={{ display: 'flex', gap: 1.5 }}><Box sx={{ width: 46, height: 46, borderRadius: 2.25, bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}><StorageRoundedIcon color="primary"/></Box><Box><Typography variant="h6" sx={{ fontWeight: 800 }}>{library.name}</Typography><Typography variant="body2" color="text.secondary">{library.libraryType === 'Standard' ? '标准影片库' : '普通媒体库'} · {library.description || '本地媒体库'} · {library.movieCount} 部影片</Typography></Box></Box>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
            <StatusBadge tone={library.enabled ? 'success' : 'neutral'} label={library.enabled ? '已启用' : '已停用'}/>
            <Button size="small" variant="outlined" onClick={() => navigate(`/media?libraryId=${library.id}&libraryName=${encodeURIComponent(library.name)}`)}>查看影片</Button>
            <Button size="small" variant="outlined" startIcon={<RefreshRoundedIcon/>} disabled={!library.enabled || scanning !== undefined} onClick={() => void scan(library.id, true)}>扫描影片</Button>
            <Button size="small" variant="outlined" color="warning" startIcon={<DeleteSweepRoundedIcon/>} disabled={library.missingCount === 0 || busy} onClick={() => void previewMissingCleanup(library.id)}>清理缺失记录</Button>
            <Tooltip title="编辑媒体库"><IconButton aria-label="编辑媒体库" onClick={() => openEdit(library)}><EditRoundedIcon/></IconButton></Tooltip>
            <Tooltip title="删除媒体库定义"><IconButton aria-label="删除媒体库定义" color="error" onClick={() => void previewDelete(library.id)}><DeleteOutlineRoundedIcon/></IconButton></Tooltip>
          </Stack>
        </Box>
        <HealthMeter label="文件可用率" value={health} detail={`${library.missingCount} 个缺失`} tone={health > 95 ? 'success' : health > 80 ? 'warning' : 'error'}/>
      </CardContent></Card>
    })}</Stack> : <EmptyState title="没有媒体库" description="新建媒体库并添加至少一个绝对路径来源文件夹，即可开始扫描。"/>)}
    <LibraryEditor editor={editor} busy={busy} setEditor={setEditor} updateEditor={updateEditor} updateFolder={updateFolder} save={save}/>
    <Dialog open={Boolean(deleting)} onClose={busy ? undefined : () => setDeleting(undefined)} fullWidth maxWidth="sm">
      <DialogTitle>删除媒体库定义</DialogTitle>
      {deleting && <DialogContent dividers><Alert severity="warning" sx={{ mb: 2 }}>将删除“{deleting.name}”及其 {deleting.folderCount} 个来源定义。</Alert><Stack spacing={1}>{deleting.warnings.map(warning => <Typography key={warning} variant="body2">• {warning}</Typography>)}</Stack></DialogContent>}
      <DialogActions><Button onClick={() => setDeleting(undefined)} disabled={busy}>取消</Button><Button color="error" variant="contained" onClick={() => void confirmDelete()} disabled={busy}>{busy ? '删除中…' : '删除媒体库定义'}</Button></DialogActions>
    </Dialog>
    <Dialog open={Boolean(cleaningMissing)} onClose={busy ? undefined : () => setCleaningMissing(undefined)} fullWidth maxWidth="sm">
      <DialogTitle>清理缺失文件记录</DialogTitle>
      {cleaningMissing && <DialogContent dividers>
        <Alert severity="warning" sx={{ mb: 2 }}>“{cleaningMissing.name}”中有 {cleaningMissing.missingFileCount} 条缺失文件记录，涉及 {cleaningMissing.affectedMovies} 部影片。</Alert>
        <Stack spacing={1}>{cleaningMissing.warnings.map(warning => <Typography key={warning} variant="body2">• {warning}</Typography>)}</Stack>
      </DialogContent>}
      <DialogActions>
        <Button onClick={() => setCleaningMissing(undefined)} disabled={busy}>取消</Button>
        <Button color="error" variant="contained" onClick={() => void confirmMissingCleanup()} disabled={busy || !cleaningMissing?.missingFileCount}>{busy ? '清理中…' : '确认清理记录'}</Button>
      </DialogActions>
    </Dialog>
  </WorkspacePage>
}

function normalizeLibraryInput(input: LibraryInput): LibraryInput {
  const name = input.name.trim()
  if (!name) throw new Error('媒体库名称不能为空。')
      const folders = input.folders.slice(0, maxSourceFolders)
    .map(folder => ({
      ...folder,
      enabled: true,
      scanMode: 'normal' as const,
      path: folder.path.trim(),
      excludePatterns: (folder.excludePatterns || []).map(pattern => pattern.trim()).filter(Boolean),
    }))
    .filter(folder => folder.path.length > 0)
  if (folders.length === 0) throw new Error('媒体库至少需要一个来源文件夹。')
  const seen = new Set<string>()
  for (const folder of folders) {
    const key = folder.path.replace(/[\\/]+$/g, '').toLocaleLowerCase()
    if (seen.has(key)) throw new Error(`来源文件夹重复：${folder.path}`)
    seen.add(key)
  }
  return { ...input, name, description: input.description?.trim(), folders }
}

function LibraryEditor({ editor, busy, setEditor, updateEditor, updateFolder, save }: { editor?: { id?: number; value: LibraryInput }; busy: boolean; setEditor: (value?: { id?: number; value: LibraryInput }) => void; updateEditor: (value: Partial<LibraryInput>) => void; updateFolder: (index: number, value: Partial<LibraryFolderInput>) => void; save: () => void }) {
  const chooseFolder = async (index?: number) => {
    if (!editor) return
    if (index === undefined && editor.value.folders.length >= maxSourceFolders) {
      window.alert('一个媒体库最多只能添加 3 个来源文件夹。')
      return
    }
    const selected = await invoke<string | null>('choose_directory')
    if (!selected) return
    if (index === undefined) updateEditor({ folders: [...editor.value.folders, { ...blankFolder(), path: selected }] })
    else updateFolder(index, { path: selected })
  }
  return <Dialog open={Boolean(editor)} onClose={busy ? undefined : () => setEditor(undefined)} fullWidth maxWidth="md">
    <DialogTitle>{editor?.id ? '编辑媒体库' : '新建媒体库'}</DialogTitle>
    {editor && <DialogContent dividers><Stack spacing={2}>
      <TextField label="名称" value={editor.value.name} onChange={event => updateEditor({ name: event.target.value })} required fullWidth/>
      <TextField select label="媒体库类型" value={editor.value.libraryType} onChange={event => updateEditor({ libraryType: event.target.value as LibraryInput['libraryType'] })} required fullWidth>
        <MenuItem value="Standard">标准影片库</MenuItem>
        <MenuItem value="Local">普通媒体库</MenuItem>
      </TextField>
      <Alert severity="info">{editor.value.libraryType === 'Standard'
        ? '适用于具有标准番号的影片。支持自动识别番号，并通过 MDC-NG、MetaTube、JavBus 获取标题、演员、标签、封面、预览图和其他元数据。'
        : '适用于国产、欧美、自拍、短视频及其他没有标准番号的本地视频。不进行番号刮削，支持本地封面、标签、收藏、评分、分类和智能搜索；自动视频截图入口已预留。'}</Alert>
      <TextField label="说明" value={editor.value.description || ''} onChange={event => updateEditor({ description: event.target.value })} fullWidth/>
      <FormControlLabel control={<Checkbox checked={editor.value.enabled} onChange={event => updateEditor({ enabled: event.target.checked })}/>} label="启用媒体库"/>
      <Typography variant="h6">来源文件夹</Typography>
      {editor.value.folders.map((folder, index) => <Card key={index} variant="outlined" sx={{ p: 2 }}><Stack spacing={1.5}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(0,1fr) 150px auto' }, gap: 1.5 }}>
          <TextField label="来源文件夹" value={folder.path} slotProps={{ input: { readOnly: true } }} placeholder="点击浏览选择文件夹" required/>
          <Button variant="outlined" onClick={() => void chooseFolder(index)}>浏览...</Button>
          <Tooltip title="删除来源"><span><IconButton aria-label="删除来源文件夹" color="error" disabled={editor.value.folders.length === 1} onClick={() => updateEditor({ folders: editor.value.folders.filter((_, folderIndex) => folderIndex !== index) })}><DeleteOutlineRoundedIcon/></IconButton></span></Tooltip>
        </Box>
        <Autocomplete multiple freeSolo options={[]} value={folder.excludePatterns} onChange={(_, value) => updateFolder(index, { excludePatterns: value })} renderInput={(params) => <TextField {...params} label="排除规则" helperText="例如 sample*、*.txt 或 trailers/*"/>}/>
        <FormControlLabel control={<Checkbox checked={folder.includeSubfolders} onChange={event => updateFolder(index, { includeSubfolders: event.target.checked })}/>} label="包含子目录"/>
      </Stack></Card>)}
      <Button variant="outlined" startIcon={<PlaylistAddRoundedIcon/>} disabled={editor.value.folders.length >= maxSourceFolders} onClick={() => void chooseFolder()}>添加来源</Button>
      {editor.value.folders.length >= maxSourceFolders && <Alert severity="info">一个媒体库最多只能添加 3 个来源文件夹。</Alert>}
    </Stack></DialogContent>}
    <DialogActions><Button onClick={() => setEditor(undefined)} disabled={busy}>取消</Button><Button variant="contained" onClick={() => void save()} disabled={busy}>{busy ? '保存中…' : '保存'}</Button></DialogActions>
  </Dialog>
}
