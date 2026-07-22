import { Chip, Stack } from '@mui/material'
import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { SectionTitle } from '@/components/ProductComponents'
import { MovieWall } from '@/components/workspace/MovieWall'
import { bridge } from '@/services/bridge'
import type { GlobalSearchResult } from '@/types/media'

export default function SearchPage() {
  const [params] = useSearchParams()
  const initialSearch = params.get('q') || ''

  return <MovieWall key={initialSearch} title="搜索" description={(total) => total ? `共 ${total} 部匹配影片。` : '通过 Smart Search 与统一 FilterBar 组合筛选影片。'}
    stateKey={`lmm.movieWall.search.${initialSearch || 'empty'}`} initialSearch={initialSearch} pageSize={24}
    emptyTitle={initialSearch ? '没有匹配影片' : '搜索你的媒体库'}
    emptyDescription={initialSearch ? '尝试减少筛选条件或使用更短的关键词。' : '输入关键词，或直接选择评分、元数据状态、图片状态和媒体库条件。'}
    childrenAfterResults={(context) => <RelatedSection query={context.query}/>}/>
}

function RelatedSection({ query }: { query: string }) {
  const navigate = useNavigate()
  const [related, setRelated] = useState<GlobalSearchResult>()
  useEffect(() => {
    if (!query) { setRelated(undefined); return }
    let active = true
    bridge.search(query).then((result) => { if (active) setRelated(result) }).catch(() => { if (active) setRelated(undefined) })
    return () => { active = false }
  }, [query])
  if (!related || (related.actors.length === 0 && related.tags.length === 0)) return null
  return <Stack spacing={2} sx={{ mt: 3 }}>
    <SectionTitle title="相关维度"/>
    <Stack direction="row" useFlexGap spacing={1} sx={{ flexWrap: 'wrap' }}>
      {related.actors.map(item => <Chip clickable key={`actor-${item.id}`} label={`演员：${item.name} · ${item.movieCount}`} onClick={() => navigate(`/actors/${item.id}`)}/>)}
      {related.tags.map(item => <Chip clickable key={`tag-${item.id}`} color="primary" variant="outlined" label={`标签：${item.name} · ${item.movieCount}`} onClick={() => navigate('/tags')}/>)}
    </Stack>
  </Stack>
}
