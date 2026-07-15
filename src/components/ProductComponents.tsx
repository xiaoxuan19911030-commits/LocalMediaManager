import InboxRoundedIcon from '@mui/icons-material/InboxRounded'
import { Box, Card, CardContent, LinearProgress, Paper, Typography } from '@mui/material'
import type { ReactNode } from 'react'

export function StatCard({ label, value, icon, tone = 'primary.main' }: { label: string; value: ReactNode; icon: ReactNode; tone?: string }) {
  return <Card sx={{ transition: 'transform .2s ease, border-color .2s ease', '&:hover': { transform: 'translateY(-2px)', borderColor: tone } }}><CardContent sx={{ display: 'flex', alignItems: 'center', gap: 1.5, p: 2, '&:last-child': { pb: 2 } }}>
    <Box sx={{ width: 42, height: 42, flex: '0 0 auto', borderRadius: 2.25, display: 'grid', placeItems: 'center', bgcolor: 'action.hover', color: tone }}>{icon}</Box>
    <Box sx={{ minWidth: 0 }}><Typography variant="h5" sx={{ fontWeight: 850, lineHeight: 1.1 }}>{value}</Typography><Typography variant="body2" color="text.secondary" noWrap>{label}</Typography></Box>
  </CardContent></Card>
}

export function SectionTitle({ title, description, action }: { title: string; description?: string; action?: ReactNode }) {
  return <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'end', gap: 2, mb: 1.5 }}>
    <Box><Typography variant="h6" sx={{ fontWeight: 800 }}>{title}</Typography>{description && <Typography variant="body2" color="text.secondary">{description}</Typography>}</Box>{action}
  </Box>
}

export function EmptyState({ title, description }: { title: string; description: string }) {
  return <Card><CardContent sx={{ minHeight: 220, display: 'grid', placeItems: 'center', textAlign: 'center' }}><Box>
    <InboxRoundedIcon sx={{ fontSize: 44, color: 'text.disabled' }}/><Typography sx={{ mt: 1, fontWeight: 750 }}>{title}</Typography><Typography variant="body2" color="text.secondary">{description}</Typography>
  </Box></CardContent></Card>
}

export function SurfaceSection({ title, description, action, children }: { title: string; description?: string; action?: ReactNode; children: ReactNode }) {
  return <Paper variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 3 }}>
    <SectionTitle title={title} description={description} action={action}/>{children}
  </Paper>
}

export function HealthMeter({ label, value, detail, tone = 'primary' }: { label: string; value: number; detail: string; tone?: 'primary' | 'success' | 'warning' | 'error' }) {
  return <Box><Box sx={{ display: 'flex', justifyContent: 'space-between', gap: 2, mb: .75 }}><Typography variant="body2" sx={{ fontWeight: 700 }}>{label}</Typography><Typography variant="body2" color="text.secondary">{detail}</Typography></Box>
    <LinearProgress color={tone} variant="determinate" value={Math.max(0, Math.min(100, value))} sx={{ height: 7, borderRadius: 999 }}/></Box>
}
