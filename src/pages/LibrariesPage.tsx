import AddRoundedIcon from '@mui/icons-material/AddRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import PlaylistAddRoundedIcon from '@mui/icons-material/PlaylistAddRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import StorageRoundedIcon from '@mui/icons-material/StorageRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Autocomplete, Box, Button, Card, CardContent, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, FormControlLabel, IconButton, MenuItem, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { EmptyState, HealthMeter, StatCard } from '@/components/ProductComponents'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { LibraryDeletePreview, LibraryFolderInput, LibraryInput, MediaLibrary } from '@/types/media'

const blankFolder = (): LibraryFolderInput => ({ path: '', includeSubfolders: true, enabled: true, scanMode: 'normal', excludePatterns: [] })
const blankLibrary = (): LibraryInput => ({ name: '', description: '', enabled: true, folders: [blankFolder()] })

export default function LibrariesPage() {
  const [libraries, setLibraries] = useState<MediaLibrary[]>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [editor, setEditor] = useState<{ id?: number; value: LibraryInput }>()
  const [deleting, setDeleting] = useState<LibraryDeletePreview>()
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
    folders: library.folders.map(folder => ({ path: folder.path, enabled: folder.enabled, includeSubfolders: folder.includeSubfolders, scanMode: folder.scanMode as LibraryFolderInput['scanMode'], excludePatterns: folder.excludePatterns || [] })),
  } })
  const updateEditor = (value: Partial<LibraryInput>) => setEditor(current => current ? ({ ...current, value: { ...current.value, ...value } }) : current)
  const updateFolder = (index: number, value: Partial<LibraryFolderInput>) => setEditor(current => current ? ({ ...current, value: { ...current.value, folders: current.value.folders.map((folder, folderIndex) => folderIndex === index ? { ...folder, ...value } : folder) } }) : current)
  const save = async () => {
    if (!editor) return
    setBusy(true); setError(''); setNotice('')
    try { const result = editor.id ? await bridge.updateLibrary(editor.id, editor.value) : await bridge.createLibrary(editor.value); setNotice(result.message); setEditor(undefined); await load() }
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

  const stats = libraries && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(190px,1fr))', gap: 1.5 }}>
    <StatCard label="媒体库" value={libraries.length} icon={<StorageRoundedIcon/>}/>
    <StatCard label="来源文件夹" value={totals.folders} icon={<FolderRoundedIcon/>}/>
    <StatCard label="关联影片" value={totals.movies} icon={<StorageRoundedIcon/>} tone="success.main"/>
    <StatCard label="文件缺失" value={totals.missing} icon={<WarningAmberRoundedIcon/>} tone="warning.main"/>
  </Box>

  return <WorkspacePage title="媒体库" description="管理来源文件夹、排除规则，并通过任务中心执行增量或全量扫描。" stats={stats} loading={!libraries && !error} error={error}
    primaryActions={[{ key: 'new', label: '新建媒体库', icon: <AddRoundedIcon/>, variant: 'contained', onClick: () => setEditor({ value: blankLibrary() }) }, refreshAction(() => void load())]}>
    {notice && <Alert severity="success" onClose={() => setNotice('')} sx={{ mb: 2 }}>{notice}</Alert>}
    {libraries && (libraries.length ? <Stack spacing={1.5}>{libraries.map(library => {
      const health = Math.round((library.movieCount - library.missingCount) / Math.max(1, library.movieCount) * 100)
      return <Card key={library.id}><CardContent>
        <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'start', gap: 2, mb: 2, flexWrap: 'wrap' }}>
          <Box sx={{ display: 'flex', gap: 1.5 }}><Box sx={{ width: 46, height: 46, borderRadius: 2.25, bgcolor: 'action.hover', display: 'grid', placeItems: 'center' }}><StorageRoundedIcon color="primary"/></Box><Box><Typography variant="h6" sx={{ fontWeight: 800 }}>{library.name}</Typography><Typography variant="body2" color="text.secondary">{library.description || '本地媒体库'} · {library.movieCount} 部影片</Typography></Box></Box>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
            <StatusBadge tone={library.enabled ? 'success' : 'neutral'} label={library.enabled ? '已启用' : '已停用'}/>
            <Button size="small" variant="outlined" startIcon={<RefreshRoundedIcon/>} disabled={!library.enabled || scanning !== undefined} onClick={() => void scan(library.id, false)}>增量扫描</Button>
            <Button size="small" variant="outlined" disabled={!library.enabled || scanning !== undefined} onClick={() => void scan(library.id, true)}>全量扫描</Button>
            <Tooltip title="编辑媒体库"><IconButton aria-label="编辑媒体库" onClick={() => openEdit(library)}><EditRoundedIcon/></IconButton></Tooltip>
            <Tooltip title="删除媒体库定义"><IconButton aria-label="删除媒体库定义" color="error" onClick={() => void previewDelete(library.id)}><DeleteOutlineRoundedIcon/></IconButton></Tooltip>
          </Stack>
        </Box>
        <HealthMeter label="文件可用率" value={health} detail={`${library.missingCount} 个缺失`} tone={health > 95 ? 'success' : health > 80 ? 'warning' : 'error'}/>
        <Stack spacing={1} sx={{ mt: 2 }}>{library.folders.map(folder => <Box key={folder.id} sx={{ display: 'grid', gridTemplateColumns: 'auto minmax(0,1fr) auto', alignItems: 'center', gap: 1.5, p: 1.5, borderRadius: 2, bgcolor: 'action.hover' }}>
          <FolderRoundedIcon color="action"/><Box sx={{ minWidth: 0 }}><Typography noWrap title={folder.path}>{folder.path}</Typography><Typography variant="caption" color="text.secondary">{folder.includeSubfolders ? '包含子目录' : '仅当前目录'} · {folder.scanMode} · {folder.excludePatterns.length ? `排除 ${folder.excludePatterns.length} 条规则` : '无排除规则'} · {folder.lastScannedAt ? `上次扫描 ${folder.lastScannedAt.slice(0, 19).replace('T', ' ')}` : '尚未扫描'}</Typography></Box><StatusBadge label={folder.enabled ? '启用' : '停用'} tone={folder.enabled ? 'success' : 'neutral'}/>
        </Box>)}</Stack>
      </CardContent></Card>
    })}</Stack> : <EmptyState title="没有媒体库" description="新建媒体库并添加至少一个绝对路径来源文件夹，即可开始扫描。"/>)}
    <LibraryEditor editor={editor} busy={busy} setEditor={setEditor} updateEditor={updateEditor} updateFolder={updateFolder} save={save}/>
    <Dialog open={Boolean(deleting)} onClose={busy ? undefined : () => setDeleting(undefined)} fullWidth maxWidth="sm">
      <DialogTitle>删除媒体库定义</DialogTitle>
      {deleting && <DialogContent dividers><Alert severity="warning" sx={{ mb: 2 }}>将删除“{deleting.name}”及其 {deleting.folderCount} 个来源定义。</Alert><Stack spacing={1}>{deleting.warnings.map(warning => <Typography key={warning} variant="body2">• {warning}</Typography>)}</Stack></DialogContent>}
      <DialogActions><Button onClick={() => setDeleting(undefined)} disabled={busy}>取消</Button><Button color="error" variant="contained" onClick={() => void confirmDelete()} disabled={busy}>{busy ? '删除中…' : '删除媒体库定义'}</Button></DialogActions>
    </Dialog>
  </WorkspacePage>
}

