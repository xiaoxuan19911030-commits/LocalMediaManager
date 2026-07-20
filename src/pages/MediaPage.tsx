import EditRoundedIcon from '@mui/icons-material/EditRounded'
import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import { Divider, MenuItem, MenuList, Paper, Snackbar, Stack } from '@mui/material'
import { useEffect, useMemo, useRef, useState, type MouseEvent } from 'react'
import { useSearchParams } from 'react-router'
import { SafeDeleteDialog } from '@/components/SafeDeleteDialog'
import { MovieWall, type MovieWallDefaults } from '@/components/workspace/MovieWall'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { bridge } from '@/services/bridge'
import type { MediaItem, SafeDeletePreview, SafeDeletePreviewCommand } from '@/types/media'

const numericParam = (params: URLSearchParams, name: string) => {
  const value = Number(params.get(name) || 0)
  return Number.isFinite(value) && value > 0 ? value : undefined
}

export default function MediaPage() {
  const [params] = useSearchParams()
  const category = useMemo(() => {
    const actorId = numericParam(params, 'actorId')
    const directorId = numericParam(params, 'directorId')
    const movieTagId = numericParam(params, 'movieTagId')
    const customTagId = numericParam(params, 'customTagId')
    const genreId = numericParam(params, 'genreId')
    const seriesId = numericParam(params, 'seriesId')
    const studioId = numericParam(params, 'studioId')
    const libraryId = numericParam(params, 'libraryId')
    const label = actorId ? `演员：${params.get('actorName') || actorId}` :
      directorId ? `导演：${params.get('directorName') || directorId}` :
      movieTagId ? `影片标签：${params.get('movieTagName') || movieTagId}` :
      customTagId ? `自定义标签：${params.get('customTagName') || customTagId}` :
      genreId ? `标签：${params.get('genreName') || genreId}` :
      seriesId ? `系列：${params.get('seriesName') || seriesId}` :
      studioId ? `厂商：${params.get('studioName') || studioId}` :
      libraryId ? `媒体库：${params.get('libraryName') || libraryId}` : ''
    return { defaults: { actorId, directorId, movieTagId, customTagId, genreId, seriesId, studioId, libraryId } satisfies MovieWallDefaults, label }
  }, [params])
  const [notice, setNotice] = useState('')
  const [selected, setSelected] = useState<number[]>([])
  const [editMode, setEditMode] = useState(false)
  const [syncAllBusy, setSyncAllBusy] = useState(false)
  const [reloadSignal, setReloadSignal] = useState(0)
  const [contextMenu, setContextMenu] = useState<{ mouseX: number; mouseY: number; item: MediaItem }>()
  const contextMenuRef = useRef<HTMLDivElement>(null)
  const [deletePreview, setDeletePreview] = useState<SafeDeletePreview>()
  const [deleteCommand, setDeleteCommand] = useState<SafeDeletePreviewCommand>()
  const refresh = () => setReloadSignal((value) => value + 1)
  const closeContextMenu = () => setContextMenu(undefined)
  const openContextMenu = (event: MouseEvent, item: MediaItem) => {
    event.preventDefault()
    setContextMenu({
      mouseX: Math.min(event.clientX + 2, window.innerWidth - 180),
      mouseY: Math.min(event.clientY - 6, window.innerHeight - 260),
      item,
    })
  }
  useEffect(() => {
    if (!contextMenu) return
    const close = () => closeContextMenu()
    const onPointerDown = (event: PointerEvent) => {
      if (event.button === 2) return
      if (contextMenuRef.current?.contains(event.target as Node)) return
      close()
    }
    const onContextMenu = (event: globalThis.MouseEvent) => {
      if (contextMenuRef.current?.contains(event.target as Node)) event.preventDefault()
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
  const enterEditMode = () => { setEditMode(true); setSelected([]) }
  const exitEditMode = () => { setEditMode(false); setSelected([]); closeContextMenu() }
  const toggleSelect = (item: MediaItem) => setSelected((current) => current.includes(item.dataId) ? current.filter((id) => id !== item.dataId) : [...current, item.dataId])
  const toggleSelectPage = (items: MediaItem[]) => {
    const pageIds = items.map((item) => item.dataId)
    const allSelected = pageIds.length > 0 && pageIds.every((id) => selected.includes(id))
    setSelected((current) => allSelected ? current.filter((id) => !pageIds.includes(id)) : Array.from(new Set([...current, ...pageIds])))
  }
  const createBatchSync = () => selected.length && bridge.createBatchSync(selected)
    .then((result) => { setNotice(result.message); setSelected([]) })
    .catch((reason: Error) => setNotice(reason.message))
  const createLibrarySync = async () => {
    if (syncAllBusy) return
    setSyncAllBusy(true)
    try {
      const batchSize = 500
      let created = 0
      for (let offset = 0; ; offset += batchSize) {
        const page = await bridge.advancedSearch({
          query: '',
          sort: 'newest',
          metadata: 'all',
          fileStatus: 'all',
          metadataStatus: 'all',
          ratingFilter: 'all',
          ratingMin: 0,
          libraryId: category.defaults.libraryId,
          limit: batchSize,
          offset,
        })
        const ids = page.items.map((item) => item.dataId)
        if (ids.length > 0) {
          await bridge.createBatchSync(ids)
          created += ids.length
        }
        if (ids.length < batchSize || offset + batchSize >= page.total) break
      }
      setNotice(`已为库内 ${created} 部影片创建刮削任务，可在任务中心查看进度。`)
    } catch (reason) {
      setNotice((reason as Error).message)
    } finally {
      setSyncAllBusy(false)
    }
  }
  const createBatchImageTasks = (type: string) => selected.length && Promise.all(selected.map((id) => bridge.generateMovieImage(id, type)))
    .then((results) => { setNotice(`已创建 ${results.length} 个${type === 'GIF' ? ' GIF' : '截图'}任务，可在任务中心查看进度。`); setSelected([]) })
    .catch((reason: Error) => setNotice(reason.message))
  const syncContextMovie = () => {
    const item = contextMenu?.item
    closeContextMenu()
    if (item) bridge.syncMovie(item.dataId).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message))
  }
  const generateContextImage = (type: string) => {
    const item = contextMenu?.item
    closeContextMenu()
    if (item) bridge.generateMovieImage(item.dataId, type).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message))
  }
  const cropContextImage = () => {
    const item = contextMenu?.item
    closeContextMenu()
    if (item) bridge.cropMovieCard(item.dataId, { aspectRatio: 16 / 9, anchor: 'center' }).then((result) => { setNotice(result.message); refresh() }).catch((reason: Error) => setNotice(reason.message))
  }
  const openContextLocation = () => {
    const item = contextMenu?.item
    closeContextMenu()
    if (item?.path) bridge.revealFile(item.path).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message))
    else setNotice('没有可定位的影片文件')
  }
  const openSafeDelete = (movieIds: number[]) => {
    closeContextMenu()
    const command = { movieIds, mode: 'media', deleteDatabaseInfo: true } satisfies SafeDeletePreviewCommand
    setDeleteCommand(command)
    bridge.previewSafeDelete(command).then(setDeletePreview).catch((reason: Error) => setNotice(reason.message))
  }
  const closeDeleteDialog = () => { setDeletePreview(undefined); setDeleteCommand(undefined) }
  const handleDeleteLaunched = (result: { message: string }) => { setNotice(`${result.message} 可在任务中心查看结果。`); closeDeleteDialog(); setSelected([]); refresh() }

  return <>
    <MovieWall title="影片墙" description={(total) => `共 ${total} 部影片`} stateKey="lmm.movieWall.media" defaults={category.defaults} defaultLabel={category.label ? <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}><StatusBadge tone="info" label={category.label}/></Stack> : undefined}
      selectable={editMode} selectedIds={selected} onSelect={(item, checked) => setSelected((current) => checked ? Array.from(new Set([...current, item.dataId])) : current.filter((id) => id !== item.dataId))} onContextMenu={openContextMenu}
      onOpenItem={(item, openDefault) => editMode ? toggleSelect(item) : openDefault()} reloadSignal={reloadSignal}
      primaryActions={(context) => {
        const { items } = context
        const pageIds = items.map((item) => item.dataId)
        const allSelected = pageIds.length > 0 && pageIds.every((id) => selected.includes(id))
        return !editMode
          ? [
            { key: 'sync-all', label: syncAllBusy ? '同步中' : '同步所有影片', icon: <SyncRoundedIcon/>, variant: 'outlined', disabled: syncAllBusy, onClick: () => { void createLibrarySync() } },
            { key: 'edit', label: '编辑', icon: <EditRoundedIcon/>, variant: 'outlined', onClick: enterEditMode },
          ]
          : [
            { key: 'selected-count', label: `已选择 ${selected.length} 部`, variant: 'text', color: 'inherit', disabled: true, onClick: () => undefined },
            { key: 'select-page', label: allSelected ? '取消当前页' : '全选当前页', variant: 'outlined', onClick: () => toggleSelectPage(items) },
            { key: 'cancel-edit', label: '取消', variant: 'outlined', color: 'inherit', onClick: () => setSelected([]) },
            { key: 'done', label: '完成', variant: 'contained', onClick: exitEditMode },
          ]
      }}
      emptyTitle="暂无影片" emptyDescription="当前媒体库还没有可展示的影片。"/>
    {contextMenu && <Paper ref={contextMenuRef} elevation={8} onContextMenu={(event) => event.preventDefault()} sx={{ position: 'fixed', top: contextMenu.mouseY, left: contextMenu.mouseX, zIndex: (theme) => theme.zIndex.modal, minWidth: 148, py: .5, borderRadius: 1.5, '& .MuiMenuItem-root': { minHeight: 34, py: 0.75, fontSize: 14 } }}>
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
        <MenuItem key="crop" onClick={cropContextImage}>裁切卡图</MenuItem>,
        <MenuItem key="screenshot" onClick={() => generateContextImage('Screenshot')}>生成截图</MenuItem>,
        <MenuItem key="gif" onClick={() => generateContextImage('GIF')}>生成 GIF</MenuItem>,
        <Divider key="divider-2"/>,
        <MenuItem key="location" onClick={openContextLocation}>打开位置</MenuItem>,
        <MenuItem key="delete-file" onClick={() => contextMenu?.item && openSafeDelete([contextMenu.item.dataId])}>删除影片</MenuItem>,
      ]}
      </MenuList>
    </Paper>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
    <SafeDeleteDialog preview={deletePreview} command={deleteCommand} onClose={closeDeleteDialog} onLaunched={handleDeleteLaunched}/>
  </>
}
