import { CssBaseline, ThemeProvider } from '@mui/material'
import { createContext, type PropsWithChildren, useContext, useMemo, useState } from 'react'
import { createLmmTheme } from './theme'

type ColorMode = 'light' | 'dark'
const ColorModeContext = createContext<{ mode: ColorMode; setMode: (mode: ColorMode) => void } | null>(null)

export function LmmThemeProvider({ children }: PropsWithChildren) {
  const [mode, setMode] = useState<ColorMode>('dark')
  const value = useMemo(() => ({ mode, setMode }), [mode])
  return <ColorModeContext.Provider value={value}>
    <ThemeProvider theme={createLmmTheme(mode)}><CssBaseline />{children}</ThemeProvider>
  </ColorModeContext.Provider>
}

export function useColorMode() {
  const value = useContext(ColorModeContext)
  if (!value) throw new Error('Color mode provider is missing')
  return value
}