function LibraryEditor({ editor, busy, setEditor, updateEditor, updateFolder, save }: { editor?: { id?: number; value: LibraryInput }; busy: boolean; setEditor: (value?: { id?: number; value: LibraryInput }) => void; updateEditor: (value: Partial<LibraryInput>) => void; updateFolder: (index: number, value: Partial<LibraryFolderInput>) => void; save: () => void }) {
  return <Dialog open={Boolean(editor)} onClose={busy ? undefined : () => setEditor(undefined)} fullWidth maxWidth="md">
    <DialogTitle>{editor?.id ? '编辑媒体库' : '新建媒体库'}</DialogTitle>
    {editor && <DialogContent dividers><Stack spacing={2}>
      <TextField label="名称" value={editor.value.name} onChange={event => updateEditor({ name: event.target.value })} required fullWidth/>
      <TextField label="说明" value={editor.value.description || ''} onChange={event => updateEditor({ description: event.target.value })} fullWidth/>
      <FormControlLabel control={<Checkbox checked={editor.value.enabled} onChange={event => updateEditor({ enabled: event.target.checked })}/>} label="启用媒体库"/>
      <Typography variant="h6">来源文件夹</Typography>
      {editor.value.folders.map((folder, index) => <Card key={index} variant="outlined" sx={{ p: 2 }}><Stack spacing={1.5}>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(0,1fr) 150px auto' }, gap: 1.5 }}>
          <TextField label="绝对路径" value={folder.path} onChange={event => updateFolder(index, { path: event.target.value })} placeholder="D:\Media" required/>
          <TextField select label="扫描模式" value={folder.scanMode} onChange={event => updateFolder(index, { scanMode: event.target.value as LibraryFolderInput['scanMode'] })}><MenuItem value="normal">普通</MenuItem><MenuItem value="watch">监控</MenuItem><MenuItem value="manual">仅手动</MenuItem></TextField>
          <Tooltip title="删除来源"><span><IconButton aria-label="删除来源文件夹" color="error" disabled={editor.value.folders.length === 1} onClick={() => updateEditor({ folders: editor.value.folders.filter((_, folderIndex) => folderIndex !== index) })}><DeleteOutlineRoundedIcon/></IconButton></span></Tooltip>
        </Box>
        <Autocomplete multiple freeSolo options={[]} value={folder.excludePatterns} onChange={(_, value) => updateFolder(index, { excludePatterns: value })} renderInput={(params) => <TextField {...params} label="排除规则" helperText="例如 sample*、*.txt 或 trailers/*"/>}/>
        <Stack direction="row" spacing={2}><FormControlLabel control={<Checkbox checked={folder.enabled} onChange={event => updateFolder(index, { enabled: event.target.checked })}/>} label="启用来源"/><FormControlLabel control={<Checkbox checked={folder.includeSubfolders} onChange={event => updateFolder(index, { includeSubfolders: event.target.checked })}/>} label="包含子目录"/></Stack>
      </Stack></Card>)}
      <Button variant="outlined" startIcon={<PlaylistAddRoundedIcon/>} onClick={() => updateEditor({ folders: [...editor.value.folders, blankFolder()] })}>添加来源文件夹</Button>
    </Stack></DialogContent>}
    <DialogActions><Button onClick={() => setEditor(undefined)} disabled={busy}>取消</Button><Button variant="contained" onClick={() => void save()} disabled={busy}>{busy ? '保存中…' : '保存'}</Button></DialogActions>
  </Dialog>
}
