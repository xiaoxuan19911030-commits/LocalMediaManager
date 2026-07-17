import BrokenImageRoundedIcon from '@mui/icons-material/BrokenImageRounded'
import BuildRoundedIcon from '@mui/icons-material/BuildRounded'
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import FavoriteRoundedIcon from '@mui/icons-material/FavoriteRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import GroupsRoundedIcon from '@mui/icons-material/GroupsRounded'
import HistoryRoundedIcon from '@mui/icons-material/HistoryRounded'
import ImageRoundedIcon from '@mui/icons-material/ImageRounded'
import LocalOfferRoundedIcon from '@mui/icons-material/LocalOfferRounded'
import MovieRoundedIcon from '@mui/icons-material/MovieRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import PlayCircleRoundedIcon from '@mui/icons-material/PlayCircleRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import SettingsRoundedIcon from '@mui/icons-material/SettingsRounded'
import ShuffleRoundedIcon from '@mui/icons-material/ShuffleRounded'
import StorageRoundedIcon from '@mui/icons-material/StorageRounded'
import SyncRoundedIcon from '@mui/icons-material/SyncRounded'
import TaskAltRoundedIcon from '@mui/icons-material/TaskAltRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Box, Button, Chip, CircularProgress, Divider, Paper, Snackbar, Stack, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { MediaCard, MediaCardGrid } from '@/components/MediaCard'
import { EmptyState, HealthMeter, StatCard, SurfaceSection } from '@/components/ProductComponents'
import { bridge } from '@/services/bridge'
import type { DashboardActivity, DashboardEntity, DashboardLibrary, DashboardSummary, MediaItem } from '@/types/media'

function formatBytes(value: number) {
  if (!value) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  const index = Math.min(units.length - 1, Math.floor(Math.log(value) / Math.log(1024)))
  return `${(value / 1024 ** index).toFixed(index ? 1 : 0)} ${units[index]}`
}

function formatDate(value?: string) {
  if (!value) return '暂无记录'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString()
}

function meterTone(value: number): 'primary' | 'success' | 'warning' | 'error' {
  if (value >= 90) return 'success'
  if (value >= 70) return 'warning'
  return 'error'
}

function ActionButton({ icon, label, onClick }: { icon: ReactNode; label: string; onClick: () => void }) {
  return <Button variant="outlined" color="inherit" startIcon={icon} onClick={onClick} sx={{ justifyContent: 'flex-start', minHeight: 42, borderColor: 'divider' }}>{label}</Button>
}

function ActivityList({ items, openMovie }: { items: DashboardActivity[]; openMovie: (id?: number) => void }) {
  if (!items.length) return <EmptyState title="暂无动态" description="导入、任务和评分记录出现后会显示在这里。"/>
  return <Stack divider={<Divider flexItem/>}>
    {items.slice(0, 10).map((item, index) => <Box key={`${item.type}-${item.movieId ?? 'none'}-${item.createdAt ?? index}`} onClick={() => openMovie(item.movieId)} sx={{ py: 1.1, cursor: item.movieId ? 'pointer' : 'default' }}>
      <Typography sx={{ fontWeight: 750 }}>{item.title}</Typography>
      <Typography variant="body2" color="text.secondary" noWrap>{item.detail}</Typography>
      <Typography variant="caption" color="text.disabled">{formatDate(item.createdAt)}</Typography>
    </Box>)}
  </Stack>
}

function LibraryList({ items }: { items: DashboardLibrary[] }) {
  if (!items.length) return <EmptyState title="暂无媒体库" description="添加媒体库后会在这里显示容量和影片数量。"/>
  return <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2,minmax(0,1fr))' }, gap: 1.25 }}>
    {items.map((item) => <Paper key={item.id} variant="outlined" sx={{ p: 1.5, borderRadius: 2 }}>
      <Typography sx={{ fontWeight: 800 }} noWrap>{item.name}</Typography>
      <Typography variant="body2" color="text.secondary">{item.movieCount.toLocaleString()} 部影片 · {formatBytes(item.fileBytes)}</Typography>
      <Typography variant="caption" color="text.disabled">最近更新 {formatDate(item.lastUpdatedAt)}</Typography>
    </Paper>)}
  </Box>
}

function EntityList({ title, items, onSelect }: { title: string; items: DashboardEntity[]; onSelect: (name: string) => void }) {
  return <Box>
    <Typography variant="subtitle2" sx={{ fontWeight: 850, mb: 1 }}>{title}</Typography>
    {items.length ? <Stack direction="row" useFlexGap spacing={1} sx={{ flexWrap: 'wrap' }}>
      {items.slice(0, 8).map((item) => <Chip key={item.id} clickable label={`${item.name} · ${item.movieCount}`} onClick={() => onSelect(item.name)} />)}
    </Stack> : <Typography variant="body2" color="text.secondary">暂无数据</Typography>}
  </Box>
}

