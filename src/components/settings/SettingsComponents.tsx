import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Box, Chip, Divider, FormControlLabel, List, ListItemButton, ListItemText,
  Paper, Stack, Switch, TextField, Tooltip, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import type { SettingField } from '@/types/settings'

export const settingsCategories = [
  ['general', '常规'], ['library', '媒体库'], ['playback', '播放'], ['metadata', '元数据'],
  ['appearance', '外观'], ['shortcuts', '快捷键'], ['advanced', '高级'],
] as const

export function SettingsLayout({ category, onCategoryChange, children }: {
  category: string; onCategoryChange: (value: string) => void; children: ReactNode
}) {
  return <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '176px minmax(0, 1fr)' }, gap: 2.25, alignItems: 'start' }}>
    <Paper variant="outlined" sx={{ position: { md: 'sticky' }, top: { md: 0 }, overflow: 'hidden' }}>
      <List dense sx={{ p: 0.75, display: { xs: 'flex', md: 'block' }, overflowX: 'auto' }}>
        {settingsCategories.map(([key, label]) => <ListItemButton key={key} selected={category === key}
          onClick={() => onCategoryChange(key)} sx={{ borderRadius: 1.5, minWidth: { xs: 92, md: 0 }, mb: { md: 0.5 } }}>
          <ListItemText primary={label} slotProps={{ primary: { sx: { fontWeight: category === key ? 700 : 500 } } }} />
        </ListItemButton>)}
      </List>
    </Paper>
    <Box sx={{ minWidth: 0, maxWidth: 1060, width: '100%' }}>{children}</Box>
  </Box>
}

export function SettingsStatusBanner({ errors, mapped, unmapped }: { errors: string[]; mapped: number; unmapped: number }) {
  return <Stack spacing={1} sx={{ mb: 2 }}>
    <Alert severity="info">当前为只读迁移验证。所有值来自旧配置，控件不会写回旧程序。</Alert>
    {errors.length > 0 && <Alert severity="error">{errors.join('；')}</Alert>}
    <Typography variant="caption" color="text.secondary">已准确映射 {mapped} 项；兼容字段 {unmapped} 项。缺失值会明确标注，不使用默认值冒充。</Typography>
  </Stack>
}

export function SettingsSection({ title, description, children }: { title: string; description?: string; children: ReactNode }) {
  return <Paper variant="outlined" sx={{ mb: 1.5, overflow: 'hidden' }}>
    <Box sx={{ px: { xs: 1.5, sm: 2 }, py: 1.5 }}>
      <Typography variant="subtitle1" sx={{ fontWeight: 750 }}>{title}</Typography>
      {description && <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>{description}</Typography>}
    </Box>
    <Divider />
    <Box>{children}</Box>
  </Paper>
}

function displayValue(field: SettingField) {
  if (field.readStatus !== 'ok') return ''
  if (field.value === null || field.value === undefined || field.value === '') return '未设置'
  if (typeof field.value === 'object') return JSON.stringify(field.value)
  if (field.key.endsWith('.ProxyMode')) return ['不使用代理', '使用系统代理', '自定义代理'][Number(field.value)] ?? String(field.value)
  if (field.key.endsWith('.ProxyType')) return ['HTTP', 'SOCKS'][Number(field.value)] ?? String(field.value)
  return String(field.value)
}

export function SettingsItem({ field }: { field: SettingField }) {
  const failed = field.readStatus !== 'ok'
  return <Box sx={{ px: { xs: 1.5, sm: 2 }, py: 1.35, display: 'grid',
    gridTemplateColumns: { xs: '1fr', sm: 'minmax(210px, 1fr) minmax(260px, 420px)' }, gap: 1.5, alignItems: 'center',
    '& + &': { borderTop: 1, borderColor: 'divider' } }}>
    <Box sx={{ minWidth: 0 }}>
      <Stack direction="row" spacing={0.75} sx={{ alignItems: 'center', flexWrap: 'wrap' }} useFlexGap>
        <Typography sx={{ fontWeight: 600 }}>{field.label}</Typography>
        {field.dangerous && <Tooltip title="危险设置；写入阶段必须单独验证"><WarningAmberRoundedIcon color="warning" fontSize="small" /></Tooltip>}
        {field.requiresRestart && <Chip label="需重启" size="small" variant="outlined" />}
        {!field.mapped && <Chip label="兼容字段" size="small" />}
      </Stack>
      <Tooltip title={field.key}><Typography variant="caption" color={failed ? 'error' : 'text.secondary'} noWrap>{failed ? field.error : field.key}</Typography></Tooltip>
    </Box>
    {field.valueType === 'boolean' && !failed ? <FormControlLabel sx={{ m: 0, justifySelf: { sm: 'end' } }}
      control={<Switch checked={Boolean(field.value)} disabled />} label={field.value ? '已开启' : '已关闭'} /> :
      <Tooltip title={displayValue(field)}><TextField size="small" fullWidth disabled error={failed}
        value={failed ? '读取失败' : displayValue(field)} slotProps={{ htmlInput: { 'aria-label': field.label } }} /></Tooltip>}
  </Box>
}

export function SettingsSaveBar() {
  return <Paper variant="outlined" sx={{ position: 'sticky', bottom: 0, mt: 2, px: 2, py: 1.25, zIndex: 2,
    display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 2 }}>
    <Box><Typography sx={{ fontWeight: 650 }}>只读模式</Typography><Typography variant="caption" color="text.secondary">保存接口尚未启用，旧配置不会发生变化。</Typography></Box>
    <Chip label="无写入权限" color="info" variant="outlined" />
  </Paper>
}
