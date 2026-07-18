import { Stack } from '@mui/material'
import { MovieWall } from '@/components/workspace/MovieWall'
import { StatusBadge } from '@/components/workspace/StatusBadges'

export default function CollectionPage({ kind }: { kind: 'favorites' | 'history' }) {
  const favorite = kind === 'favorites'
  return <MovieWall title={favorite ? '我的收藏' : '最近播放'}
    description={(total) => favorite ? `共 ${total} 部已收藏影片。` : `共 ${total} 部有播放记录的影片。`}
    stateKey={`lmm.movieWall.${kind}`}
    defaults={favorite ? { favorite: true } : { watched: true }}
    defaultLabel={<Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}><StatusBadge tone={favorite ? 'error' : 'success'} label={favorite ? '收藏' : '已观看'}/></Stack>}
    pageSize={24}
    emptyTitle={favorite ? '暂无收藏' : '暂无播放历史'}
    emptyDescription="数据存在时会通过 Bridge 显示在这里。"/>
}
