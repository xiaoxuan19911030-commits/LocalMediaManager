import React from 'react'
import { createRoot } from 'react-dom/client'
import { CssBaseline, ThemeProvider } from '@mui/material'
import { RouterProvider } from 'react-router'
import { router } from '@/app/router'
import { createLmmTheme } from '@/themes/theme'
import './styles.css'

const root = document.getElementById('root')
if (!root) throw new Error('Missing root element')

createRoot(root).render(
  <React.StrictMode>
    <ThemeProvider theme={createLmmTheme('dark')}>
      <CssBaseline />
      <RouterProvider router={router} />
    </ThemeProvider>
  </React.StrictMode>,
)

