import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded'
import BadgeRoundedIcon from '@mui/icons-material/BadgeRounded'
import CakeRoundedIcon from '@mui/icons-material/CakeRounded'
import FitnessCenterRoundedIcon from '@mui/icons-material/FitnessCenterRounded'
import LocalOfferRoundedIcon from '@mui/icons-material/LocalOfferRounded'
import PlaceRoundedIcon from '@mui/icons-material/PlaceRounded'
import { Alert, Avatar, Box, Button, CircularProgress, Paper, Stack, Typography } from '@mui/material'
import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router'
import { MovieWall } from '@/components/workspace/MovieWall'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { BRIDGE_ORIGIN, bridge } from '@/services/bridge'
import type { ActorDetail } from '@/types/media'

const fallbackText = (value?: string | number) => value === undefined || value === null || value === '' ? '暂无' : String(value)
const genderText = (value?: number) => value === 1 ? '男' : value === 2 ? '女' : value === 0 ? '未知' : '暂无'

export default function ActorDetailPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const actorId = Number(id || 0)
  const [actor, setActor] = useState<ActorDetail>()
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!Number.isFinite(actorId) || actorId <= 0) {
      setError('演员不存在。')
      setLoading(false)
      return
    }
    setLoading(true)
    setError('')
    bridge.actor(actorId)
      .then(setActor)
      .catch((reason: Error) => setError(reason.message))
      .finally(() => setLoading(false))
  }, [actorId])

  const fields = useMemo(() => actor ? [
    { label: '别名', value: fallbackText(actor.alias), icon: <BadgeRoundedIcon fontSize="small"/> },
    { label: '生日', value: fallbackText(actor.birthDate?.slice(0, 10)), icon: <CakeRoundedIcon fontSize="small"/> },
    { label: '身高', value: actor.heightCm ? `${actor.heightCm} cm` : '暂无', icon: <FitnessCenterRoundedIcon fontSize="small"/> },
    { label: '罩杯', value: fallbackText(actor.cup), icon: <LocalOfferRoundedIcon fontSize="small"/> },
    { label: '出生地', value: fallbackText(actor.birthPlace), icon: <PlaceRoundedIcon fontSize="small"/> },
    { label: '性别', value: genderText(actor.gender), icon: <BadgeRoundedIcon fontSize="small"/> },
    { label: '活动时期', value: fallbackText(actor.activityPeriod), icon: <BadgeRoundedIcon fontSize="small"/> },
    { label: '标签', value: '暂无标签', icon: <LocalOfferRoundedIcon fontSize="small"/> },
  ] : [], [actor])

  if (loading) return <Box sx={{ minHeight: 360, display: 'grid', placeItems: 'center' }}><CircularProgress/></Box>
  if (error || !actor) return <Alert severity="error">{error || '演员不存在。'}</Alert>

  const avatarUrl = `${BRIDGE_ORIGIN}/api/actors/${actor.id}/image`

  return <Stack spacing={2.25}>
    <Box>
      <Button color="inherit" startIcon={<ArrowBackRoundedIcon/>} onClick={() => navigate(-1)}>返回</Button>
    </Box>

    <Paper variant="outlined" sx={{ p: { xs: 2, md: 2.5 }, borderRadius: 1 }}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '132px minmax(0,1fr)' }, gap: 2.25, alignItems: 'start' }}>
        <Avatar
          src={avatarUrl}
          alt={actor.name}
          sx={{ width: 116, height: 116, bgcolor: 'action.selected', color: 'text.secondary', border: 1, borderColor: 'divider', mx: { xs: 'auto', md: 0 }, fontSize: 42, fontWeight: 850 }}
        >
          {actor.name.slice(0, 1)}
        </Avatar>
        <Stack spacing={1.5} sx={{ minWidth: 0 }}>
          <Box sx={{ minWidth: 0 }}>
            <Typography variant="h5" sx={{ fontWeight: 850, overflowWrap: 'anywhere' }}>{actor.name}</Typography>
            {actor.description && <Typography variant="body2" color="text.secondary" sx={{ mt: .75, lineHeight: 1.7, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{actor.description}</Typography>}
          </Box>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2,minmax(0,1fr))', lg: 'repeat(4,minmax(0,1fr))' }, gap: 1 }}>
            {fields.map((item) => <Paper key={item.label} variant="outlined" sx={{ p: 1.25, borderRadius: 1, minWidth: 0 }}>
              <Stack direction="row" spacing={.75} sx={{ alignItems: 'center', color: 'primary.main', mb: .5 }}>
                {item.icon}
                <Typography variant="caption" sx={{ fontWeight: 800 }}>{item.label}</Typography>
              </Stack>
              <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>{item.value}</Typography>
            </Paper>)}
          </Box>
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
            <StatusBadge tone="info" label="演员详情"/>
            <StatusBadge tone="neutral" label="影片列表已按演员筛选"/>
          </Stack>
        </Stack>
      </Box>
    </Paper>

    <MovieWall
      title="该演员所有影片"
      description={(total) => `共 ${total} 部影片`}
      stateKey={`lmm.actor.${actor.id}.movies`}
      defaults={{ actorId: actor.id }}
      defaultLabel={<StatusBadge tone="info" label={`演员：${actor.name}`}/>}
      emptyTitle="暂无关联影片"
      emptyDescription="当前演员还没有关联到可展示的影片。"
    />
  </Stack>
}
