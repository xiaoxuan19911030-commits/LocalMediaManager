import FolderRoundedIcon from '@mui/icons-material/FolderRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import StorageRoundedIcon from '@mui/icons-material/StorageRounded'
import WarningAmberRoundedIcon from '@mui/icons-material/WarningAmberRounded'
import { Alert, Box, Card, CardContent, Chip, CircularProgress, Stack, Typography } from '@mui/material'
import { useEffect, useState } from 'react'
import { PageHeader } from '@/components/PageHeader'
import { bridge } from '@/services/bridge'
import type { MediaLibrary } from '@/types/media'

export default function LibrariesPage() {
  const [libraries,setLibraries]=useState<MediaLibrary[]>();const[error,setError]=useState('')
  useEffect(()=>{bridge.libraries().then(setLibraries).catch((reason:Error)=>setError(reason.message))},[])
  return <Box><PageHeader title="媒体库" description="管理媒体库与来源文件夹；当前阶段展示从 Database v1 读取的真实结构。"/>
    {error&&<Alert severity="error">{error}</Alert>}{!libraries&&!error?<Box sx={{minHeight:320,display:'grid',placeItems:'center'}}><CircularProgress/></Box>:<Stack spacing={2}>
      {libraries?.map(library=><Card key={library.id}><CardContent>
        <Box sx={{display:'flex',justifyContent:'space-between',alignItems:'start',gap:2,mb:2}}><Box sx={{display:'flex',gap:1.5}}><Box sx={{width:44,height:44,borderRadius:2,bgcolor:'action.hover',display:'grid',placeItems:'center'}}><StorageRoundedIcon color="primary"/></Box><Box><Typography variant="h6" sx={{fontWeight:800}}>{library.name}</Typography><Typography variant="body2" color="text.secondary">{library.description||'本地媒体库'}</Typography></Box></Box><Chip color={library.enabled?'success':'default'} label={library.enabled?'已启用':'已停用'}/></Box>
        <Stack direction="row" spacing={1} sx={{mb:2}}><Chip icon={<FolderRoundedIcon/>} label={`${library.movieCount} 部影片`}/>{library.missingCount>0&&<Chip color="warning" icon={<WarningAmberRoundedIcon/>} label={`${library.missingCount} 个缺失`}/>}</Stack>
        <Stack spacing={1}>{library.folders.map(folder=><Box key={folder.id} sx={{display:'flex',alignItems:'center',gap:1.5,p:1.5,borderRadius:2,bgcolor:'action.hover'}}><FolderRoundedIcon color="action"/><Box sx={{minWidth:0,flex:1}}><Typography noWrap title={folder.path}>{folder.path}</Typography><Typography variant="caption" color="text.secondary">{folder.includeSubfolders?'包含子目录':'仅当前目录'} · {folder.scanMode}</Typography></Box><RefreshRoundedIcon fontSize="small" color={folder.lastScannedAt?'success':'disabled'}/></Box>)}</Stack>
      </CardContent></Card>)}
    </Stack>}
  </Box>
}
