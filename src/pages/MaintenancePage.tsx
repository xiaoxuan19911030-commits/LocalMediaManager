import BuildRoundedIcon from '@mui/icons-material/BuildRounded'
import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import ImageRoundedIcon from '@mui/icons-material/ImageRounded'
import ReportProblemRoundedIcon from '@mui/icons-material/ReportProblemRounded'
import TaskAltRoundedIcon from '@mui/icons-material/TaskAltRounded'
import { Alert, Box, Button, Card, CardContent, Pagination, Stack, Typography } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState, StatCard, SurfaceSection } from '@/components/ProductComponents'
import { DuplicateStatusBadge, MaintenanceStatusBadge, StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { MaintenanceReport } from '@/types/media'

const pageSize = 50
const openDirectory = (path?: string) => {
  if (!path) return
  const directory = path.includes('.') ? path.replace(/[\\/][^\\/]*$/, '') : path
  window.open(`file:///${directory.replaceAll('\\', '/')}`)
}

export default function MaintenancePage() {
  const navigate = useNavigate()
  const [page, setPage] = useState(1)
  const [data, setData] = useState<MaintenanceReport>()
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const load = useCallback((nextPage = page) => {
    setData(undefined); setError('')
    bridge.maintenanceReport(pageSize, (nextPage - 1) * pageSize).then(setData).catch((reason: Error) => setError(reason.message))
  }, [page])
  useEffect(() => { load(page) }, [page, load])
  const action = (run: Promise<unknown>, message: string) => run.then(() => { setNotice(message); load(page) }).catch((reason: Error) => setError(reason.message))
  const stats = data && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(170px,1fr))', gap: 1.25 }}>
    <StatCard label="影片总数" value={data.stats.totalMovies} icon={<TaskAltRoundedIcon/>}/>
    <StatCard label="健康影片" value={data.stats.healthyMovies} icon={<TaskAltRoundedIcon/>} tone="success.main"/>
    <StatCard label="异常影片" value={data.stats.problemMovies} icon={<ReportProblemRoundedIcon/>} tone="warning.main"/>
    <StatCard label="重复影片" value={data.stats.duplicateMovies} icon={<ContentCopyRoundedIcon/>} tone="warning.main"/>
    <StatCard label="缺图片" value={data.stats.missingImages} icon={<ImageRoundedIcon/>} tone="warning.main"/>
    <StatCard label="缺 NFO" value={data.stats.missingNfo} icon={<ReportProblemRoundedIcon/>} tone="warning.main"/>
    <StatCard label="缺 Metadata" value={data.stats.missingMetadata} icon={<BuildRoundedIcon/>} tone="warning.main"/>
    <StatCard label="孤立文件" value={data.stats.orphanFiles} icon={<FolderRoundedIcon/>} tone="info.main"/>
    <StatCard label="空目录" value={data.stats.emptyDirectories} icon={<FolderRoundedIcon/>} tone="info.main"/>
    <StatCard label="缓存异常" value={data.stats.cacheProblems} icon={<ImageRoundedIcon/>} tone="error.main"/>
  </Box>

  return <WorkspacePage title="Maintenance" description="统一维护中心：健康检查、孤立文件、目录问题、重复影片和缓存维护。" stats={stats} loading={!data && !error} error={error} primaryActions={[refreshAction(() => load(page), '重新扫描')]}>
    {notice && <Alert severity="success" onClose={() => setNotice('')} sx={{ mb: 2 }}>{notice}</Alert>}
    {data && <Stack spacing={2.25}>
      <SurfaceSection title="一键维护" description="调用已有安全接口，不新增删除或修复逻辑。">
        <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
          <Button variant="outlined" onClick={() => action(bridge.metadataOverview(), 'Metadata 状态已刷新')}>刷新 Metadata</Button>
          <Button variant="outlined" onClick={() => action(bridge.maintenanceReport(pageSize, (page - 1) * pageSize), '图片状态已刷新')}>刷新图片状态</Button>
          <Button variant="outlined" onClick={() => action(bridge.rebuildImageCache(), 'Thumbnail 重建任务已创建')}>重新生成 Thumbnail</Button>
          <Button variant="outlined" onClick={() => action(bridge.imageCachePreview(), '缓存状态已刷新')}>刷新缓存</Button>
          <Button variant="outlined" onClick={() => load(page)}>重新建立索引</Button>
        </Stack>
      </SurfaceSection>

      <SurfaceSection title="Library Health" description="分页展示扫描结果；文件定位仅打开目录，不执行删除。">
        {data.issues.length ? <Stack spacing={1} sx={{ maxHeight: 520, overflowY: 'auto' }}>
          {data.issues.map((item, index) => <Card key={`${item.category}-${item.movieId}-${item.path}-${index}`} variant="outlined"><CardContent sx={{ py: 1.25, '&:last-child': { pb: 1.25 } }}>
            <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
              <MaintenanceStatusBadge severity={item.severity} label={item.category}/>
              <Typography sx={{ fontWeight: 800 }}>{item.title}</Typography>
              {item.movieId && <Button size="small" onClick={() => navigate(`/movies/${item.movieId}`)}>详情</Button>}
              {item.path && <Button size="small" onClick={() => openDirectory(item.path)}>定位目录</Button>}
            </Stack>
            <Typography variant="body2" color="text.secondary" sx={{ mt: .5, overflowWrap: 'anywhere' }}>{item.detail}{item.path ? ` · ${item.path}` : ''}</Typography>
          </CardContent></Card>)}
        </Stack> : <EmptyState title="未发现健康问题" description="当前分页没有媒体库健康检查问题。"/>}
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 1.5 }}><Pagination count={Math.max(1, Math.ceil(data.stats.problemMovies / pageSize))} page={page} onChange={(_, value) => setPage(value)}/></Box>
      </SurfaceSection>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '1fr 1fr' }, gap: 2.25 }}>
        <PathSection title="孤立文件" description="图片、NFO、字幕、演员头像和缓存文件中未匹配影片的条目。" items={data.orphanFiles}/>
        <PathSection title="目录问题" description="空目录、只有图片、只有 NFO、只有字幕或没有视频的目录。" items={data.directories}/>
      </Box>

      <SurfaceSection title="重复影片（二阶段）" description="根据收藏、评分、文件大小、播放次数和最近观看生成保留建议；不执行删除。">
        {data.duplicates.groups.length ? <Stack spacing={1.25}>{data.duplicates.groups.slice(0, 20).map(group => <Card key={`${group.rule}:${group.key}`} variant="outlined"><CardContent>
          <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}><DuplicateStatusBadge rule={group.rule}/><Typography sx={{ fontWeight: 850 }}>{group.key}</Typography></Stack>
          {group.items.map(item => <Stack key={item.movieId} direction="row" spacing={1} sx={{ alignItems: 'center', py: .5, borderTop: 1, borderColor: 'divider' }}>
            <StatusBadge tone={item.recommendation === '推荐保留' ? 'success' : 'warning'} label={item.recommendation}/>
            <Typography sx={{ flex: 1 }} noWrap>{item.code || item.title}</Typography>
            <Button size="small" onClick={() => navigate(`/movies/${item.movieId}`)}>详情</Button>
          </Stack>)}
        </CardContent></Card>)}</Stack> : <EmptyState title="未发现重复影片" description="当前扫描规则下没有重复候选。"/>}
      </SurfaceSection>
    </Stack>}
  </WorkspacePage>
}

function PathSection({ title, description, items }: { title: string; description: string; items: { path: string; reason?: string }[] }) {
  return <SurfaceSection title={title} description={description}>
    <Stack spacing={1} sx={{ maxHeight: 360, overflowY: 'auto' }}>{items.length ? items.map(item => <Card key={item.path} variant="outlined"><CardContent sx={{ py: 1.25, '&:last-child': { pb: 1.25 } }}><Typography sx={{ fontWeight: 750, overflowWrap: 'anywhere' }}>{item.path}</Typography>{item.reason && <Typography variant="body2" color="text.secondary">{item.reason}</Typography>}<Button size="small" onClick={() => openDirectory(item.path)}>定位目录</Button></CardContent></Card>) : <Typography color="text.secondary">Empty</Typography>}</Stack>
  </SurfaceSection>
}
