import CloudOffRoundedIcon from '@mui/icons-material/CloudOffRounded'
import ExtensionRoundedIcon from '@mui/icons-material/ExtensionRounded'
import VerifiedRoundedIcon from '@mui/icons-material/VerifiedRounded'
import { Alert, Box, Card, CardContent, Stack, Typography } from '@mui/material'
import { useEffect, useMemo, useState } from 'react'
import { EmptyState } from '@/components/ProductComponents'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { SettingsSnapshot } from '@/types/settings'

const displayName = (id: string) => ({ private: 'Private', bus: 'BUS', javbus: 'JavBus' }[id.toLowerCase()] || id || '兼容服务器')

export default function PluginsPage() {
  const [snapshot, setSnapshot] = useState<SettingsSnapshot>()
  const [error, setError] = useState('')
  useEffect(() => { bridge.settings().then(setSnapshot).catch((reason: Error) => setError(reason.message)) }, [])
  const groups = useMemo(() => snapshot ? [...new Set(snapshot.servers.map(item => item.pluginId || 'legacy'))].map(id => ({ id, servers: snapshot.servers.filter(item => (item.pluginId || 'legacy') === id) })) : [], [snapshot])
  const stats = snapshot && <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
    <StatusBadge tone="info" label={`${groups.length} 个 Provider 组`}/>
    <StatusBadge tone={snapshot.servers.some(server => server.enabled) ? 'success' : 'neutral'} label={`${snapshot.servers.filter(server => server.enabled).length} 个已启用`}/>
  </Stack>

  return <WorkspacePage title="插件中心" description="查看旧配置中仍可识别的元数据服务与兼容状态。" stats={stats} loading={!snapshot && !error} error={error}>
    {snapshot && <>
      <Alert severity="info" sx={{ mb: 2 }}>当前仅读取兼容配置；安装、卸载、启停和在线市场尚未迁移，页面不会假装执行写操作。</Alert>
      {groups.length ? <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(280px,1fr))', gap: 1.5 }}>{groups.map(group => {
        const enabled = group.servers.some(server => server.enabled)
        const available = group.servers.some(server => server.available > 0)
        return <Card key={group.id} variant="outlined"><CardContent><Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, mb: 2 }}><Box sx={{ width: 44, height: 44, display: 'grid', placeItems: 'center', borderRadius: 2, bgcolor: 'action.hover', color: 'primary.main' }}><ExtensionRoundedIcon/></Box><Box sx={{ flex: 1, minWidth: 0 }}><Typography variant="h6" sx={{ fontWeight: 800 }} noWrap>{displayName(group.id)}</Typography><Typography variant="body2" color="text.secondary">旧版兼容 Provider</Typography></Box><StatusBadge label={enabled ? '已启用' : '未启用'} tone={enabled ? 'success' : 'neutral'}/></Box>
          <Stack spacing={1}>{group.servers.map((server, index) => <Box key={`${server.url}-${index}`} sx={{ p: 1.25, borderRadius: 2, bgcolor: 'action.hover' }}><Box sx={{ display: 'flex', gap: 1, alignItems: 'center' }}>{available ? <VerifiedRoundedIcon fontSize="small" color="success"/> : <CloudOffRoundedIcon fontSize="small" color="disabled"/>}<Typography noWrap title={server.url} sx={{ flex: 1 }}>{server.url || '未设置地址'}</Typography></Box></Box>)}</Stack>
        </CardContent></Card>
      })}</Box> : <EmptyState title="没有兼容插件配置" description="当前设置来源中未检测到服务器资源。"/>}
    </>}
  </WorkspacePage>
}
