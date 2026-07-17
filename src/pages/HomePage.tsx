import BrokenImageRoundedIcon from '@mui/icons-material/BrokenImageRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import MovieRoundedIcon from '@mui/icons-material/MovieRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import PlayCircleRoundedIcon from '@mui/icons-material/PlayCircleRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import TaskAltRoundedIcon from '@mui/icons-material/TaskAltRounded'
import { Alert, Box, Button, CircularProgress, Paper, Snackbar, Stack, Typography } from '@mui/material'
import { alpha } from '@mui/material/styles'
import type { ReactNode } from 'react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { MediaCard, MediaCardGrid } from '@/components/MediaCard'
import { HealthMeter, StatCard, SurfaceSection } from '@/components/ProductComponents'
import { bridge } from '@/services/bridge'
import type { DashboardSummary, MediaItem } from '@/types/media'

function QuickAction({ icon, label, onClick }: { icon: ReactNode; label: string; onClick: () => void }) {
  return <Button variant="outlined" color="inherit" startIcon={icon} onClick={onClick} sx={{ justifyContent: 'flex-start', borderColor: 'divider', bgcolor: 'background.paper', minHeight: 42 }}>{label}</Button>
}

export default function HomePage() {
  const [dashboard, setDashboard] = useState<DashboardSummary>()
  const [error, setError] = useState(''); const [notice, setNotice] = useState(''); const navigate = useNavigate()
  useEffect(() => { bridge.dashboard().then(setDashboard).catch((reason: Error) => setError(reason.message)) }, [])
  const play = (item: MediaItem) => bridge.play(item.dataId).then(() => setNotice(`已交给系统播放器：${item.code}`)).catch((reason: Error) => setNotice(reason.message))
  const wall = (items: MediaItem[]) => <MediaCardGrid>{items.map((item) => <MediaCard key={item.dataId} item={item} onPlay={play} onOpen={() => navigate(`/movies/${item.dataId}`)}/>)}</MediaCardGrid>

  const health = dashboard && dashboard.movieCount ? Math.round((dashboard.movieCount - dashboard.missingFileCount) / dashboard.movieCount * 100) : 100
  const played = dashboard && dashboard.movieCount ? Math.round(dashboard.playedCount / dashboard.movieCount * 100) : 0
  const favorites = dashboard && dashboard.movieCount ? Math.round(dashboard.favoriteCount / dashboard.movieCount * 100) : 0

  return <Box sx={{ '@keyframes dashboardIn': { from: { opacity: 0, transform: 'translateY(8px)' }, to: { opacity: 1, transform: 'translateY(0)' } } }}>
    {error && <Alert severity="error">Dashboard 读取失败：{error}</Alert>}
    {!dashboard && !error ? <Box sx={{ minHeight: 420, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : dashboard && <Stack spacing={2.5} sx={{ animation: 'dashboardIn .32s ease both', '@media (prefers-reduced-motion: reduce)': { animation: 'none' } }}>
      <Paper variant="outlined" sx={{ position: 'relative', overflow: 'hidden', borderRadius: 3.5, p: { xs: 2.5, md: 3.5 }, minHeight: 190, display: 'flex', alignItems: 'center' }}>
        <Box sx={{ position: 'absolute', inset: 0, background: (theme) => `radial-gradient(circle at 82% 15%, ${alpha(theme.palette.primary.main, .28)}, transparent 38%), radial-gradient(circle at 66% 110%, ${alpha(theme.palette.secondary.main, .14)}, transparent 42%)`, pointerEvents: 'none' }}/>
        <Box sx={{ position: 'relative', maxWidth: 720 }}><Typography variant="overline" color="primary.main" sx={{ fontWeight: 850, letterSpacing: '.16em' }}>LOCAL MEDIA MANAGER</Typography>
          <Typography variant="h3" sx={{ fontWeight: 900, letterSpacing: '-.035em', mt: .5 }}>你的媒体，一目了然</Typography>
          <Typography color="text.secondary" sx={{ mt: 1, lineHeight: 1.7 }}>本地优先的媒体管理工作台。快速回到最近内容，掌握媒体库状态，并从一个入口继续整理与探索。</Typography>
          <Stack direction="row" spacing={1.25} sx={{ mt: 2.25 }}><Button variant="contained" startIcon={<MovieRoundedIcon/>} onClick={() => navigate('/media')}>浏览影片墙</Button><Button variant="outlined" startIcon={<SearchRoundedIcon/>} onClick={() => navigate('/search')}>全局搜索</Button></Stack>
        </Box>
      </Paper>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2,minmax(0,1fr))', md: 'repeat(3,minmax(0,1fr))', xl: 'repeat(6,minmax(0,1fr))' }, gap: 1.25 }}>
        <StatCard label="全部影片" value={dashboard.movieCount} icon={<MovieRoundedIcon/>}/>
        <Box onClick={() => navigate('/search?metadataStatus=complete')} sx={{ cursor: 'pointer' }}><StatCard label="已完整" value={dashboard.completeMetadataCount} icon={<TaskAltRoundedIcon/>} tone="success.main"/></Box>
        <Box onClick={() => navigate('/search?metadataStatus=missing-images')} sx={{ cursor: 'pointer' }}><StatCard label="待完善" value={dashboard.pendingMetadataCount} icon={<BrokenImageRoundedIcon/>} tone="warning.main"/></Box>
        <Box onClick={() => navigate('/search?metadataStatus=unscraped')} sx={{ cursor: 'pointer' }}><StatCard label="未刮削" value={dashboard.unscrapedCount} icon={<BrokenImageRoundedIcon/>} tone="error.main"/></Box>
        <StatCard label="我的收藏" value={dashboard.favoriteCount} icon={<FavoriteRoundedIcon/>} tone="error.main"/>
        <StatCard label="播放过" value={dashboard.playedCount} icon={<PlayCircleRoundedIcon/>} tone="success.main"/>
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0,1.35fr) minmax(300px,.65fr)' }, gap: 2 }}>
        <SurfaceSection title="媒体库状态" description="根据当前数据库实时计算">
          <Stack spacing={2}><HealthMeter label="文件可用率" value={health} detail={`${health}%`} tone={health > 95 ? 'success' : health > 85 ? 'warning' : 'error'}/><HealthMeter label="已播放覆盖" value={played} detail={`${dashboard.playedCount} / ${dashboard.movieCount}`} tone="primary"/><HealthMeter label="收藏占比" value={favorites} detail={`${dashboard.favoriteCount} 部`} tone="error"/></Stack>
        </SurfaceSection>
        <SurfaceSection title="快捷入口" description="继续常用工作">
          <Box sx={{ display: 'grid', gap: 1 }}><QuickAction icon={<PlayArrowRoundedIcon/>} label="继续最近播放" onClick={() => navigate('/history')}/><QuickAction icon={<FolderRoundedIcon/>} label="管理媒体库" onClick={() => navigate('/libraries')}/><QuickAction icon={<TaskAltRoundedIcon/>} label="查看任务中心" onClick={() => navigate('/tasks')}/></Box>
        </SurfaceSection>
      </Box>

      <SurfaceSection title="最近导入" description="新加入媒体库的影片" action={<Button size="small" onClick={() => navigate('/media')}>查看全部</Button>}>{wall(dashboard.recentImports)}</SurfaceSection>
      {dashboard.recentPlays.length > 0 && <SurfaceSection title="最近播放" description="继续浏览近期观看内容">{wall(dashboard.recentPlays)}</SurfaceSection>}
    </Stack>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
