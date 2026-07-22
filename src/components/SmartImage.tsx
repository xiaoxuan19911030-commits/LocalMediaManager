import { Box } from '@mui/material'
import { useEffect, useRef, useState } from 'react'

const memoryCache = new Map<string, string>()

export function clearImageMemoryCache(url?: string) {
  if (url) memoryCache.delete(url)
  else memoryCache.clear()
}

export function SmartImage({ src, alt, fit = 'cover', eager = false, onError, bgcolor = 'action.hover' }: { src?: string; alt: string; fit?: 'cover' | 'contain'; eager?: boolean; onError?: () => void; bgcolor?: string }) {
  const ref = useRef<HTMLDivElement | null>(null)
  const [visible, setVisible] = useState(eager)
  const [loaded, setLoaded] = useState(src ? memoryCache.get(src) : undefined)

  useEffect(() => {
    if (eager || !ref.current || typeof IntersectionObserver === 'undefined') { setVisible(true); return }
    const fallback = window.setTimeout(() => setVisible(true), 300)
    const observer = new IntersectionObserver(([entry]) => {
      if (entry.isIntersecting) {
        window.clearTimeout(fallback)
        setVisible(true)
        observer.disconnect()
      }
    }, { rootMargin: '220px' })
    observer.observe(ref.current)
    return () => {
      window.clearTimeout(fallback)
      observer.disconnect()
    }
  }, [eager, src])

  useEffect(() => {
    if (!src || !visible) { setLoaded(undefined); return }
    const cached = memoryCache.get(src)
    if (cached) { setLoaded(cached); return }
    let active = true
    const image = new Image()
    image.onload = () => { if (active) { memoryCache.set(src, src); setLoaded(src) } }
    image.onerror = () => { if (active) onError?.() }
    image.src = src
    return () => { active = false; setLoaded(undefined) }
  }, [src, visible, onError])

  return <Box ref={ref} component={loaded ? 'img' : 'div'} src={loaded} alt={alt}
    sx={{ width: '100%', height: '100%', objectFit: fit, display: 'block', bgcolor }}/>
}
