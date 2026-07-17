import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'

export function DangerConfirmDialog({
  open,
  title,
  description,
  warnings = [],
  confirmText,
  confirmLabel = '确认执行',
  busy,
  onClose,
  onConfirm,
}: {
  open: boolean
  title: string
  description: string
  warnings?: string[]
  confirmText?: string
  confirmLabel?: string
  busy?: boolean
  onClose: () => void
  onConfirm: () => void
}) {
  const [input, setInput] = useState('')
  const allowed = !confirmText || input === confirmText
  return <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
    <DialogTitle>{title}</DialogTitle>
    <DialogContent dividers>
      <Stack spacing={1.5}>
        <Alert severity="warning">{description}</Alert>
        {warnings.map(item => <Typography key={item} variant="body2">• {item}</Typography>)}
        {confirmText && <TextField value={input} onChange={event => setInput(event.target.value)} label={`输入 ${confirmText} 以确认`} autoFocus/>}
      </Stack>
    </DialogContent>
    <DialogActions>
      <Button onClick={onClose} disabled={busy}>取消</Button>
      <Button color="error" variant="contained" onClick={onConfirm} disabled={busy || !allowed}>{busy ? '执行中…' : confirmLabel}</Button>
    </DialogActions>
  </Dialog>
}
