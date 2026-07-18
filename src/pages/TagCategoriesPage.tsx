import GroupsRoundedIcon from '@mui/icons-material/GroupsRounded'
import LocalOfferRoundedIcon from '@mui/icons-material/LocalOfferRounded'
import MovieRoundedIcon from '@mui/icons-material/MovieRounded'
import SellRoundedIcon from '@mui/icons-material/SellRounded'
import { Box, Card, CardActionArea, CardContent, Stack, Typography } from '@mui/material'
import { useNavigate } from 'react-router'
import { WorkspacePage } from '@/components/workspace/Workspace'

const categories = [
  { title: '导演', description: '影片导演分类', path: '/tags/directors', icon: <GroupsRoundedIcon/> },
  { title: '标签', description: '浏览影片已有标签', path: '/tags/movie-tags', icon: <SellRoundedIcon/> },
  { title: '系列', description: '影片系列分类', path: '/tags/series', icon: <MovieRoundedIcon/> },
  { title: '自定义标签', description: '用户手动创建和维护的标签', path: '/tags/custom', icon: <LocalOfferRoundedIcon/> },
] as const

export default function TagCategoriesPage() {
  const navigate = useNavigate()
  return <WorkspacePage title="标签" description="按导演、标签、系列和自定义标签浏览影片。">
    <Box sx={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill,minmax(220px,1fr))', gap: 1.25 }}>
      {categories.map((item) => <Card key={item.path} variant="outlined">
        <CardActionArea onClick={() => navigate(item.path)}>
          <CardContent>
            <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
              <Box sx={{ width: 40, height: 40, borderRadius: 2, bgcolor: 'action.hover', color: 'primary.main', display: 'grid', placeItems: 'center' }}>{item.icon}</Box>
              <Box sx={{ minWidth: 0 }}>
                <Typography sx={{ fontWeight: 900 }}>{item.title}</Typography>
                <Typography variant="body2" color="text.secondary">{item.description}</Typography>
              </Box>
            </Stack>
          </CardContent>
        </CardActionArea>
      </Card>)}
    </Box>
  </WorkspacePage>
}
