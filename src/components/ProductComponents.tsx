import InboxRoundedIcon from '@mui/icons-material/InboxRounded'
import { Box, Card, CardContent, Typography } from '@mui/material'
import type { ReactNode } from 'react'

export function StatCard({ label, value, icon, tone = 'primary.main' }: { label: string; value: ReactNode; icon: ReactNode; tone?: string }) {
  return <Card><CardContent sx={{ display: 'flex', alignItems: 'center', gap: 2 }}>
    <Box sx={{ width: 44, height: 44, borderRadius: 2.25, display: 'grid', placeItems: 'center', bgcolor: 'action.hover', color: tone }}>{icon}</Box>
    <Box><Typography variant="h5" sx={{ fontWeight: 800, lineHeight: 1.15 }}>{value}</Typography><Typography variant="body2" color="text.secondary">{label}</Typography></Box>
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
