import { Box, Fade, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { BrandMark } from '@/components/BrandMark'

export function BrandSplash(){
  const[visible,setVisible]=useState(true)
  useEffect(()=>{const timer=window.setTimeout(()=>setVisible(false),650);return()=>window.clearTimeout(timer)},[])
  return <Fade in={visible} unmountOnExit timeout={{enter:0,exit:260}}><Box sx={{position:'fixed',inset:0,zIndex:2000,bgcolor:'background.default',display:'grid',placeItems:'center'}}>
    <Box sx={{textAlign:'center'}}><Box sx={{transform:'scale(1.35)',mb:2.5}}><BrandMark/></Box><Typography variant="body2" color="text.secondary">正在连接本地媒体库</Typography></Box>
  </Box></Fade>
}
