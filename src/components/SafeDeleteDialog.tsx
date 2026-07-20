import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import { Alert, Box, Button, Checkbox, Dialog, DialogActions, DialogContent, DialogTitle, Divider, FormControlLabel, Stack, Typography } from '@mui/material'
import { useMemo, useState } from 'react'
import type { SafeDeleteLaunchResult, SafeDeletePreview, SafeDeletePreviewCommand } from '@/types/media'
import { bridge } from '@/services/bridge'

const formatSize = (bytes: number) => bytes >= 1024 * 1024 * 1024
  ? `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`
  : bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.ceil(bytes / 1024)} KB`

export function SafeDeleteDialog({ preview, command, onClose, onLaunched }: {
  preview?: SafeDeletePreview
  command?: SafeDeletePreviewCommand
  onClose: () => void
  onLaunched: (result: SafeDeleteLaunchResult) => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [confirmOriginal, setConfirmOriginal] = useState(false)
  const canSubmit = useMemo(() => {
    if (!preview || !command) return false
    if (preview.deletesOriginalMedia && !confirmOriginal) return false
    return true
  }, [command, confirmOriginal, preview])

  const execute = async () => {
    if (!preview || !command) return
    setBusy(true); setError('')
    try {
      const result = await bridge.executeSafeDelete(command, preview.confirmationToken, confirmOriginal)
      onLaunched(result)
    } catch (reason) {
      setError((reason as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return <Dialog open={Boolean(preview)} onClose={busy ? undefined : onClose} maxWidth="md" fullWidth>
    <DialogTitle>{preview?.mode === 'media' ? '安全删除影片' : '删除影片信息'}</DialogTitle>
    <DialogContent dividers>
      {preview && <Stack spacing={2}>
        <Alert severity={preview.deletesOriginalMedia ? 'error' : 'warning'} icon={<DeleteOutlineRoundedIcon/>}>
          {preview.deletesOriginalMedia ? '此操作将删除原始影片文件。默认移动到系统回收站，成功后才会更新数据库。' : '只删除数据库信息，不删除原始媒体文件。'}
        </Alert>
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(3,minmax(0,1fr))' }, gap: 1.25 }}>
          <Summary label="影片数量" value={`${preview.movieCount}`}/>
          <Summary label="预计释放" value={formatSize(preview.estimatedBytes)}/>
          <Summary label="数据库信息" value={preview.deleteDatabaseInfo ? '删除' : '保留'}/>
        </Box>
        {preview.warnings.map(warning => <Alert key={warning} severity="info">{warning}</Alert>)}
        <Stack spacing={1.25}>
          {preview.items.map(item => <Box key={item.movieId} sx={{ border: 1, borderColor: 'divider', borderRadius: 1.5, p: 1.25 }}>
            <Typography sx={{ fontWeight: 800 }}>{item.code} {item.title ? `· ${item.title}` : ''}</Typography>
            <Typography variant="caption" color="text.secondary" sx={{ display: 'block', overflowWrap: 'anywhere' }}>{item.primaryPath || '没有主文件路径'}</Typography>
            {item.databaseInfo.length > 0 && <Typography variant="body2" sx={{ mt: .75 }}>将删除数据库信息：{item.databaseInfo.join('、')}</Typography>}
            <Divider sx={{ my: 1 }}/>
            <Stack spacing={.5}>
              {item.files.slice(0, 8).map(file => <Typography key={`${item.movieId}-${file.kind}-${file.path}`} variant="caption" color={file.willDelete ? 'text.primary' : 'text.secondary'} sx={{ overflowWrap: 'anywhere' }}>
                {file.willDelete ? '将删除' : '保留'} · {file.kind} · {file.exists ? formatSize(file.size) : '不存在'} · {file.path}{file.reason ? ` · ${file.reason}` : ''}
              </Typography>)}
              {item.files.length > 8 && <Typography variant="caption" color="text.secondary">另有 {item.files.length - 8} 个登记文件，执行前已纳入计划。</Typography>}
            </Stack>
            {item.warnings.map(warning => <Alert key={warning} severity="warning" sx={{ mt: 1 }}>{warning}</Alert>)}
          </Box>)}
        </Stack>
        {preview.deletesOriginalMedia && <Stack spacing={1}>
          <FormControlLabel control={<Checkbox checked={confirmOriginal} onChange={(_, checked) => setConfirmOriginal(checked)}/>} label="我确认删除原始影片文件"/>
        </Stack>}
        {error && <Alert severity="error">{error}</Alert>}
      </Stack>}
    </DialogContent>
    <DialogActions>
      <Button onClick={onClose} disabled={busy}>取消</Button>
      <Button variant="contained" color="error" disabled={!canSubmit || busy} onClick={() => void execute()}>
        {preview?.mode === 'media' ? '确认安全删除' : '确认删除信息'}
      </Button>
    </DialogActions>
  </Dialog>
}

function Summary({ label, value }: { label: string; value: string }) {
  return <Box sx={{ border: 1, borderColor: 'divider', borderRadius: 1.5, p: 1.25 }}>
    <Typography variant="caption" color="text.secondary">{label}</Typography>
    <Typography sx={{ fontWeight: 850 }}>{value}</Typography>
  </Box>
}
