import ContentCopyRoundedIcon from '@mui/icons-material/ContentCopyRounded'
import DriveFileMoveRoundedIcon from '@mui/icons-material/DriveFileMoveRounded'
import OpenInNewRoundedIcon from '@mui/icons-material/OpenInNewRounded'
import PlaceRoundedIcon from '@mui/icons-material/PlaceRounded'
import { Alert, Box, Button, Card, CardContent, Dialog, DialogActions, DialogContent, DialogTitle, MenuItem, Snackbar, Stack, TextField, ToggleButton, ToggleButtonGroup, Tooltip, Typography } from '@mui/material'
import { useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState, StatCard } from '@/components/ProductComponents'
import { MovieWall } from '@/components/workspace/MovieWall'
import { DuplicateStatusBadge, StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, refreshAction } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { DuplicateResults, MediaItem, OrganizerPreview } from '@/types/media'

type OrganizerMode = 'duplicates' | 'batch'
type DuplicateRule = 'all' | 'code' | 'path' | 'hash'

export default function OrganizerPage() {
  const [mode, setMode] = useState<OrganizerMode>(readOrganizerMode)
  useEffect(() => { window.sessionStorage.setItem('lmm.organizer.mode', mode) }, [mode])
  return mode === 'batch' ? <BatchOrganizerView mode={mode} onModeChange={setMode}/> : <DuplicateOrganizerView mode={mode} onModeChange={setMode}/>
}

function OrganizerModeToggle({ mode, onModeChange }: { mode: OrganizerMode; onModeChange: (mode: OrganizerMode) => void }) {
  return <ToggleButtonGroup exclusive size="small" value={mode} onChange={(_, next) => next && onModeChange(next)} aria-label="整理工具阶段">
    <ToggleButton value="duplicates">重复影片</ToggleButton>
    <ToggleButton value="batch">批量整理</ToggleButton>
  </ToggleButtonGroup>
}

function DuplicateOrganizerView({ mode, onModeChange }: { mode: OrganizerMode; onModeChange: (mode: OrganizerMode) => void }) {
  const navigate = useNavigate()
  const [rule, setRule] = useState<DuplicateRule>('all')
  const [data, setData] = useState<DuplicateResults>()
  const [error, setError] = useState('')
  const load = useCallback(() => {
    setData(undefined); setError('')
    bridge.duplicates(rule).then(setData).catch((reason: Error) => setError(reason.message))
  }, [rule])
  useEffect(load, [load])
  const locate = (code: string, title: string) => navigate(`/search?q=${encodeURIComponent((code || title).trim())}`)
  const filters = <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', md: 'center' }, flexWrap: 'wrap' }}>
    <OrganizerModeToggle mode={mode} onModeChange={onModeChange}/>
    <TextField select size="small" label="重复类型" value={rule} onChange={event => setRule(event.target.value as DuplicateRule)} sx={{ width: { xs: '100%', sm: 190 } }}>
      <MenuItem value="all">全部重复项</MenuItem><MenuItem value="code">番号重复</MenuItem><MenuItem value="path">文件路径重复</MenuItem><MenuItem value="hash">文件 Hash 重复</MenuItem>
    </TextField>
  </Stack>
  const stats = data && <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit,minmax(180px,1fr))', gap: 1.5 }}>
    <StatCard label="重复组" value={data.totalGroups} icon={<ContentCopyRoundedIcon/>} tone={data.totalGroups ? 'warning.main' : 'success.main'}/>
    <StatCard label="涉及影片" value={data.totalMovies} icon={<ContentCopyRoundedIcon/>} tone={data.totalMovies ? 'warning.main' : 'success.main'}/>
    <StatCard label="番号" value={data.codeGroups} icon={<ContentCopyRoundedIcon/>}/>
    <StatCard label="路径" value={data.pathGroups} icon={<ContentCopyRoundedIcon/>}/>
    <StatCard label="Hash" value={data.hashGroups} icon={<ContentCopyRoundedIcon/>}/>
  </Box>

  return <WorkspacePage title="整理工具" description="按整理流程集中处理重复影片和批量整理；本阶段先合并入口与架构。" stats={stats} filters={filters} activeFilterCount={rule === 'all' ? 0 : 1} onClearFilters={() => setRule('all')} loading={!data && !error} error={error}
    primaryActions={[refreshAction(load, '重新扫描')]}>
    {data && (data.groups.length === 0 ? <EmptyState title="未发现重复项" description="当前筛选范围内没有重复番号、路径或已有文件 Hash。"/> :
      <Stack spacing={1.5}>{data.groups.map(group => <Card key={`${group.rule}:${group.key}`}><CardContent>
        <Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap', mb: 1.25 }}>
          <DuplicateStatusBadge rule={group.rule}/>
          <Typography sx={{ fontWeight: 800, minWidth: 0, overflowWrap: 'anywhere' }}>{group.key}</Typography>
          <StatusBadge label={`${group.count} 部`} tone="neutral"/>
        </Stack>
        <Stack spacing={1}>{group.items.map(item => <Box key={item.movieId} sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'minmax(150px,.8fr) minmax(180px,1fr) minmax(220px,1.4fr) auto' }, gap: 1.25, alignItems: 'center', py: .75, borderTop: 1, borderColor: 'divider' }}>
          <Box sx={{ minWidth: 0 }}><Typography noWrap sx={{ fontWeight: 750 }}>{item.code || `#${item.movieId}`}</Typography><Typography variant="caption" color="text.secondary">ID {item.movieId}</Typography></Box>
          <Typography noWrap title={item.title || item.code}>{item.title || '未命名影片'}</Typography>
          <Tooltip title={item.fileHash || item.filePath || '无文件信息'}><Typography variant="body2" color="text.secondary" noWrap>{group.rule === 'hash' ? item.fileHash : item.filePath}</Typography></Tooltip>
          <Stack direction="row" spacing={.5}>
            <Button size="small" startIcon={<PlaceRoundedIcon/>} onClick={() => locate(item.code, item.title)}>定位</Button>
            <Button size="small" startIcon={<OpenInNewRoundedIcon/>} onClick={() => navigate(`/movies/${item.movieId}`)}>详情</Button>
          </Stack>
        </Box>)}</Stack>
      </CardContent></Card>)}</Stack>)}
  </WorkspacePage>
}

