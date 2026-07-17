import AutoAwesomeRoundedIcon from '@mui/icons-material/AutoAwesomeRounded'
import CloudRoundedIcon from '@mui/icons-material/CloudRounded'
import ComputerRoundedIcon from '@mui/icons-material/ComputerRounded'
import LockRoundedIcon from '@mui/icons-material/LockRounded'
import { Alert, Box, Card, CardContent, Typography } from '@mui/material'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage } from '@/components/workspace/Workspace'

const providers = [
  { name: 'OpenAI 兼容服务', type: '云端 Provider', icon: <CloudRoundedIcon/> },
  { name: 'Ollama', type: '本地 Provider', icon: <ComputerRoundedIcon/> },
  { name: '自定义 Provider', type: '开放接口预留', icon: <AutoAwesomeRoundedIcon/> },
]

export default function AiProvidersPage() {
  const stats = <StatusBadge tone="neutral" label="默认关闭"/>
  return <WorkspacePage title="AI Provider" description="架构预留：不连接模型、不发送媒体数据、不保存 API Key。" stats={stats}>
    <Alert severity="info" icon={<LockRoundedIcon/>} sx={{ mb: 2.5 }}>AI 默认关闭。后续启用前会明确展示发送范围、脱敏策略、图片与路径权限，并通过独立 AI 模块和受控 Bridge API 工作。</Alert>
    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(260px,1fr))', gap: 1.5 }}>{providers.map(provider => <Card key={provider.name} variant="outlined" sx={{ opacity: .85 }}><CardContent>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25 }}><Box sx={{ width: 46, height: 46, borderRadius: 2.25, bgcolor: 'action.hover', color: 'primary.main', display: 'grid', placeItems: 'center' }}>{provider.icon}</Box><Box sx={{ flex: 1, minWidth: 0 }}><Typography sx={{ fontWeight: 800 }} noWrap>{provider.name}</Typography><Typography variant="body2" color="text.secondary">{provider.type}</Typography></Box><StatusBadge label="未启用" tone="neutral"/></Box>
    </CardContent></Card>)}</Box>
    <Alert severity="warning" sx={{ mt: 2.5 }}>模型配置、连接测试、标签建议和元数据建议属于后续版本；当前不实现任何真实 AI 调用。</Alert>
  </WorkspacePage>
}
