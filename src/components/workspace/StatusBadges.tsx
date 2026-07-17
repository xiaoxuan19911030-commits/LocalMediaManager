import { Chip, Tooltip } from '@mui/material'
import type { MetadataStatus } from '@/types/media'

export type StatusTone = 'success' | 'warning' | 'error' | 'info' | 'neutral'

const color = (tone: StatusTone) => tone === 'neutral' ? 'default' : tone

export function StatusBadge({ label, tone = 'neutral', title }: { label: string; tone?: StatusTone; title?: string }) {
  const chip = <Chip size="small" variant="outlined" color={color(tone)} label={label}/>
  return title ? <Tooltip title={title}>{chip}</Tooltip> : chip
}

export function MetadataStatusBadge({ status }: { status?: MetadataStatus }) {
  if (!status) return <StatusBadge label="未知" tone="neutral"/>
  const tone: StatusTone = status.state === 'complete' ? 'success' : status.state === 'unscraped' ? 'error' : 'warning'
  return <StatusBadge label={status.label} tone={tone} title={status.missingItems.length ? `缺少：${status.missingItems.join('、')}` : status.label}/>
}

export function DuplicateStatusBadge({ rule }: { rule: string }) {
  return <StatusBadge label={rule === 'code' ? '番号重复' : rule === 'path' ? '路径重复' : rule === 'hash' ? 'Hash 重复' : rule} tone={rule === 'code' ? 'info' : 'warning'}/>
}

export function MaintenanceStatusBadge({ severity, label }: { severity?: string; label: string }) {
  return <StatusBadge label={label} tone={severity === 'error' ? 'error' : severity === 'warning' ? 'warning' : 'info'}/>
}

export function TaskStatusBadge({ status }: { status: string }) {
  const tone: StatusTone = status === 'Completed' ? 'success' : status === 'Failed' ? 'error' : status === 'Cancelled' ? 'neutral' : 'info'
  return <StatusBadge label={status} tone={tone}/>
}
