import MovieRoundedIcon from '@mui/icons-material/MovieRounded'
import StorageRoundedIcon from '@mui/icons-material/StorageRounded'
import { Alert, Box, Card, CardContent, CircularProgress, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { BridgeHealth, LibrarySummary } from '@/types/media'

export default function HomePage() {
  const [health, setHealth] = useState<BridgeHealth>()
  const [summary, setSummary] = useState<LibrarySummary>()
  const [error, setError] = useState('')

  useEffect(() => {
    Promise.all([bridge.health(), bridge.summary()])
      .then(([nextHealth, nextSummary]) => { setHealth(nextHealth); setSummary(nextSummary) })
      .catch((reason: Error) => setError(reason.message))
  }, [])

  return (
    <Box>
      <PageHeader title="首页" description="Local Media Manager 下一代桌面前端架构验证。" />
      {error && <Alert severity="warning" sx={{ mb: 2 }}>后端 Bridge 尚未连接：{error}</Alert>}
      {!error && !summary && <CircularProgress />}
      <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(260px, 1fr))', gap: 2 }}>
        <Card><CardContent>
          <MovieRoundedIcon color="primary" />
          <Typography variant="h3" sx={{ mt: 1, fontWeight: 800 }}>{summary?.videoCount ?? '—'}</Typography>
          <Typography color="text.secondary">现有数据库影片总数</Typography>
        </CardContent></Card>
        <Card><CardContent>
          <StorageRoundedIcon color="success" />
          <Typography variant="h6" sx={{ mt: 1, fontWeight: 700 }}>{health?.databaseAvailable ? '已连接' : '等待连接'}</Typography>
          <Typography color="text.secondary" noWrap title={health?.databasePath}>{health?.databasePath || '正在读取 Bridge 状态'}</Typography>
          <Typography variant="caption" color="success.main">只读模式，不修改正式数据库</Typography>
        </CardContent></Card>
      </Box>
    </Box>
  )
}
