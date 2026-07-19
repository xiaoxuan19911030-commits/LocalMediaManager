import FavoriteBorderRoundedIcon from '@mui/icons-material/FavoriteBorderRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import StarRoundedIcon from '@mui/icons-material/StarRounded'
import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import { Autocomplete, Button, Dialog, DialogActions, DialogContent, DialogTitle, Divider, ListItemIcon, Menu, MenuItem, Snackbar, Stack, TextField, Typography } from '@mui/material'
import type { MouseEvent } from 'react'
import { useMemo, useState } from 'react'
import { useSearchParams } from 'react-router'
import { SafeDeleteDialog } from '@/components/SafeDeleteDialog'
import { MovieWall, type MovieWallDefaults } from '@/components/workspace/MovieWall'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { bridge } from '@/services/bridge'
import type { MediaItem, NamedItem, SafeDeletePreview, SafeDeletePreviewCommand } from '@/types/media'

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
    const seriesId = numericParam(params, 'seriesId')
    const libraryId = numericParam(params, 'libraryId')
    const label = actorId ? `演员：${params.get('actorName') || actorId}` :
      directorId ? `导演：${params.get('directorName') || directorId}` :
      movieTagId ? `影片标签：${params.get('movieTagName') || movieTagId}` :
      customTagId ? `自定义标签：${params.get('customTagName') || customTagId}` :
      seriesId ? `系列：${params.get('seriesName') || seriesId}` :
      libraryId ? `媒体库：${params.get('libraryName') || libraryId}` : ''
    return { defaults: { actorId, directorId, movieTagId, customTagId, seriesId, libraryId } satisfies MovieWallDefaults, label }
  }, [params])
  const [notice, setNotice] = useState('')
  const [selected, setSelected] = useState<number[]>([])
  const [editMode, setEditMode] = useState(false)
  const [reloadSignal, setReloadSignal] = useState(0)
  const [batchRating, setBatchRating] = useState('5')
  const [ratingDialog, setRatingDialog] = useState(false)
  const [tagDialog, setTagDialog] = useState(false)
  const [tags, setTags] = useState<NamedItem[]>([])
  const [addTags, setAddTags] = useState<NamedItem[]>([])
  const [removeTags, setRemoveTags] = useState<NamedItem[]>([])
  const [contextMenu, setContextMenu] = useState<{ mouseX: number; mouseY: number; item: MediaItem }>()
  const [deletePreview, setDeletePreview] = useState<SafeDeletePreview>()
  const [deleteCommand, setDeleteCommand] = useState<SafeDeletePreviewCommand>()
  const [subMenu, setSubMenu] = useState<{ kind: 'edit' | 'image' | 'open'; anchor: HTMLElement }>()
  const refresh = () => setReloadSignal((value) => value + 1)
  const closeContextMenu = () => { setContextMenu(undefined); setSubMenu(undefined) }
  const openContextMenu = (event: MouseEvent, item: MediaItem) => { event.preventDefault(); setContextMenu({ mouseX: event.clientX + 2, mouseY: event.clientY - 6, item }) }
  const enterEditMode = () => { setEditMode(true); setSelected([]) }
  const exitEditMode = () => { setEditMode(false); setSelected([]); closeContextMenu() }
  const toggleSelect = (item: MediaItem) => setSelected((current) => current.includes(item.dataId) ? current.filter((id) => id !== item.dataId) : [...current, item.dataId])
  const openBatchTags = () => { setAddTags([]); setRemoveTags([]); setTagDialog(true); bridge.entities('tags', '', 'name', 96, 0).then((result) => setTags(result.items)).catch((reason: Error) => setNotice(reason.message)) }
  const batchFavorite = (value: boolean) => bridge.setBatchFavorite(selected, value).then((result) => { setNotice(result.message); setSelected([]); refresh() }).catch((reason: Error) => setNotice(reason.message))
  const saveBatchTags = () => bridge.updateBatchTags(selected, addTags.map((item) => item.id), removeTags.map((item) => item.id)).then((result) => { setNotice(result.message); setTagDialog(false); setSelected([]); refresh() }).catch((reason: Error) => setNotice(reason.message))
  const saveBatchRating = () => bridge.setBatchRating(selected, batchRating === '' ? undefined : Number(batchRating), batchRating === '').then((result) => { setNotice(result.message); setRatingDialog(false); setSelected([]); refresh() }).catch((reason: Error) => setNotice(reason.message))
  const createBatchSync = () => bridge.createBatchSync(selected).then((result) => { setNotice(result.message); setSelected([]) }).catch((reason: Error) => setNotice(reason.message))
  const syncContextMovie = () => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.syncMovie(item.dataId).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message)) }
  const generateContextImage = (type: string) => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.generateMovieImage(item.dataId, type).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message)) }
  const cropContextImage = () => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.cropMovieCard(item.dataId, { aspectRatio: 16 / 9, anchor: 'center' }).then((result) => { setNotice(result.message); refresh() }).catch((reason: Error) => setNotice(reason.message)) }
  const createBatchImageTasks = (type: string) => Promise.all(selected.map((id) => bridge.generateMovieImage(id, type))).then((results) => { setNotice(`已创建 ${results.length} 个${type === 'GIF' ? ' GIF' : '截图'}任务，可在任务中心查看进度。`); setSelected([]) }).catch((reason: Error) => setNotice(reason.message))
  const openContextLocation = () => { const item = contextMenu?.item; closeContextMenu(); if (item?.path) bridge.revealFile(item.path).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message)); else setNotice('没有可定位的影片文件') }
  const openSafeDelete = (movieIds: number[], mode: 'metadata' | 'media') => {
    closeContextMenu()
    const command = { movieIds, mode, deleteDatabaseInfo: true } satisfies SafeDeletePreviewCommand
    setDeleteCommand(command)
    bridge.previewSafeDelete(command).then(setDeletePreview).catch((reason: Error) => setNotice(reason.message))
  }
  const closeDeleteDialog = () => { setDeletePreview(undefined); setDeleteCommand(undefined) }
  const handleDeleteLaunched = (result: { message: string }) => { setNotice(`${result.message} 可在任务中心查看结果。`); closeDeleteDialog(); setSelected([]); refresh() }

  return <>
    <MovieWall title="影片墙" description={(total) => `共 ${total} 部影片，支持搜索、排序、筛选和分页浏览。`} stateKey="lmm.movieWall.media" defaults={category.defaults} defaultLabel={category.label ? <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}><StatusBadge tone="info" label={category.label}/></Stack> : undefined}
      selectable={editMode} selectedIds={selected} onSelect={(item, checked) => setSelected((current) => checked ? [...current, item.dataId] : current.filter((id) => id !== item.dataId))} onContextMenu={openContextMenu} onRatingClick={editMode && selected.length > 0 ? () => setRatingDialog(true) : undefined}
      onOpenItem={(item, openDefault) => editMode ? toggleSelect(item) : openDefault()} reloadSignal={reloadSignal}
      primaryActions={({ items }) => !editMode
        ? [{ key: 'edit', label: '编辑', icon: <EditRoundedIcon/>, variant: 'outlined', onClick: enterEditMode }]
        : [{ key: 'select-page', label: '全选当前页', variant: 'outlined', onClick: () => setSelected(items.map((item) => item.dataId)) }, { key: 'done', label: '完成', variant: 'contained', onClick: exitEditMode }]}
      renderStats={() => editMode && selected.length > 0 && <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', p: 1.25, border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: 'action.hover' }}>
        <Typography sx={{ fontWeight: 800 }}>已选择 {selected.length} 部</Typography>
        <Button size="small" startIcon={<FavoriteRoundedIcon/>} onClick={() => batchFavorite(true)}>收藏</Button>
        <Button size="small" startIcon={<FavoriteBorderRoundedIcon/>} onClick={() => batchFavorite(false)}>取消收藏</Button>
        <Button size="small" onClick={openBatchTags}>批量标签</Button>
        <TextField select size="small" label="评分" value={batchRating} onChange={(event) => setBatchRating(event.target.value)} sx={{ width: 116 }}>
          <MenuItem value="">清除</MenuItem>{[0, 1, 2, 3, 4, 5].map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}
        </TextField>
        <Button size="small" startIcon={<StarRoundedIcon/>} onClick={() => setRatingDialog(true)}>应用评分</Button>
        <Button size="small" startIcon={<SyncRoundedIcon/>} onClick={createBatchSync}>同步信息</Button>
        <Button size="small" color="inherit" onClick={() => setSelected([])}>取消选择</Button>
      </Stack>}
      emptyTitle="暂无影片" emptyDescription="当前媒体库还没有可展示的影片。"/>
    <Menu open={Boolean(contextMenu)} onClose={closeContextMenu} anchorReference="anchorPosition" anchorPosition={contextMenu ? { top: contextMenu.mouseY, left: contextMenu.mouseX } : undefined}>
      {editMode && selected.length > 0 ? [
        <MenuItem key="batch-sync" onClick={() => { closeContextMenu(); void createBatchSync() }}><ListItemIcon><SyncRoundedIcon fontSize="small"/></ListItemIcon>全部同步信息</MenuItem>,
        <Divider key="batch-divider-1"/>,
        <MenuItem key="batch-screenshot" onClick={() => { closeContextMenu(); void createBatchImageTasks('Screenshot') }}>批量生成截图</MenuItem>,
        <MenuItem key="batch-gif" onClick={() => { closeContextMenu(); void createBatchImageTasks('GIF') }}>批量生成 GIF</MenuItem>,
        <Divider key="batch-divider-2"/>,
        <MenuItem key="batch-delete-info" onClick={() => selected.length && openSafeDelete(selected, 'metadata')}>删除信息</MenuItem>,
        <MenuItem key="batch-delete-file" onClick={() => selected.length && openSafeDelete(selected, 'media')}>删除影片</MenuItem>,
      ] : [
        <MenuItem key="sync" onClick={syncContextMovie}><ListItemIcon><SyncRoundedIcon fontSize="small"/></ListItemIcon>同步信息</MenuItem>,
        <Divider key="divider-1"/>,
        <MenuItem key="edit" onMouseEnter={(event) => setSubMenu({ kind: 'edit', anchor: event.currentTarget })} onClick={(event) => setSubMenu({ kind: 'edit', anchor: event.currentTarget })}><ListItemIcon><EditRoundedIcon fontSize="small"/></ListItemIcon>编辑</MenuItem>,
        <Divider key="divider-2"/>,
        <MenuItem key="image" onMouseEnter={(event) => setSubMenu({ kind: 'image', anchor: event.currentTarget })} onClick={(event) => setSubMenu({ kind: 'image', anchor: event.currentTarget })}>图片</MenuItem>,
        <Divider key="divider-3"/>,
        <MenuItem key="location" onMouseEnter={(event) => setSubMenu({ kind: 'open', anchor: event.currentTarget })} onClick={(event) => setSubMenu({ kind: 'open', anchor: event.currentTarget })}><ListItemIcon><FolderRoundedIcon fontSize="small"/></ListItemIcon>打开位置</MenuItem>,
        <Divider key="divider"/>,
        <MenuItem key="delete-info" onClick={() => contextMenu?.item && openSafeDelete([contextMenu.item.dataId], 'metadata')}><ListItemIcon><DeleteOutlineRoundedIcon fontSize="small"/></ListItemIcon>删除信息</MenuItem>,
        <MenuItem key="delete-file" onClick={() => contextMenu?.item && openSafeDelete([contextMenu.item.dataId], 'media')}>删除影片</MenuItem>,
      ]}
    </Menu>
    <Menu open={Boolean(subMenu)} anchorEl={subMenu?.anchor} onClose={() => setSubMenu(undefined)} anchorOrigin={{ vertical: 'top', horizontal: 'right' }} transformOrigin={{ vertical: 'top', horizontal: 'left' }}>
      {subMenu?.kind === 'edit' && [<MenuItem key="info" onClick={() => { const item = contextMenu?.item; closeContextMenu(); if (item) window.location.hash = `/movies/${item.dataId}` }}>编辑信息</MenuItem>]}
      {subMenu?.kind === 'image' && [<MenuItem key="crop" onClick={cropContextImage}>裁切卡图</MenuItem>, <Divider key="image-divider"/>, <MenuItem key="poster" onClick={() => generateContextImage('Poster')}>生成封面</MenuItem>, <MenuItem key="preview" onClick={() => generateContextImage('Preview')}>生成预览图</MenuItem>, <MenuItem key="screenshot" onClick={() => generateContextImage('Screenshot')}>生成截图</MenuItem>, <MenuItem key="gif" onClick={() => generateContextImage('GIF')}>生成 GIF</MenuItem>]}
      {subMenu?.kind === 'open' && [<MenuItem key="movie" disabled={!contextMenu?.item.path} onClick={openContextLocation}>影片{contextMenu?.item.path ? '' : '（无文件路径）'}</MenuItem>]}
    </Menu>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
    <Dialog open={ratingDialog} onClose={() => setRatingDialog(false)} fullWidth maxWidth="xs"><DialogTitle>批量评分</DialogTitle><DialogContent><TextField select fullWidth margin="normal" label="评分" value={batchRating} onChange={(event) => setBatchRating(event.target.value)}><MenuItem value="">清除评分</MenuItem>{[0, 1, 2, 3, 4, 5].map((value) => <MenuItem key={value} value={value}>{value}</MenuItem>)}</TextField></DialogContent><DialogActions><Button onClick={() => setRatingDialog(false)}>取消</Button><Button variant="contained" onClick={saveBatchRating}>应用到 {selected.length} 部影片</Button></DialogActions></Dialog>
    <Dialog open={tagDialog} onClose={() => setTagDialog(false)} fullWidth maxWidth="sm"><DialogTitle>批量编辑标签</DialogTitle><DialogContent><Autocomplete multiple options={tags.filter((tag) => !removeTags.some((item) => item.id === tag.id))} value={addTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setAddTags(value)} renderInput={(params) => <TextField {...params} label="添加标签" margin="normal"/>}/><Autocomplete multiple options={tags.filter((tag) => !addTags.some((item) => item.id === tag.id))} value={removeTags} isOptionEqualToValue={(a, b) => a.id === b.id} getOptionLabel={(option) => option.name} onChange={(_, value) => setRemoveTags(value)} renderInput={(params) => <TextField {...params} label="解绑标签" margin="normal" helperText="只解除关系，不删除标签。"/>}/></DialogContent><DialogActions><Button onClick={() => setTagDialog(false)}>取消</Button><Button variant="contained" disabled={!addTags.length && !removeTags.length} onClick={saveBatchTags}>应用到 {selected.length} 部影片</Button></DialogActions></Dialog>
    <SafeDeleteDialog preview={deletePreview} command={deleteCommand} onClose={closeDeleteDialog} onLaunched={handleDeleteLaunched}/>
  </>
}
