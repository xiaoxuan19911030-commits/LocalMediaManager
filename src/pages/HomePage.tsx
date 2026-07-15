import BrokenImageRoundedIcon from '@mui/icons-material/BrokenImageRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import MovieRoundedIcon from '@mui/icons-material/MovieRounded'
import PlayCircleRoundedIcon from '@mui/icons-material/PlayCircleRounded'
import TaskAltRoundedIcon from '@mui/icons-material/TaskAltRounded'
import { Alert, Box, Button, CircularProgress, Snackbar } from '@mui/material'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { MediaCard } from '@/components/MediaCard'
import { PageHeader } from '@/components/PageHeader'
import { SectionTitle, StatCard } from '@/components/ProductComponents'
import { bridge } from '@/services/bridge'
import type { DashboardSummary, MediaItem } from '@/types/media'

export default function HomePage() {
  const [dashboard, setDashboard] = useState<DashboardSummary>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const navigate = useNavigate()
  useEffect(() => { bridge.dashboard().then(setDashboard).catch((reason: Error) => setError(reason.message)) }, [])
  const play = (item: MediaItem) => bridge.play(item.dataId).then(() => setNotice(`已交给系统播放器：${item.code}`)).catch((reason: Error) => setNotice(reason.message))
  const wall = (items: MediaItem[]) => <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(138px, 1fr))', gap: 1.5 }}>
    {items.map((item) => <MediaCard key={item.dataId} item={item} onPlay={play} onOpen={() => navigate(`/movies/${item.dataId}`)}/>)}</Box>
  return <Box>
    <PageHeader title="首页" description="你的本地媒体库概览、最近导入和播放活动。" action={<Button variant="contained" onClick={() => navigate('/media')}>打开影片墙</Button>}/>
    {error && <Alert severity="error">Dashboard 读取失败：{error}</Alert>}
    {!dashboard && !error ? <Box sx={{ minHeight: 320, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : dashboard && <>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2,minmax(0,1fr))', xl: 'repeat(6,minmax(0,1fr))' }, gap: 1.5, mb: 3 }}>
        <StatCard label="全部影片" value={dashboard.movieCount} icon={<MovieRoundedIcon/>}/>
        <StatCard label="我的收藏" value={dashboard.favoriteCount} icon={<FavoriteRoundedIcon/>} tone="error.main"/>
        <StatCard label="播放过" value={dashboard.playedCount} icon={<PlayCircleRoundedIcon/>} tone="success.main"/>
        <StatCard label="媒体库" value={dashboard.libraryCount} icon={<FolderRoundedIcon/>} tone="warning.main"/>
        <StatCard label="缺失文件" value={dashboard.missingFileCount} icon={<BrokenImageRoundedIcon/>} tone="error.main"/>
        <StatCard label="进行中任务" value={dashboard.activeTaskCount} icon={<TaskAltRoundedIcon/>} tone="info.main"/>
      </Box>
      <SectionTitle title="最近导入" description="新加入媒体库的影片" action={<Button size="small" onClick={() => navigate('/media')}>查看全部</Button>}/>
      {wall(dashboard.recentImports)}
      {dashboard.recentPlays.length > 0 && <Box sx={{ mt: 3 }}><SectionTitle title="最近播放" description="继续浏览近期观看内容"/>{wall(dashboard.recentPlays)}</Box>}
    </>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
