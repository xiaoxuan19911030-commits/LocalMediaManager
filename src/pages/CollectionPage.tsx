import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import { Divider, ListItemIcon, Menu, MenuItem, Snackbar, Stack } from '@mui/material'
import type { MouseEvent } from 'react'
import { useState } from 'react'
import { useNavigate } from 'react-router'
import { MovieWall } from '@/components/workspace/MovieWall'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { bridge } from '@/services/bridge'
import type { AdvancedSearchFilters, MediaItem } from '@/types/media'

export default function CollectionPage({ kind }: { kind: 'favorites' | 'history' }) {
  const favorite = kind === 'favorites'
  const navigate = useNavigate()
  const [notice, setNotice] = useState('')
  const [syncAllBusy, setSyncAllBusy] = useState(false)
  const [reloadSignal, setReloadSignal] = useState(0)
  const [contextMenu, setContextMenu] = useState<{ mouseX: number; mouseY: number; item: MediaItem }>()
  const [subMenu, setSubMenu] = useState<{ kind: 'image' | 'open'; anchor: HTMLElement }>()
  const closeContextMenu = () => { setContextMenu(undefined); setSubMenu(undefined) }
  const openContextMenu = (event: MouseEvent, item: MediaItem) => { event.preventDefault(); setContextMenu({ mouseX: event.clientX + 2, mouseY: event.clientY - 6, item }) }
  const refresh = () => setReloadSignal((value) => value + 1)
  const syncContextMovie = () => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.syncMovie(item.dataId).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message)) }
  const cropContextImage = () => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.cropMovieCard(item.dataId, { aspectRatio: 16 / 9, anchor: 'center' }).then((result) => { setNotice(result.message); refresh() }).catch((reason: Error) => setNotice(reason.message)) }
  const generateContextImage = (type: string) => { const item = contextMenu?.item; closeContextMenu(); if (item) bridge.generateMovieImage(item.dataId, type).then((result) => setNotice(`${result.message} 可在任务中心查看进度。`)).catch((reason: Error) => setNotice(reason.message)) }
  const openContextLocation = () => { const item = contextMenu?.item; closeContextMenu(); if (item?.path) bridge.revealFile(item.path).then((result) => setNotice(result.message)).catch((reason: Error) => setNotice(reason.message)); else setNotice('没有可定位的影片文件') }
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

  return <>
    <MovieWall title={favorite ? '我的收藏' : '最近播放'}
      description={(total) => favorite ? `共 ${total} 部已收藏影片。` : `共 ${total} 部有播放记录的影片。`}
      stateKey={`lmm.movieWall.${kind}`}
      defaults={favorite ? { favorite: true } : { watched: true }}
      defaultLabel={<Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}><StatusBadge tone={favorite ? 'error' : 'success'} label={favorite ? '收藏' : '已观看'}/></Stack>}
      pageSize={24}
      reloadSignal={reloadSignal}
      primaryActions={favorite ? (context) => [{ key: 'sync-current', label: syncAllBusy ? '同步中' : '同步当前结果', icon: <SyncRoundedIcon/>, variant: 'outlined', disabled: syncAllBusy, onClick: () => { void createFilteredSync(context.filters) } }] : undefined}
      onContextMenu={openContextMenu}
      emptyTitle={favorite ? '暂无收藏' : '暂无播放历史'}
      emptyDescription="数据存在时会通过 Bridge 显示在这里。"/>
    <Menu open={Boolean(contextMenu)} onClose={closeContextMenu} anchorReference="anchorPosition" anchorPosition={contextMenu ? { top: contextMenu.mouseY, left: contextMenu.mouseX } : undefined}>
      <MenuItem onClick={syncContextMovie}><ListItemIcon><SyncRoundedIcon fontSize="small"/></ListItemIcon>同步信息</MenuItem>
      <Divider/>
      <MenuItem onClick={() => { const item = contextMenu?.item; closeContextMenu(); if (item) navigate(`/movies/${item.dataId}`) }}><ListItemIcon><EditRoundedIcon fontSize="small"/></ListItemIcon>编辑信息</MenuItem>
      <MenuItem onMouseEnter={(event) => setSubMenu({ kind: 'image', anchor: event.currentTarget })} onClick={(event) => setSubMenu({ kind: 'image', anchor: event.currentTarget })}>图片</MenuItem>
      <Divider/>
      <MenuItem onMouseEnter={(event) => setSubMenu({ kind: 'open', anchor: event.currentTarget })} onClick={(event) => setSubMenu({ kind: 'open', anchor: event.currentTarget })}><ListItemIcon><FolderRoundedIcon fontSize="small"/></ListItemIcon>打开位置</MenuItem>
    </Menu>
    <Menu open={Boolean(subMenu)} anchorEl={subMenu?.anchor} onClose={() => setSubMenu(undefined)} anchorOrigin={{ vertical: 'top', horizontal: 'right' }} transformOrigin={{ vertical: 'top', horizontal: 'left' }}>
      {subMenu?.kind === 'image' && [<MenuItem key="crop" onClick={cropContextImage}>裁切卡图</MenuItem>, <Divider key="image-divider"/>, <MenuItem key="poster" onClick={() => generateContextImage('Poster')}>生成封面</MenuItem>, <MenuItem key="preview" onClick={() => generateContextImage('Preview')}>生成预览图</MenuItem>, <MenuItem key="screenshot" onClick={() => generateContextImage('Screenshot')}>生成截图</MenuItem>, <MenuItem key="gif" onClick={() => generateContextImage('GIF')}>生成 GIF</MenuItem>]}
      {subMenu?.kind === 'open' && [<MenuItem key="movie" disabled={!contextMenu?.item.path} onClick={openContextLocation}>影片{contextMenu?.item.path ? '' : '（无文件路径）'}</MenuItem>]}
    </Menu>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </>
}
