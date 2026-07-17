import BrokenImageRoundedIcon from '@mui/icons-material/BrokenImageRounded'
import DescriptionRoundedIcon from '@mui/icons-material/DescriptionRounded'
import MovieFilterRoundedIcon from '@mui/icons-material/MovieFilterRounded'
import SellRoundedIcon from '@mui/icons-material/SellRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Box, CircularProgress } from '@mui/material'
import { useEffect, useState } from 'react'
import { HealthMeter, StatCard, SurfaceSection } from '@/components/ProductComponents'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { MetadataOverview } from '@/types/media'

export default function MetadataPage() {
  const [data, setData] = useState<MetadataOverview>(); const [error, setError] = useState('')
  useEffect(() => { bridge.metadataOverview().then(setData).catch((reason: Error) => setError(reason.message)) }, [])
  if (!data && !error) return <Box><PageHeader title="元数据中心" description="检查影片元数据完整度与资源状态。"/><Box sx={{ minHeight: 360, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box></Box>
  const percent = (missing: number) => data ? Math.round((data.totalMovies - missing) / Math.max(1, data.totalMovies) * 100) : 0
  return <Box><PageHeader title="元数据中心" description="只读汇总真实数据库中的刮削、海报、关联和 NFO 状态；修复操作将在后续版本接入任务系统。"/>
    {error && <Alert severity="error">元数据读取失败：{error}</Alert>}{data && <>
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1.5, mb: 2.5 }}>
        <StatCard label="影片总数" value={data.totalMovies} icon={<MovieFilterRoundedIcon/>}/>
        <StatCard label="已完整" value={data.completeMovies} icon={<DescriptionRoundedIcon/>} tone="success.main"/>
        <StatCard label="待完善" value={data.pendingMovies} icon={<WarningAmberRoundedIcon/>} tone="warning.main"/>
        <StatCard label="未刮削" value={data.unscrapedMovies} icon={<BrokenImageRoundedIcon/>} tone="error.main"/>
      </Box>
      <SurfaceSection title="完整度" description="数值直接由 Bridge 查询数据库，不在页面中访问 SQLite。"><Box sx={{ display: 'grid', gap: 2 }}>
        <HealthMeter label="标题" value={percent(data.missingTitle)} detail={`${data.missingTitle} 部缺失`} tone={data.missingTitle ? 'warning' : 'success'}/>
        <HealthMeter label="封面" value={percent(data.missingCover)} detail={`${data.missingCover} 部缺失`} tone={data.missingCover ? 'warning' : 'success'}/>
        <HealthMeter label="背景图" value={percent(data.missingFanart)} detail={`${data.missingFanart} 部缺失`} tone={data.missingFanart ? 'warning' : 'success'}/>
        <HealthMeter label="预览图" value={percent(data.missingPreview)} detail={`${data.missingPreview} 部缺失`} tone={data.missingPreview ? 'warning' : 'success'}/>
        <HealthMeter label="演员关联" value={percent(data.missingActors)} detail={`${data.missingActors} 部缺失`} tone="warning"/>
        <HealthMeter label="标签关联" value={percent(data.missingTags)} detail={`${data.missingTags} 部缺失`} tone={data.missingTags ? 'warning' : 'success'}/>
        <HealthMeter label="简介" value={percent(data.missingDescription)} detail={`${data.missingDescription} 部缺失`} tone={data.missingDescription ? 'warning' : 'success'}/>
        <HealthMeter label="NFO" value={percent(data.missingNfo)} detail={`${data.missingNfo} 部缺失`} tone="warning"/>
      </Box></SurfaceSection>
      <Alert severity="info" icon={<SellRoundedIcon/>} sx={{ mt: 2 }}>0.4.0 提供真实状态与问题入口；批量刮削、字段覆盖和关系修复尚未迁移，避免产生不可回滚的数据写入。</Alert>
    </>}
  </Box>
}
