import DarkModeRoundedIcon from '@mui/icons-material/DarkModeRounded'
import LightModeRoundedIcon from '@mui/icons-material/LightModeRounded'
import { Accordion, AccordionDetails, AccordionSummary, Alert, Box, Button, CircularProgress, FormControlLabel,
  Paper, Stack, Switch, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, TextField, Tooltip, Typography } from '@mui/material'
import ExpandMoreRoundedIcon from '@mui/icons-material/ExpandMoreRounded'
import { useEffect, useMemo, useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { SettingsItem, SettingsLayout, SettingsSaveBar, SettingsSection, SettingsStatusBanner, settingsCategories } from '@/components/settings/SettingsComponents'
import { bridge } from '@/services/bridge'
import { useColorMode } from '@/themes/ThemeContext'
import type { MetaTubeSettings, SettingsSnapshot } from '@/types/settings'
import { BrandMark } from '@/components/BrandMark'

const descriptions: Record<string, string> = {
  general: '启动、窗口行为和语言。', library: '媒体库行为、扫描导入和数据库状态。',
  playback: '播放器路径和系统默认播放行为。', metadata: '图片、同步、网络、NFO、缓存、视频处理和重命名。',
  appearance: '主题与影片卡片显示。主题预览仅影响当前窗口，不写入旧配置。', shortcuts: '老板键和旧快捷键配置。',
  advanced: '插件、服务器、端口、日志及尚未归类的兼容字段。',
}

export default function SettingsPage() {
  const [category, setCategory] = useState('general')
  const [snapshot, setSnapshot] = useState<SettingsSnapshot | null>(null)
  const [error, setError] = useState('')
  const [providerNotice, setProviderNotice] = useState<{ severity: 'success' | 'error' | 'info'; text: string }>()
  const [providerBusy, setProviderBusy] = useState(false)
  const [metaTube, setMetaTube] = useState<MetaTubeSettings>()
  const { mode, setMode } = useColorMode()
  useEffect(() => { bridge.settings().then(value => { setSnapshot(value); setMetaTube(value.metaTube) }).catch((reason: Error) => setError(reason.message)) }, [])
  const title = settingsCategories.find(([key]) => key === category)?.[1] ?? '设置'
  const fields = useMemo(() => snapshot?.fields.filter(field => field.category === category) ?? [], [snapshot, category])
  const mapped = fields.filter(field => field.mapped)
  const compatibility = fields.filter(field => !field.mapped)
  const sections = [...new Set(mapped.map(field => field.section))]

  return <Box>
    <PageHeader title="设置" description="Local Media Manager 的统一设置中心。" />
    {error && <Alert severity="error">设置读取失败：{error}</Alert>}
    {!snapshot && !error ? <Box sx={{ minHeight: 320, display: 'grid', placeItems: 'center' }}><CircularProgress /></Box> : snapshot &&
      <SettingsLayout category={category} onCategoryChange={setCategory}>
        <Typography variant="h5" sx={{ fontWeight: 800 }}>{title}</Typography>
        <Typography color="text.secondary" sx={{ mt: 0.5, mb: 2 }}>{descriptions[category]}</Typography>
        <SettingsStatusBanner errors={snapshot.errors} mapped={snapshot.mappedCount} unmapped={snapshot.unmappedCount} />
        {category === 'metadata' && metaTube && <SettingsSection title="MetaTube Provider" description="复用旧版已验证的 MetaTube v1 接口。同步始终只补全空字段，不覆盖用户手工数据和已有文件。">
          <Box sx={{ p: 2 }}>
            {providerNotice && <Alert severity={providerNotice.severity} onClose={() => setProviderNotice(undefined)} sx={{ mb: 2 }}>{providerNotice.text}</Alert>}
            <Stack spacing={1.5}>
              <FormControlLabel control={<Switch checked={metaTube.enabled} onChange={event => setMetaTube({ ...metaTube, enabled: event.target.checked })}/>} label="启用 MetaTube"/>
              <TextField size="small" label="服务地址" value={metaTube.baseUrl} onChange={event => setMetaTube({ ...metaTube, baseUrl: event.target.value })} helperText="默认 http://127.0.0.1:8080/；仅允许 HTTP/HTTPS。"/>
              <TextField size="small" type="number" label="请求超时（秒）" value={metaTube.timeoutSeconds} onChange={event => setMetaTube({ ...metaTube, timeoutSeconds: Number(event.target.value) || 30 })} slotProps={{ htmlInput: { min: 15, max: 180 } }}/>
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <FormControlLabel control={<Switch checked={metaTube.autoExecute} onChange={event => setMetaTube({ ...metaTube, autoExecute: event.target.checked })}/>} label="自动执行 Pending 同步任务"/>
                <FormControlLabel control={<Switch checked={metaTube.downloadImages} onChange={event => setMetaTube({ ...metaTube, downloadImages: event.target.checked })}/>} label="下载海报与预览图"/>
                <FormControlLabel control={<Switch checked={metaTube.writeNfo} onChange={event => setMetaTube({ ...metaTube, writeNfo: event.target.checked })}/>} label="生成 NFO（不覆盖）"/>
              </Stack>
              <Alert severity="info">非破坏合并已强制开启：已有标题、简介、日期、时长、标签、演员关系、图片和 NFO 不会被覆盖或删除。</Alert>
              <Stack direction="row" spacing={1} sx={{ justifyContent: 'flex-end' }}>
                <Button disabled={providerBusy} variant="outlined" onClick={() => { setProviderBusy(true); bridge.testMetaTube(metaTube).then(result => setProviderNotice({ severity: result.success ? 'success' : 'error', text: `${result.message}（${result.elapsedMilliseconds} ms）` })).catch((reason: Error) => setProviderNotice({ severity: 'error', text: reason.message })).finally(() => setProviderBusy(false)) }}>测试连接</Button>
                <Button disabled={providerBusy} variant="contained" onClick={() => { setProviderBusy(true); bridge.saveMetaTubeSettings(metaTube).then(value => { setMetaTube(value); setProviderNotice({ severity: 'success', text: 'MetaTube 设置已保存并立即生效。' }) }).catch((reason: Error) => setProviderNotice({ severity: 'error', text: reason.message })).finally(() => setProviderBusy(false)) }}>保存 Provider 设置</Button>
              </Stack>
            </Stack>
          </Box>
        </SettingsSection>}
        {category === 'appearance' && <SettingsSection title="主题预览" description="即时预览只修改当前窗口，不会写回旧主题设置。">
          <Stack direction="row" spacing={1} sx={{ p: 2 }}>
            <Button variant={mode === 'light' ? 'contained' : 'outlined'} startIcon={<LightModeRoundedIcon />} onClick={() => setMode('light')}>浅色</Button>
            <Button variant={mode === 'dark' ? 'contained' : 'outlined'} startIcon={<DarkModeRoundedIcon />} onClick={() => setMode('dark')}>深色</Button>
          </Stack>
        </SettingsSection>}
        {sections.map(section => <SettingsSection key={section} title={section}>
          {mapped.filter(field => field.section === section).map(field => <SettingsItem key={field.key} field={field} />)}
        </SettingsSection>)}
        {category === 'advanced' && snapshot.servers.length > 0 && <SettingsSection title="服务器资源" description="Cookies 和 Headers 已在 Bridge 中隐藏；当前阶段禁止修改和测试。">
          <TableContainer sx={{ overflowX: 'auto' }}><Table size="small" sx={{ minWidth: 760 }}>
            <TableHead><TableRow><TableCell sx={{ width: 70 }}>启用</TableCell><TableCell sx={{ width: 130 }}>插件</TableCell><TableCell>URL</TableCell><TableCell sx={{ width: 90 }}>状态</TableCell><TableCell sx={{ width: 170 }}>更新时间</TableCell><TableCell sx={{ width: 120 }}>Headers</TableCell></TableRow></TableHead>
            <TableBody>{snapshot.servers.map((server, index) => <TableRow key={`${server.pluginId}-${server.url}-${index}`}>
              <TableCell>{server.enabled ? '是' : '否'}</TableCell><TableCell>{server.pluginId}</TableCell>
              <Tooltip title={server.url}><TableCell sx={{ maxWidth: 320, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{server.url}</TableCell></Tooltip>
              <TableCell>{server.available > 0 ? '可用' : '未知'}</TableCell><TableCell>{server.lastRefreshDate || '未记录'}</TableCell><TableCell>{server.headers}</TableCell>
            </TableRow>)}</TableBody>
          </Table></TableContainer>
        </SettingsSection>}
        {category === 'advanced' && <SettingsSection title="关于 Local Media Manager" description="本地优先、可维护的现代媒体管理工具。">
          <Box sx={{ p: 2, display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2 }}><BrandMark/><Typography color="text.secondary">版本 0.4.1</Typography></Box>
        </SettingsSection>}
        {compatibility.length > 0 && <Accordion disableGutters><AccordionSummary expandIcon={<ExpandMoreRoundedIcon />}>
          <Typography sx={{ fontWeight: 700 }}>兼容字段（{compatibility.length}）</Typography></AccordionSummary>
          <AccordionDetails sx={{ p: 0 }}>{compatibility.map(field => <SettingsItem key={field.key} field={field} />)}</AccordionDetails>
        </Accordion>}
        <Paper variant="outlined" sx={{ mt: 2, p: 1.5 }}><Typography variant="caption" color="text.secondary">
          配置来源：{snapshot.sources.map(source => source.path).join('；')} · 读取时间：{new Date(snapshot.readAt).toLocaleString()}
        </Typography></Paper>
        <SettingsSaveBar />
      </SettingsLayout>}
  </Box>
}
