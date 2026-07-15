import { Box, Typography } from '@mui/material'

export function BrandMark({ compact = false }: { compact?: boolean }) {
  const size = compact ? 32 : 40
  return <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.25, minWidth: 0 }}>
    <Box component="svg" viewBox="0 0 256 256" aria-label="LMM" sx={{ width: size, height: size, flex: '0 0 auto' }}>
      <defs><linearGradient id="lmm-brand" x1="24" y1="24" x2="232" y2="232" gradientUnits="userSpaceOnUse"><stop stopColor="#16A8FF"/><stop offset=".52" stopColor="#2563EB"/><stop offset="1" stopColor="#7C3AED"/></linearGradient></defs>
      <rect x="8" y="8" width="240" height="240" rx="56" fill="url(#lmm-brand)"/>
      <path d="M61 61v126h54" fill="none" stroke="#fff" strokeWidth="27" strokeLinecap="round" strokeLinejoin="round"/>
      <path d="M126 188V91c0-13 15-19 24-10l20 20 20-20c9-9 24-3 24 10v97" fill="none" stroke="#fff" strokeWidth="23" strokeLinecap="round" strokeLinejoin="round"/>
    </Box>
    {!compact && <Box sx={{ minWidth: 0 }}>
      <Typography noWrap sx={{ fontWeight: 800, lineHeight: 1.15 }}>Local Media Manager</Typography>
      <Typography variant="caption" color="text.secondary" sx={{ letterSpacing: 1.6 }}>LMM</Typography>
    </Box>}
  </Box>
}
