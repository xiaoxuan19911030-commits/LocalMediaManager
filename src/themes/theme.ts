import { createTheme, type PaletteMode } from '@mui/material/styles'

export function createLmmTheme(mode: PaletteMode) {
  const dark = mode === 'dark'
  return createTheme({
    palette: {
      mode,
      primary: { main: dark ? '#0A84FF' : '#007AFF' },
      secondary: { main: dark ? '#FF9F0A' : '#FC9B76' },
      success: { main: dark ? '#30D158' : '#06943D' },
      error: { main: dark ? '#FF453A' : '#FF3B30' },
      background: {
        default: dark ? '#0f1117' : '#f5f5f7',
        paper: dark ? '#181b23' : '#ffffff',
      },
    },
    shape: { borderRadius: 10 },
    typography: {
      fontFamily:
        '-apple-system, BlinkMacSystemFont, "Microsoft YaHei UI", "Microsoft YaHei", Roboto, sans-serif',
      button: { textTransform: 'none', fontWeight: 600 },
    },
    components: {
      MuiButton: { defaultProps: { disableElevation: true } },
      MuiPaper: {
        styleOverrides: {
          root: { backgroundImage: 'none' },
        },
      },
      MuiCard: {
        styleOverrides: {
          root: {
            border: `1px solid ${dark ? 'rgba(255,255,255,.08)' : 'rgba(15,23,42,.09)'}`,
            boxShadow: 'none',
          },
        },
      },
    },
  })
}

