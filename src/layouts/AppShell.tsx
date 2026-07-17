import ActorsRoundedIcon from '@mui/icons-material/GroupsRounded'
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded'
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
import FactCheckRoundedIcon from '@mui/icons-material/FactCheckRounded'
import TroubleshootRoundedIcon from '@mui/icons-material/TroubleshootRounded'
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import { Alert, Box, Divider, InputAdornment, List, ListItemButton, ListItemIcon, ListItemText, Paper, TextField, Typography } from '@mui/material'
import { FormEvent, ReactNode, useEffect, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router'
import { BrandMark } from '@/components/BrandMark'
import { bridge } from '@/services/bridge'

type NavItem = readonly [string,string,ReactNode]
const primary:NavItem[]=[['首页','/',<HomeRoundedIcon/>],['影片墙','/media',<MovieRoundedIcon/>],['媒体库','/libraries',<FolderRoundedIcon/>],['标签','/tags',<LocalOfferRoundedIcon/>],['演员','/actors',<ActorsRoundedIcon/>],['收藏','/favorites',<FavoriteRoundedIcon/>],['最近播放','/history',<HistoryRoundedIcon/>]]
const utility:NavItem[]=[['元数据中心','/metadata',<FactCheckRoundedIcon/>],['诊断中心','/diagnostics',<TroubleshootRoundedIcon/>],['查重结果','/duplicates',<ContentCopyRoundedIcon/>],['任务中心','/tasks',<TaskRoundedIcon/>],['插件中心','/plugins',<ExtensionRoundedIcon/>],['AI Provider','/ai-providers',<AutoAwesomeRoundedIcon/>],['设置','/settings',<SettingsRoundedIcon/>]]

export default function AppShell(){
  const navigate=useNavigate();const{pathname}=useLocation();const[search,setSearch]=useState('');const[bridgeOnline,setBridgeOnline]=useState<boolean>()
  useEffect(()=>{let active=true;const check=()=>bridge.health().then(()=>{if(active)setBridgeOnline(true)}).catch(()=>{if(active)setBridgeOnline(false)});void check();const timer=window.setInterval(check,5000);return()=>{active=false;window.clearInterval(timer)}},[])
  const submit=(event:FormEvent)=>{event.preventDefault();const value=search.trim();if(value)navigate(`/search?q=${encodeURIComponent(value)}`)}
  const nav=(items:NavItem[])=>items.map(([label,path,icon])=><ListItemButton key={path} selected={path==='/'?pathname===path:pathname.startsWith(path)} onClick={()=>navigate(path)} sx={{borderRadius:1.75,mb:.4}}><ListItemIcon sx={{minWidth:38}}>{icon}</ListItemIcon><ListItemText primary={label}/></ListItemButton>)
  return <Box sx={{height:'100vh',display:'grid',gridTemplateColumns:'220px minmax(0,1fr)',overflow:'hidden'}}>
    <Paper square elevation={0} sx={{borderRight:1,borderColor:'divider',display:'flex',flexDirection:'column',minHeight:0}}>
      <Box sx={{px:2,py:2}}><BrandMark/></Box><Divider/>
      <Box component="form" onSubmit={submit} sx={{px:1.25,pt:1.25}}><TextField size="small" fullWidth value={search} onChange={(event)=>setSearch(event.target.value)} placeholder="全局搜索"
        slotProps={{input:{startAdornment:<InputAdornment position="start"><SearchRoundedIcon fontSize="small"/></InputAdornment>}}}/></Box>
      <Box sx={{ flex: 1, minHeight: 0, overflowY: 'auto' }}><List sx={{px:1,py:1}}>{nav(primary)}</List><Divider sx={{mx:1}}/><List sx={{px:1,py:1}}>{nav(utility)}</List></Box>
      <Box><Divider/><Box sx={{px:2,py:1.25}}><Typography variant="caption" color={bridgeOnline===false?'error.main':'text.secondary'}>LMM 0.4.3 · {bridgeOnline===false?'Bridge 已断开':'Bridge 已连接'}</Typography></Box></Box>
    </Paper>
    <Box component="main" sx={{overflowY:'auto',p:{xs:2,md:3},minWidth:0}}>{bridgeOnline===false&&<Alert severity="error" sx={{mb:2}}>Bridge 已停止响应。请保存当前操作并重新启动 Local Media Manager；未完成的持久任务会在下次启动时恢复为可重试状态。</Alert>}<Outlet/></Box>
  </Box>
}
