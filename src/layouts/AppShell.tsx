import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded'
import HomeRoundedIcon from '@mui/icons-material/HomeRounded'
import LocalOfferRoundedIcon from '@mui/icons-material/LocalOfferRounded'
import MovieRoundedIcon from '@mui/icons-material/MovieRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded'
import TaskRoundedIcon from '@mui/icons-material/TaskRounded'
import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded'
import BuildRoundedIcon from '@mui/icons-material/BuildRounded'
import { Alert, Box, Divider, InputAdornment, List, ListItemButton, ListItemIcon, ListItemText, Paper, TextField, Typography } from '@mui/material'
import { invoke } from '@tauri-apps/api/core'
import { listen } from '@tauri-apps/api/event'
import { getCurrentWindow } from '@tauri-apps/api/window'
import { FormEvent, ReactNode, useEffect, useRef, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router'
import { BrandMark } from '@/components/BrandMark'
import { shortBuildLabel } from '@/buildInfo'
import { bridge } from '@/services/bridge'
import type { BridgeHealth, TaskItem } from '@/types/media'

type NavItem = readonly [string,string,ReactNode]
const primary:NavItem[]=[['首页','/',<HomeRoundedIcon/>],['影片墙','/media',<MovieRoundedIcon/>],['媒体库','/libraries',<FolderRoundedIcon/>],['标签','/tags',<LocalOfferRoundedIcon/>],['收藏','/favorites',<FavoriteRoundedIcon/>],['最近播放','/history',<HistoryRoundedIcon/>]]
const visibleUtility:NavItem[]=[['数据中心','/data-center',<BuildRoundedIcon/>],['任务中心','/tasks',<TaskRoundedIcon/>],['AI Provider','/ai-providers',<AutoAwesomeRoundedIcon/>],['设置','/settings',<SettingsRoundedIcon/>]]

export default function AppShell(){
  const navigate=useNavigate();const{pathname}=useLocation();const[search,setSearch]=useState('');const[bridgeOnline,setBridgeOnline]=useState<boolean>();const[health,setHealth]=useState<BridgeHealth>()
  const searchRef=useRef<HTMLInputElement|null>(null);const shortcutsEnabled=useRef(true);const minimizedAtStartup=useRef(false);const closeBehavior=useRef<'exit'|'minimizeToTray'>('exit');const pathRef=useRef(pathname)
  useEffect(()=>{let active=true;const check=()=>bridge.health().then((value)=>{if(active){setHealth(value);setBridgeOnline(true)}}).catch(()=>{if(active)setBridgeOnline(false)});void check();const timer=window.setInterval(check,5000);return()=>{active=false;window.clearInterval(timer)}},[])
  useEffect(()=>{pathRef.current=pathname},[pathname])
  useEffect(()=>{bridge.allSettings().then(settings=>{shortcutsEnabled.current=settings.system?.globalShortcutsEnabled??true;closeBehavior.current=settings.system?.closeBehavior??'exit';if(settings.system?.startMinimizedToTray&&!minimizedAtStartup.current){minimizedAtStartup.current=true;void invoke('hide_main_window')}}).catch(()=>undefined)},[])
  useEffect(()=>{let unlistenClose:(()=>void)|undefined;let unlistenTray:(()=>void)|undefined;const quit=async()=>{if(await confirmExitIfTasksRunning()){await runAutoBackupIfDue();void invoke('close_local_media_manager')}};getCurrentWindow().onCloseRequested(async event=>{if(pathRef.current.startsWith('/settings'))return;event.preventDefault();if(closeBehavior.current==='minimizeToTray'){void invoke('hide_main_window');return}await quit()}).then(value=>{unlistenClose=value}).catch(()=>undefined);listen('lmm-tray-quit',()=>{void quit()}).then(value=>{unlistenTray=value}).catch(()=>undefined);return()=>{unlistenClose?.();unlistenTray?.()}},[])
  useEffect(()=>{const handle=(event:KeyboardEvent)=>{if(!shortcutsEnabled.current||event.defaultPrevented||!event.ctrlKey||event.key.toLowerCase()!=='f')return;if(isShortcutBlocked(event))return;event.preventDefault();searchRef.current?.focus();searchRef.current?.select()};window.addEventListener('keydown',handle);return()=>window.removeEventListener('keydown',handle)},[])
  const submit=(event:FormEvent)=>{event.preventDefault();const value=search.trim();if(value)navigate(`/search?q=${encodeURIComponent(value)}`)}
  const nav=(items:NavItem[])=>items.map(([label,path,icon])=><ListItemButton key={path} selected={path==='/'?pathname===path:pathname.startsWith(path)} onClick={()=>navigate(path)} sx={{borderRadius:1.75,mb:.4}}><ListItemIcon sx={{minWidth:38}}>{icon}</ListItemIcon><ListItemText primary={label}/></ListItemButton>)
  return <Box onContextMenu={(event) => event.preventDefault()} sx={{height:'100vh',display:'grid',gridTemplateColumns:'220px minmax(0,1fr)',overflow:'hidden'}}>
    <Paper square elevation={0} sx={{borderRight:1,borderColor:'divider',display:'flex',flexDirection:'column',minHeight:0}}>
      <Box sx={{px:2,py:2}}><BrandMark/></Box><Divider/>
      <Box component="form" onSubmit={submit} sx={{px:1.25,pt:1.25}}><TextField inputRef={searchRef} size="small" fullWidth value={search} onChange={(event)=>setSearch(event.target.value)} placeholder="全局搜索"
        slotProps={{input:{startAdornment:<InputAdornment position="start"><SearchRoundedIcon fontSize="small"/></InputAdornment>}}}/></Box>
      <Box sx={{ flex: 1, minHeight: 0, overflowY: 'auto' }}><List sx={{px:1,py:1}}>{nav(primary)}</List><Divider sx={{mx:1}}/><List sx={{px:1,py:1}}>{nav(visibleUtility)}</List></Box>
      <Box><Divider/><Box sx={{px:2,py:1.25}}><Typography variant="caption" color={bridgeOnline===false?'error.main':'text.secondary'} sx={{display:'block'}}>LMM {shortBuildLabel}</Typography><Typography variant="caption" color={bridgeOnline===false?'error.main':'text.secondary'}>{bridgeOnline===false?'Bridge 已断开':`Bridge ${health?.version ?? 'unknown'} 已连接`}</Typography></Box></Box>
    </Paper>
    <Box component="main" sx={{overflowY:'auto',p:{xs:2,md:3},minWidth:0}}>{bridgeOnline===false&&<Alert severity="error" sx={{mb:2}}>Bridge 已停止响应。请保存当前操作并重新启动 Local Media Manager；未完成的持久任务会在下次启动时恢复为可重试状态。</Alert>}<Outlet/></Box>
  </Box>
}

function isShortcutBlocked(event: KeyboardEvent) {
  if (document.querySelector('.MuiModal-root, .MuiPopover-root')) return true
  const target = event.target
  if (!(target instanceof HTMLElement)) return false
  if (target.isContentEditable) return true
  const tag = target.tagName.toLowerCase()
  return tag === 'input' || tag === 'textarea' || tag === 'select' || Boolean(target.closest('[contenteditable="true"], input, textarea, select'))
}

async function confirmExitIfTasksRunning() {
  let running: TaskItem[] = []
  try {
    running = (await bridge.tasks(100)).filter(task => ['Pending', 'Running', 'Paused'].includes(task.status))
  } catch {
    return true
  }
  if (running.length === 0) return true
  return window.confirm(`仍有 ${running.length} 个任务未完成，退出后会中断当前任务。确定退出吗？`)
}

async function runAutoBackupIfDue() {
  try {
    const [settings, overview] = await Promise.all([bridge.allSettings(), bridge.dataSafetyOverview()])
    if (!settings.dataBackup?.enabled) return
    const last = overview.lastBackupAt ? new Date(overview.lastBackupAt).getTime() : 0
    const interval = Math.max(1, settings.dataBackup.frequencyDays) * 24 * 60 * 60 * 1000
    if (!last || Date.now() - last >= interval) {
      await bridge.createBackup({ includeConfig: true, includeGeneratedCache: false })
    }
  } catch {
    // 退出备份失败不能阻塞用户关闭应用；失败详情保留在 Bridge 日志中。
  }
}
