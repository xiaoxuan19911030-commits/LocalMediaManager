import ConstructionRoundedIcon from '@mui/icons-material/ConstructionRounded'
import { Box, Card, CardContent, Typography } from '@mui/material'
import { PageHeader } from '@/components/PageHeader'

export default function PlaceholderPage({ title }: { title: string }) {
  return <Box>
    <PageHeader title={title} description="旧 WPF 功能仍然保留，本页将在对应迁移阶段接入。" />
    <Card><CardContent sx={{ py: 6, textAlign: 'center' }}>
      <ConstructionRoundedIcon color="primary" sx={{ fontSize: 48 }} />
      <Typography variant="h6" sx={{ mt: 1 }}>尚未迁移，不提供虚假操作</Typography>
      <Typography color="text.secondary">当前原型仅验证桌面壳、数据库只读访问、影片列表和播放器调用。</Typography>
    </CardContent></Card>
  </Box>
}

