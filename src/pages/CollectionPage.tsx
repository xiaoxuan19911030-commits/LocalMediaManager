import { Snackbar } from '@mui/material'
import { useEffect, useState } from 'react'
import { MovieResultContainer, useMovieActions } from '@/components/workspace/MovieResults'
import { WorkspacePage } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { MediaItem } from '@/types/media'

const pageSize = 48

export default function CollectionPage({ kind }: { kind: 'favorites' | 'history' }) {
  const favorite = kind === 'favorites'
  const [page, setPage] = useState(1)
  const [items, setItems] = useState<MediaItem[]>()
  const [total, setTotal] = useState(0)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const movieActions = useMovieActions({ onNotice: setNotice, play: bridge.play })

  useEffect(() => {
    setItems(undefined); setError('')
    bridge.collection(kind, pageSize, (page - 1) * pageSize).then((result) => { setItems(result.items); setTotal(result.total) }).catch((reason: Error) => setError(reason.message))
  }, [kind, page])

  return <WorkspacePage title={favorite ? '我的收藏' : '最近播放'} description={favorite ? `共 ${total} 部已收藏影片。` : `共 ${total} 部有播放记录的影片。`} error={error} loading={items === undefined && !error}>
    <MovieResultContainer items={items ?? []} total={total} page={page} pageSize={pageSize} onPageChange={setPage} onPlay={movieActions.playMovie} onOpen={movieActions.openMovie} emptyTitle={favorite ? '暂无收藏' : '暂无播放历史'} emptyDescription="数据存在时会通过 Bridge 显示在这里。"/>
    <Snackbar open={Boolean(notice)} autoHideDuration={3000} onClose={() => setNotice('')} message={notice}/>
  </WorkspacePage>
}
