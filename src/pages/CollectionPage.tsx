import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
import CheckRoundedIcon from '@mui/icons-material/CheckRounded'
import KeyboardArrowRightRoundedIcon from '@mui/icons-material/KeyboardArrowRightRounded'
import { Divider, MenuItem, MenuList, Paper, Snackbar, Stack } from '@mui/material'
import type { MouseEvent } from 'react'
import { useEffect, useRef, useState } from 'react'
import { SafeDeleteDialog } from '@/components/SafeDeleteDialog'
import { MovieWall } from '@/components/workspace/MovieWall'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { bridge } from '@/services/bridge'
import type { AdvancedSearchFilters, MediaItem, SafeDeletePreview, SafeDeletePreviewCommand } from '@/types/media'

const FAVORITES_DEFAULTS = { favorite: true, fileStatus: 'include-missing' } as const
const HISTORY_DEFAULTS = { watched: true, fileStatus: 'include-missing' } as const

export default function CollectionPage({ kind }: { kind: 'favorites' | 'history' }) {
  const favorite = kind === 'favorites'
  const [notice, setNotice] = useState('')
  const [selected, setSelected] = useState<number[]>([])
  const [editMode, setEditMode] = useState(false)
  const [syncAllBusy, setSyncAllBusy] = useState(false)
  const [reloadSignal, setReloadSignal] = useState(0)
  const [contextMenu, setContextMenu] = useState<{ mouseX: number; mouseY: number; item: MediaItem; cropMode: 'AutoFace' | 'Left' | 'Center' | 'Right' }>()
  const contextMenuRef = useRef<HTMLDivElement>(null)
  const cropSubmenuRef = useRef<HTMLDivElement>(null)
  const [cropSubmenuOpen, setCropSubmenuOpen] = useState(false)
  const [deletePreview, setDeletePreview] = useState<SafeDeletePreview>()
  const [deleteCommand, setDeleteCommand] = useState<SafeDeletePreviewCommand>()
  const closeContextMenu = () => { setContextMenu(undefined); setCropSubmenuOpen(false) }
  const openContextMenu = (event: MouseEvent, item: MediaItem) => {
    event.preventDefault()
    setCropSubmenuOpen(false)
    setContextMenu({
      mouseX: Math.min(event.clientX + 2, window.innerWidth - 180),
      mouseY: Math.min(event.clientY - 6, window.innerHeight - 260),
      item,
      cropMode: 'AutoFace',
    })
    bridge.coverCrop(item.dataId).then(crop => setContextMenu(current => current?.item.dataId === item.dataId ? { ...current, cropMode: crop.mode } : current)).catch(() => undefined)
  }
  useEffect(() => {
    if (!contextMenu) return
    const close = () => closeContextMenu()
    const onPointerDown = (event: PointerEvent) => {
      if (event.button === 2) return
      if (contextMenuRef.current?.contains(event.target as Node) || cropSubmenuRef.current?.contains(event.target as Node)) return
      close()
    }
    const onContextMenu = (event: globalThis.MouseEvent) => {
      if (contextMenuRef.current?.contains(event.target as Node) || cropSubmenuRef.current?.contains(event.target as Node)) event.preventDefault()
      else close()
    }
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === 'Escape') close() }
    window.addEventListener('blur', close)
    window.addEventListener('wheel', close, { passive: true })
    window.addEventListener('pointerdown', onPointerDown, true)
    window.addEventListener('contextmenu', onContextMenu, true)
    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('blur', close)
      window.removeEventListener('wheel', close)
      window.removeEventListener('pointerdown', onPointerDown, true)
      window.removeEventListener('contextmenu', onContextMenu, true)
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [contextMenu])
  const refresh = () => setReloadSignal((value) => value + 1)
  const enterEditMode = () => { setEditMode(true); setSelected([]) }
  const exitEditMode = () => { setEditMode(false); setSelected([]); closeContextMenu() }
  const toggleSelect = (item: MediaItem) => setSelected((current) => current.includes(item.dataId) ? current.filter((id) => id !== item.dataId) : [...current, item.dataId])
  const toggleSelectPage = (items: MediaItem[]) => {
    const pageIds = items.map((item) => item.dataId)
    const allSelected = pageIds.length > 0 && pageIds.every((id) => selected.includes(id))
    setSelected((current) => allSelected ? current.filter((id) => !pageIds.includes(id)) : Array.from(new Set([...current, ...pageIds])))
  }
  const syncContextMovie = () => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.syncMovie(item.dataId).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message)) }
  const setContextCropMode = (mode: 'AutoFace' | 'Left' | 'Center' | 'Right') => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.setCoverCrop(item.dataId, mode).then(() => { window.dispatchEvent(new CustomEvent('lmm:cover-crop-updated', { detail: item.dataId })); setNotice('封面裁切已更新'); refresh() }).catch((reason: Error) => setNotice(reason.message)) }
  const generateContextImage = (type: string) => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.generateMovieImage(item.dataId, type).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message)) }
  const openContextLocation = () => { const item = contextMenu?.item; closeContextMenu(); if (item?.path) bridge.revealFile(item.path).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message)); else setNotice('没有可定位的影片文件') }
  const openSafeDelete = (movieIds: number[]) => {
    closeContextMenu()
    const command = { movieIds, mode: 'media', deleteDatabaseInfo: true } satisfies SafeDeletePreviewCommand
    setDeleteCommand(command)
    bridge.previewSafeDelete(command).then(setDeletePreview).catch((reason: Error) => setNotice(reason.message))
  }
  const closeDeleteDialog = () => { setDeletePreview(undefined); setDeleteCommand(undefined) }
  const handleDeleteLaunched = (result: { message: string }) => { setNotice(`${result.message} 可在任务中心查看结果。`); closeDeleteDialog(); refresh() }
  const createFilteredSync = async (filters: AdvancedSearchFilters) => {
    if (syncAllBusy) return
    setSyncAllBusy(true)
    try {
      const preview = await bridge.previewFilteredSync(filters)
      if (preview.count === 0) { setNotice('当前筛选结果没有匹配影片。'); return }
      if (!window.confirm(`将同步当前筛选结果，共 ${preview.count} 部影片。`)) return
      const result = await bridge.createFilteredSync(filters)
      setNotice(result.message)
    } catch (reason) { setNotice((reason as Error).message) }
    finally { setSyncAllBusy(false) }
  }
  const createBatchSync = () => selected.length && bridge.createBatchSync(selected)
    .then((result) => { setNotice(result.message); setSelected([]) })
    .catch((reason: Error) => setNotice(reason.message))
  const createBatchImageTasks = (type: string) => selected.length && Promise.all(selected.map((id) => bridge.generateMovieImage(id, type)))
    .then((results) => { setNotice(`已创建 ${results.length} 个${type === 'GIF' ? ' GIF' : '截图'}任务，可在任务中心查看进度。`); setSelected([]) })
    .catch((reason: Error) => setNotice(reason.message))

  return <>
    <MovieWall title={favorite ? '我的收藏' : '最近播放'}
      description={(total) => favorite ? `共 ${total} 部已收藏影片。` : `共 ${total} 部有播放记录的影片。`}
      stateKey={`lmm.movieWall.${kind}`}
      defaults={favorite ? FAVORITES_DEFAULTS : HISTORY_DEFAULTS}
      defaultLabel={<Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}><StatusBadge tone={favorite ? 'error' : 'success'} label={favorite ? '收藏' : '已观看'}/></Stack>}
      pageSize={48}
      reloadSignal={reloadSignal}
      selectable={editMode} selectedIds={selected}
      onSelect={(item, checked) => setSelected((current) => checked ? Array.from(new Set([...current, item.dataId])) : current.filter((id) => id !== item.dataId))}
      onOpenItem={(item, openDefault) => editMode ? toggleSelect(item) : openDefault()}
      primaryActions={(context) => {
        const { items } = context
        const pageIds = items.map((item) => item.dataId)
        const allSelected = pageIds.length > 0 && pageIds.every((id) => selected.includes(id))
        return !editMode
          ? [
            { key: 'sync-all', label: syncAllBusy ? '同步中' : '同步当前结果', icon: <SyncRoundedIcon/>, variant: 'outlined', disabled: syncAllBusy, onClick: () => { void createFilteredSync(context.filters) } },
            { key: 'edit', label: '编辑', icon: <EditRoundedIcon/>, variant: 'outlined', onClick: enterEditMode },
          ]
          : [
            { key: 'selected-count', label: `已选择 ${selected.length} 部`, variant: 'text', color: 'inherit', disabled: true, onClick: () => undefined },
            { key: 'select-page', label: allSelected ? '取消当前页' : '全选当前页', variant: 'outlined', onClick: () => toggleSelectPage(items) },
            { key: 'cancel-edit', label: '取消', variant: 'outlined', color: 'inherit', onClick: () => setSelected([]) },
            { key: 'done', label: '完成', variant: 'contained', onClick: exitEditMode },
          ]
      }}
      onContextMenu={openContextMenu}
      emptyTitle={favorite ? '暂无收藏' : '暂无播放历史'}
      emptyDescription="数据存在时会通过 Bridge 显示在这里。"/>
    {contextMenu && <Paper ref={contextMenuRef} elevation={8} onContextMenu={(event) => event.preventDefault()} sx={{ position: 'fixed', top: contextMenu.mouseY, left: contextMenu.mouseX, zIndex: (theme) => theme.zIndex.modal, minWidth: 148, py: .5, borderRadius: 1.5, '& .MuiMenuItem-root': { minHeight: 34, py: .75, fontSize: 14 } }}>
      <MenuList dense autoFocusItem={false}>
        {editMode && selected.length > 0 ? [
          <MenuItem key="batch-sync" onClick={() => { closeContextMenu(); void createBatchSync() }}>批量同步信息</MenuItem>,
          <MenuItem key="batch-screenshot" onClick={() => { closeContextMenu(); void createBatchImageTasks('Screenshot') }}>批量生成截图</MenuItem>,
          <MenuItem key="batch-gif" onClick={() => { closeContextMenu(); void createBatchImageTasks('GIF') }}>批量生成 GIF</MenuItem>,
          <MenuItem key="batch-rename" onClick={() => { closeContextMenu(); setNotice('批量重命名请到批量整理流程中执行。') }}>批量重命名</MenuItem>,
          <Divider key="batch-divider"/>,
          <MenuItem key="batch-delete-file" onClick={() => selected.length && openSafeDelete(selected)}>删除影片</MenuItem>,
        ] : [
          <MenuItem key="sync" onClick={syncContextMovie}>同步信息</MenuItem>,
          <Divider key="divider-1"/>,
          <Divider key="crop-divider"/>,
          <MenuItem key="crop" onMouseEnter={() => setCropSubmenuOpen(true)} onClick={() => setCropSubmenuOpen((open) => !open)} aria-haspopup="menu" aria-expanded={cropSubmenuOpen}>裁切封面<KeyboardArrowRightRoundedIcon fontSize="small" sx={{ ml: 'auto' }}/></MenuItem>,
          <MenuItem key="screenshot" onClick={() => generateContextImage('Screenshot')}>生成截图</MenuItem>,
          <MenuItem key="gif" onClick={() => generateContextImage('GIF')}>生成 GIF</MenuItem>,
          <Divider key="divider-2"/>,
          <MenuItem key="location" onClick={openContextLocation}>打开位置</MenuItem>,
          <MenuItem key="delete-file" onClick={() => openSafeDelete([contextMenu.item.dataId])}>删除影片</MenuItem>,
        ]}
      </MenuList>
    </Paper>}
    {contextMenu && cropSubmenuOpen && <Paper ref={cropSubmenuRef} elevation={8} onContextMenu={(event) => event.preventDefault()} onMouseLeave={() => setCropSubmenuOpen(false)} sx={{ position: 'fixed', top: Math.min(contextMenu.mouseY + 68, window.innerHeight - 160), left: Math.min(contextMenu.mouseX + 150, window.innerWidth - 148), zIndex: (theme) => theme.zIndex.modal + 1, minWidth: 148, py: .5, borderRadius: 1.5, '& .MuiMenuItem-root': { minHeight: 34, py: .75, fontSize: 14 } }}>
      <MenuList dense autoFocusItem={false} aria-label="裁切封面">
        {([['AutoFace', '智能识别'], ['Left', '居左裁切'], ['Center', '居中裁切'], ['Right', '居右裁切']] as const).map(([mode, label]) => <MenuItem key={mode} selected={contextMenu.cropMode === mode} onClick={() => setContextCropMode(mode)}>{contextMenu.cropMode === mode ? <CheckRoundedIcon fontSize="small" sx={{ mr: 1 }}/> : <span style={{ width: 20, display: 'inline-block' }}/>} {label}</MenuItem>)}
      </MenuList>
    </Paper>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
    <SafeDeleteDialog preview={deletePreview} command={deleteCommand} onClose={closeDeleteDialog} onLaunched={handleDeleteLaunched}/>
  </>
}
