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
import { Box, Divider, InputAdornment, List, ListItemButton, ListItemIcon, ListItemText, Paper, TextField, Typography } from '@mui/material'
import { FormEvent, ReactNode, useState } from 'react'
import { Outlet, useLocation, useNavigate } from 'react-router'
import { BrandMark } from '@/components/BrandMark'

type NavItem = readonly [string,string,ReactNode]
const primary:NavItem[]=[['首页','/',<HomeRoundedIcon/>],['影片墙','/media',<MovieRoundedIcon/>],['媒体库','/libraries',<FolderRoundedIcon/>],['标签','/tags',<LocalOfferRoundedIcon/>],['演员','/actors',<ActorsRoundedIcon/>],['收藏','/favorites',<FavoriteRoundedIcon/>],['最近播放','/history',<HistoryRoundedIcon/>]]
const utility:NavItem[]=[['任务中心','/tasks',<TaskRoundedIcon/>],['插件','/plugins',<ExtensionRoundedIcon/>],['设置','/settings',<SettingsRoundedIcon/>]]

export default function AppShell(){
  const navigate=useNavigate();const{pathname}=useLocation();const[search,setSearch]=useState('')
  const submit=(event:FormEvent)=>{event.preventDefault();const value=search.trim();if(value)navigate(`/search?q=${encodeURIComponent(value)}`)}
  const nav=(items:NavItem[])=>items.map(([label,path,icon])=><ListItemButton key={path} selected={path==='/'?pathname===path:pathname.startsWith(path)} onClick={()=>navigate(path)} sx={{borderRadius:1.75,mb:.4}}><ListItemIcon sx={{minWidth:38}}>{icon}</ListItemIcon><ListItemText primary={label}/></ListItemButton>)
  return <Box sx={{height:'100vh',display:'grid',gridTemplateColumns:'220px minmax(0,1fr)',overflow:'hidden'}}>
    <Paper square elevation={0} sx={{borderRight:1,borderColor:'divider',display:'flex',flexDirection:'column',minHeight:0}}>
      <Box sx={{px:2,py:2}}><BrandMark/></Box><Divider/>
      <Box component="form" onSubmit={submit} sx={{px:1.25,pt:1.25}}><TextField size="small" fullWidth value={search} onChange={(event)=>setSearch(event.target.value)} placeholder="全局搜索"
        slotProps={{input:{startAdornment:<InputAdornment position="start"><SearchRoundedIcon fontSize="small"/></InputAdornment>}}}/></Box>
      <List sx={{px:1,py:1,overflowY:'auto'}}>{nav(primary)}</List>
      <Box sx={{mt:'auto'}}><Divider/><List sx={{px:1,py:1}}>{nav(utility)}</List><Box sx={{px:2,py:1.25}}><Typography variant="caption" color="text.secondary">LMM 0.4.0 · Bridge 已连接</Typography></Box></Box>
    </Paper>
    <Box component="main" sx={{overflowY:'auto',p:{xs:2,md:3},minWidth:0}}><Outlet/></Box>
  </Box>
}
