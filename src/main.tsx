import React from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router'
import { router } from '@/app/router'
import { LmmThemeProvider } from '@/themes/ThemeContext'
import './styles.css'

const root = document.getElementById('root')
if (!root) throw new Error('Missing root element')

createRoot(root).render(
  <React.StrictMode>
    <LmmThemeProvider>
      <RouterProvider router={router} />
    </LmmThemeProvider>
  </React.StrictMode>,
)
