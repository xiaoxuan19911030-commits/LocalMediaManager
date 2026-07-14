import ActorsRoundedIcon from '@mui/icons-material/GroupsRounded'
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded'
import HomeRoundedIcon from '@mui/icons-material/HomeRounded'
import LocalOfferRoundedIcon from '@mui/icons-material/LocalOfferRounded'
import MovieRoundedIcon from '@mui/icons-material/MovieRounded'
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded'
import TaskRoundedIcon from '@mui/icons-material/TaskRounded'
import { Box, Divider, List, ListItemButton, ListItemIcon, ListItemText, Paper, Typography } from '@mui/material'
import { Outlet, useLocation, useNavigate } from 'react-router'

const items = [
  ['首页', '/', <HomeRoundedIcon />], ['全部影片', '/media', <MovieRoundedIcon />],
  ['媒体库', '/libraries', <FolderRoundedIcon />], ['标签', '/tags', <LocalOfferRoundedIcon />],
  ['演员', '/actors', <ActorsRoundedIcon />], ['收藏', '/favorites', <FavoriteRoundedIcon />],
  ['最近播放', '/history', <HistoryRoundedIcon />], ['任务', '/tasks', <TaskRoundedIcon />],
  ['插件', '/plugins', <ExtensionRoundedIcon />], ['设置', '/settings', <SettingsRoundedIcon />],
] as const

export default function AppShell() {
  const navigate = useNavigate(); const { pathname } = useLocation()
  return <Box sx={{ height: '100vh', display: 'grid', gridTemplateColumns: '208px minmax(0,1fr)', overflow: 'hidden' }}>
    <Paper square elevation={0} sx={{ borderRight: 1, borderColor: 'divider', display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <Box sx={{ px: 2, py: 2.25 }}>
        <Typography sx={{ fontWeight: 800 }}>Local Media Manager</Typography>
        <Typography variant="caption" color="text.secondary">Next · 架构原型</Typography>
      </Box>
      <Divider />
      <List sx={{ px: 1, py: 1, overflowY: 'auto' }}>
        {items.map(([label, path, icon]) => <ListItemButton key={path} selected={pathname === path} onClick={() => navigate(path)} sx={{ borderRadius: 1.5, mb: 0.5 }}>
          <ListItemIcon sx={{ minWidth: 38 }}>{icon}</ListItemIcon><ListItemText primary={label} />
        </ListItemButton>)}
      </List>
      <Box sx={{ mt: 'auto', px: 2, py: 1.5 }}><Typography variant="caption" color="text.secondary">Bridge: 127.0.0.1:47831</Typography></Box>
    </Paper>
    <Box component="main" sx={{ overflowY: 'auto', p: { xs: 2, md: 3 }, minWidth: 0 }}><Outlet /></Box>
  </Box>
}