function BatchOrganizerView({ mode, onModeChange }: { mode: OrganizerMode; onModeChange: (mode: OrganizerMode) => void }) {
  const [selected, setSelected] = useState<number[]>([])
  const [template, setTemplate] = useState('{Code}')
  const [destination, setDestination] = useState('')
  const [preview, setPreview] = useState<OrganizerPreview>()
  const [notice, setNotice] = useState('')
  const [busy, setBusy] = useState(false)
  const [reloadSignal, setReloadSignal] = useState(0)
  const toggleSelected = (item: MediaItem, checked: boolean) => setSelected((current) => checked ? [...current, item.dataId] : current.filter((id) => id !== item.dataId))
  const dryRun = () => {
    if (!selected.length || busy) return
    setBusy(true)
    bridge.organizerDryRun(selected, template, destination).then(setPreview).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false))
  }
  const execute = () => {
    if (!preview || busy) return
    setBusy(true)
    bridge.executeOrganizer(preview.taskId, preview.confirmationToken).then((result) => {
      setNotice(`${result.message} 可在任务中心查看进度。`)
      setPreview(undefined)
      setSelected([])
      setReloadSignal((value) => value + 1)
    }).catch((reason: Error) => setNotice(reason.message)).finally(() => setBusy(false))
  }
  return <>
    <MovieWall title="整理工具" description={(total) => `按当前 MovieWall 条件筛选 ${total} 部影片，并对选中影片执行文件整理 Dry Run。`} stateKey="lmm.organizer.batch" selectable selectedIds={selected} onSelect={toggleSelected} reloadSignal={reloadSignal}
      defaultLabel={<Stack direction="row" spacing={1} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}><OrganizerModeToggle mode={mode} onModeChange={onModeChange}/>{selected.length > 0 && <StatusBadge tone="info" label={`已选择 ${selected.length} 部`}/>}</Stack>}
      primaryActions={({ items }) => [
        { key: 'select-page', label: '全选当前页', variant: 'outlined', onClick: () => setSelected(items.map((item) => item.dataId)), disabled: items.length === 0 },
        { key: 'clear-selection', label: '清空选择', variant: 'outlined', color: 'inherit', onClick: () => setSelected([]), disabled: selected.length === 0 },
        { key: 'dry-run', label: 'Dry Run 批量整理', icon: <DriveFileMoveRoundedIcon/>, variant: 'contained', onClick: dryRun, disabled: selected.length === 0 || busy },
      ]}
      renderStats={() => <Stack spacing={1.25} sx={{ p: 1.25, border: 1, borderColor: 'divider', borderRadius: 2, bgcolor: 'action.hover' }}>
        <Stack direction={{ xs: 'column', md: 'row' }} spacing={1.25} useFlexGap sx={{ alignItems: { xs: 'stretch', md: 'center' }, flexWrap: 'wrap' }}>
          <TextField size="small" label="文件名模板" value={template} onChange={event => setTemplate(event.target.value)} sx={{ flex: '1 1 220px' }} helperText="支持 {Code}、{Title}、{Year}、{Actors}"/>
          <TextField size="small" label="目标目录" value={destination} onChange={event => setDestination(event.target.value)} sx={{ flex: '1 1 280px' }} helperText="留空时仅在原目录重命名，不覆盖已有文件"/>
        </Stack>
      </Stack>}
      emptyTitle="暂无可整理影片" emptyDescription="当前筛选条件下没有可用于整理的影片。"/>
    <Dialog open={Boolean(preview)} onClose={() => !busy && setPreview(undefined)} maxWidth="md" fullWidth>
      <DialogTitle>批量整理预览</DialogTitle>
      <DialogContent>
        {preview && <Stack spacing={1.25} sx={{ mt: 1 }}>
          <Alert severity={preview.conflictItems ? 'error' : 'success'}>Dry Run：{preview.validItems} 项可执行，{preview.conflictItems} 项冲突。确认前不会移动或重命名文件。</Alert>
          {preview.warnings.map((warning) => <Alert key={warning} severity="warning">{warning}</Alert>)}
          {preview.items.map(item => <Card key={item.mediaFileId} variant="outlined"><CardContent>
            <Typography variant="caption" color="text.secondary">原路径</Typography>
            <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{item.sourcePath}</Typography>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 1 }}>目标路径</Typography>
            <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{item.destinationPath}</Typography>
            {item.conflict && <Alert severity="error" sx={{ mt: 1 }}>{item.conflict}</Alert>}
          </CardContent></Card>)}
        </Stack>}
      </DialogContent>
      <DialogActions><Button disabled={busy} onClick={() => setPreview(undefined)}>取消</Button><Button variant="contained" disabled={busy || !preview || preview.conflictItems > 0} onClick={execute}>确认并进入任务</Button></DialogActions>
    </Dialog>
    <Snackbar open={Boolean(notice)} autoHideDuration={3500} onClose={() => setNotice('')} message={notice}/>
  </>
}

function readOrganizerMode(): OrganizerMode {
  return window.sessionStorage.getItem('lmm.organizer.mode') === 'batch' ? 'batch' : 'duplicates'
}
