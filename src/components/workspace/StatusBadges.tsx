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
  const tone: StatusTone = status === 'Completed' ? 'success' : ['CompletedWithErrors', 'CompletedWithWarnings', 'NoResult', 'Blocked'].includes(status) ? 'warning' : status === 'Failed' ? 'error' : status === 'Cancelled' ? 'neutral' : 'info'
  const labels: Record<string, string> = {
    Pending: '等待中',
    Preparing: '准备中',
    FetchingMetadata: '获取元数据',
    DownloadingImages: '下载图片',
    WritingMetadata: '写入元数据',
    WritingNfo: '写入 NFO',
    Retrying: '等待重试',
    Running: '进行中',
    Paused: '已暂停',
    Completed: '已完成',
    CompletedWithErrors: '部分错误',
    CompletedWithWarnings: '部分完成',
    NoResult: '无结果',
    Blocked: '被阻断',
    Failed: '失败',
    Cancelled: '已取消',
    Info: '信息',
    Warning: '警告',
    Error: '错误',
    Debug: '调试',
  }
  return <StatusBadge label={labels[status] ?? status} tone={tone}/>
}
