import ClearRoundedIcon from '@mui/icons-material/ClearRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import ViewListRoundedIcon from '@mui/icons-material/ViewListRounded'
import ViewModuleRoundedIcon from '@mui/icons-material/ViewModuleRounded'
import { Alert, Box, Button, CircularProgress, Paper, Stack, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material'
import type { ReactNode } from 'react'
import { EmptyState } from '@/components/ProductComponents'

export type WorkspaceViewMode = 'grid' | 'list'

export interface WorkspaceAction {
  key: string
  label: string
  icon?: ReactNode
  onClick: () => void
  variant?: 'text' | 'outlined' | 'contained'
  color?: 'inherit' | 'primary' | 'secondary' | 'success' | 'error' | 'info' | 'warning'
  disabled?: boolean
}

export function WorkspacePage({
  title,
  description,
  stats,
  primaryActions,
  secondaryActions,
  filters,
  activeFilterCount = 0,
  onClearFilters,
  loading,
  error,
  empty,
  children,
}: {
  title: string
  description?: string
  stats?: ReactNode
  primaryActions?: WorkspaceAction[]
  secondaryActions?: WorkspaceAction[]
  filters?: ReactNode
  activeFilterCount?: number
  onClearFilters?: () => void
  loading?: boolean
  error?: string
  empty?: { title: string; description: string }
  children?: ReactNode
}) {
  return <Box>
    <Paper variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3, mb: 2 }}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} sx={{ alignItems: { xs: 'stretch', md: 'center' }, justifyContent: 'space-between' }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h5" sx={{ fontWeight: 900 }}>{title}</Typography>
          {description && <Typography color="text.secondary" sx={{ mt: .5, maxWidth: 840 }}>{description}</Typography>}
        </Box>
        <WorkspaceToolbar primaryActions={primaryActions} secondaryActions={secondaryActions}/>
      </Stack>
      {stats && <Box sx={{ mt: 2 }}>{stats}</Box>}
    </Paper>
    {filters && <FilterBar activeCount={activeFilterCount} onClear={onClearFilters}>{filters}</FilterBar>}
    {error && <Alert severity="error" sx={{ mb: 2 }}>{error}</Alert>}
    {loading ? <WorkspaceLoading/> : empty ? <EmptyState title={empty.title} description={empty.description}/> : children}
  </Box>
}

export function WorkspaceToolbar({ primaryActions, secondaryActions }: { primaryActions?: WorkspaceAction[]; secondaryActions?: WorkspaceAction[] }) {
  const render = (action: WorkspaceAction) => <Button key={action.key} size="small" variant={action.variant ?? 'outlined'} color={action.color ?? 'primary'} startIcon={action.icon} onClick={action.onClick} disabled={action.disabled} sx={{ minHeight: 36, whiteSpace: 'nowrap' }}>{action.label}</Button>
  return <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', justifyContent: { xs: 'flex-start', md: 'flex-end' } }}>
    {primaryActions?.map(render)}
    {secondaryActions?.map(render)}
  </Stack>
}

export function FilterBar({ children, activeCount = 0, onClear }: { children: ReactNode; activeCount?: number; onClear?: () => void }) {
  return <Paper variant="outlined" sx={{ p: 1.5, borderRadius: 3, mb: 2 }}>
    <Stack direction={{ xs: 'column', lg: 'row' }} spacing={1.25} sx={{ alignItems: { xs: 'stretch', lg: 'center' } }}>
      <Box sx={{ flex: 1, minWidth: 0 }}>{children}</Box>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', justifyContent: 'flex-end' }}>
        <Typography variant="body2" color="text.secondary" sx={{ whiteSpace: 'nowrap' }}>{activeCount} 个条件</Typography>
        {onClear && <Button size="small" color="inherit" startIcon={<ClearRoundedIcon/>} onClick={onClear}>清空</Button>}
      </Stack>
    </Stack>
  </Paper>
}

export function WorkspaceLoading() {
  return <Box sx={{ minHeight: 320, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box>
}

export function refreshAction(onClick: () => void, label = '刷新'): WorkspaceAction {
  return { key: 'refresh', label, icon: <RefreshRoundedIcon/>, onClick }
}

export function ViewModeToggle({ value, onChange }: { value: WorkspaceViewMode; onChange: (value: WorkspaceViewMode) => void }) {
  return <ToggleButtonGroup exclusive size="small" value={value} onChange={(_, next) => next && onChange(next)} aria-label="视图模式">
    <ToggleButton value="grid" aria-label="卡片视图"><ViewModuleRoundedIcon fontSize="small"/></ToggleButton>
    <ToggleButton value="list" aria-label="列表视图"><ViewListRoundedIcon fontSize="small"/></ToggleButton>
  </ToggleButtonGroup>
}