export default function HomePage() {
  const [dashboard, setDashboard] = useState<DashboardSummary>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const navigate = useNavigate()

  useEffect(() => { bridge.dashboard().then(setDashboard).catch((reason: Error) => setError(reason.message)) }, [])

  const play = (item: MediaItem) => bridge.play(item.dataId).then(() => setNotice(`已交给系统播放器：${item.code}`)).catch((reason: Error) => setNotice(reason.message))
  const openMovie = (id?: number) => { if (id) navigate(`/movies/${id}`) }
  const searchEntity = (name: string) => navigate(`/search?q=${encodeURIComponent(name)}`)
  const openRandom = () => {
    const pool = [...(dashboard?.recentImports ?? []), ...(dashboard?.recentPlays ?? [])]
    const item = pool[Math.floor(Math.random() * pool.length)]
    item ? navigate(`/movies/${item.dataId}`) : navigate('/media')
  }
  const wall = (items: MediaItem[]) => items.length
    ? <MediaCardGrid>{items.map((item) => <MediaCard key={item.dataId} item={item} onPlay={play} onOpen={() => navigate(`/movies/${item.dataId}`)}/>)}</MediaCardGrid>
    : <EmptyState title="暂无影片" description="有可展示的影片后会自动出现在这里。"/>

  return <Box>
    {error && <Alert severity="error">Dashboard 读取失败：{error}</Alert>}
    {!dashboard && !error ? <Box sx={{ minHeight: 420, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box> : dashboard && <Stack spacing={2.5}>
      <Paper variant="outlined" sx={{ p: { xs: 2, md: 2.75 }, borderRadius: 3 }}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ alignItems: { xs: 'stretch', md: 'center' }, justifyContent: 'space-between' }}>
          <Box>
            <Typography variant="overline" color="primary.main" sx={{ fontWeight: 850 }}>Dashboard</Typography>
            <Typography variant="h4" sx={{ fontWeight: 900 }}>媒体库总览</Typography>
            <Typography color="text.secondary" sx={{ mt: .75 }}>从一个入口查看影片、元数据、任务、维护和最近活动。</Typography>
          </Box>
          <Stack direction="row" useFlexGap spacing={1} sx={{ flexWrap: 'wrap' }}>
            <Button variant="contained" startIcon={<MovieRoundedIcon/>} onClick={() => navigate('/media')}>影片墙</Button>
            <Button variant="outlined" startIcon={<SearchRoundedIcon/>} onClick={() => navigate('/search')}>搜索</Button>
            <Button variant="outlined" startIcon={<ShuffleRoundedIcon/>} onClick={openRandom}>随机</Button>
          </Stack>
        </Stack>
      </Paper>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2,minmax(0,1fr))', md: 'repeat(4,minmax(0,1fr))', xl: 'repeat(8,minmax(0,1fr))' }, gap: 1.25 }}>
        <Box onClick={() => navigate('/media')} sx={{ cursor: 'pointer' }}><StatCard label="全部影片" value={dashboard.movieCount} icon={<MovieRoundedIcon/>}/></Box>
        <Box onClick={() => navigate('/favorites')} sx={{ cursor: 'pointer' }}><StatCard label="收藏" value={dashboard.favoriteCount} icon={<FavoriteRoundedIcon/>} tone="error.main"/></Box>
        <Box onClick={() => navigate('/history')} sx={{ cursor: 'pointer' }}><StatCard label="播放过" value={dashboard.playedCount} icon={<PlayCircleRoundedIcon/>} tone="success.main"/></Box>
        <Box onClick={() => navigate('/libraries')} sx={{ cursor: 'pointer' }}><StatCard label="媒体库" value={dashboard.libraryCount} icon={<StorageRoundedIcon/>}/></Box>
        <Box onClick={() => navigate('/actors')} sx={{ cursor: 'pointer' }}><StatCard label="演员" value={dashboard.actorCount} icon={<GroupsRoundedIcon/>}/></Box>
        <Box onClick={() => navigate('/tags')} sx={{ cursor: 'pointer' }}><StatCard label="标签" value={dashboard.tagCount} icon={<LocalOfferRoundedIcon/>}/></Box>
        <Box onClick={() => navigate('/tasks')} sx={{ cursor: 'pointer' }}><StatCard label="活动任务" value={dashboard.activeTaskCount} icon={<SyncRoundedIcon/>} tone="warning.main"/></Box>
        <Box onClick={() => navigate('/maintenance')} sx={{ cursor: 'pointer' }}><StatCard label="待维护" value={dashboard.maintenance.pendingMovies} icon={<BuildRoundedIcon/>} tone="warning.main"/></Box>
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0,1.35fr) minmax(320px,.65fr)' }, gap: 2 }}>
        <SurfaceSection title="元数据健康" description="完整度、图片、NFO、演员和标签覆盖率">
          <Stack spacing={2}>
            <HealthMeter label="完整影片" value={dashboard.metadataHealth.completeRate} detail={`${dashboard.completeMetadataCount} / ${dashboard.movieCount}`} tone={meterTone(dashboard.metadataHealth.completeRate)}/>
            <HealthMeter label="图片覆盖" value={dashboard.metadataHealth.imageRate} detail={`${dashboard.metadataHealth.imageRate}%`} tone={meterTone(dashboard.metadataHealth.imageRate)}/>
            <HealthMeter label="NFO 覆盖" value={dashboard.metadataHealth.nfoRate} detail={`${dashboard.metadataHealth.nfoRate}%`} tone={meterTone(dashboard.metadataHealth.nfoRate)}/>
            <HealthMeter label="演员覆盖" value={dashboard.metadataHealth.actorRate} detail={`${dashboard.metadataHealth.actorRate}%`} tone={meterTone(dashboard.metadataHealth.actorRate)}/>
            <HealthMeter label="标签覆盖" value={dashboard.metadataHealth.tagRate} detail={`${dashboard.metadataHealth.tagRate}%`} tone={meterTone(dashboard.metadataHealth.tagRate)}/>
          </Stack>
        </SurfaceSection>
        <SurfaceSection title="快捷入口" description="继续常用工作">
          <Box sx={{ display: 'grid', gap: 1 }}>
            <ActionButton icon={<FolderRoundedIcon/>} label="扫描与管理媒体库" onClick={() => navigate('/libraries')}/>
            <ActionButton icon={<SyncRoundedIcon/>} label="同步与任务中心" onClick={() => navigate('/tasks')}/>
            <ActionButton icon={<BuildRoundedIcon/>} label="维护中心" onClick={() => navigate('/maintenance')}/>
            <ActionButton icon={<ContentCopyRoundedIcon/>} label="重复影片" onClick={() => navigate('/duplicates')}/>
            <ActionButton icon={<SettingsRoundedIcon/>} label="设置" onClick={() => navigate('/settings')}/>
          </Box>
        </SurfaceSection>
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0,.75fr) minmax(0,1.25fr)' }, gap: 2 }}>
        <SurfaceSection title="维护信号" description="只读聚合，不执行修复操作">
          <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(2,minmax(0,1fr))', gap: 1.25 }}>
            <StatCard label="健康影片" value={dashboard.maintenance.healthyMovies} icon={<TaskAltRoundedIcon/>} tone="success.main"/>
            <StatCard label="缺失文件" value={dashboard.missingFileCount} icon={<WarningAmberRoundedIcon/>} tone="error.main"/>
            <StatCard label="重复影片" value={dashboard.maintenance.duplicateMovies} icon={<ContentCopyRoundedIcon/>} tone="warning.main"/>
            <StatCard label="缺图片" value={dashboard.maintenance.missingImages} icon={<ImageRoundedIcon/>} tone="warning.main"/>
            <StatCard label="缺 NFO" value={dashboard.maintenance.missingNfo} icon={<BrokenImageRoundedIcon/>} tone="warning.main"/>
            <StatCard label="缓存异常" value={dashboard.maintenance.cacheProblems} icon={<ImageRoundedIcon/>} tone="error.main"/>
          </Box>
        </SurfaceSection>
        <SurfaceSection title="最近活动" description="导入、任务、收藏和评分的时间线">
          <ActivityList items={dashboard.recentActivity} openMovie={openMovie}/>
        </SurfaceSection>
      </Box>

      <SurfaceSection title="媒体库" description="按关联影片数量排序">
        <LibraryList items={dashboard.libraries}/>
      </SurfaceSection>

      <SurfaceSection title="热门维度" description="标签、演员、导演、制作方和系列">
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2,minmax(0,1fr))', xl: 'repeat(5,minmax(0,1fr))' }, gap: 2 }}>
          <EntityList title="标签" items={dashboard.topTags} onSelect={searchEntity}/>
          <EntityList title="演员" items={dashboard.topActors} onSelect={searchEntity}/>
          <EntityList title="导演" items={dashboard.topDirectors} onSelect={searchEntity}/>
          <EntityList title="制作方" items={dashboard.topStudios} onSelect={searchEntity}/>
          <EntityList title="系列" items={dashboard.topSeries} onSelect={searchEntity}/>
        </Box>
      </SurfaceSection>

      <SurfaceSection title="最近导入" description="新加入媒体库的影片" action={<Button size="small" onClick={() => navigate('/media')}>查看全部</Button>}>
        {wall(dashboard.recentImports)}
      </SurfaceSection>
      <SurfaceSection title="最近播放" description="继续浏览近期观看内容" action={<Button size="small" startIcon={<HistoryRoundedIcon/>} onClick={() => navigate('/history')}>播放历史</Button>}>
        {wall(dashboard.recentPlays)}
      </SurfaceSection>
    </Stack>}
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </Box>
}
