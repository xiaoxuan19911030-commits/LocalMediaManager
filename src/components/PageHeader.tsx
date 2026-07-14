import { Box, Typography } from '@mui/material'
import type { ReactNode } from 'react'

export function PageHeader({
  title,
  description,
  action,
}: {
  title: string
  description: string
  action?: ReactNode
}) {
  return (
    <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', mb: 3, gap: 2 }}>
      <Box>
        <Typography variant="h4" sx={{ fontWeight: 750 }}>{title}</Typography>
        <Typography color="text.secondary" sx={{ mt: 0.5 }}>{description}</Typography>
      </Box>
      {action}
    </Box>
  )
}
