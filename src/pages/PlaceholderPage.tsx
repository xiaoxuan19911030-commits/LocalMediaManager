import ConstructionRoundedIcon from '@mui/icons-material/ConstructionRounded'
import { Box } from '@mui/material'
import { EmptyState } from '@/components/ProductComponents'
import { WorkspacePage } from '@/components/workspace/Workspace'

export default function PlaceholderPage({ title }: { title: string }) {
  const empty = <Box sx={{ color: 'text.disabled', mb: 1 }}><ConstructionRoundedIcon sx={{ fontSize: 48 }}/></Box>
  return <WorkspacePage title={title} description="旧 WPF 功能仍然保留，本页将在对应迁移阶段接入。">
    <Box sx={{ '& .MuiCardContent-root > div': { display: 'grid', justifyItems: 'center' } }}>
      {empty}
      <EmptyState title="尚未迁移，不提供虚假操作" description="当前仅展示真实已接入能力；未完成能力会在对应迁移阶段接入。"/>
    </Box>
  </WorkspacePage>
}
