import AddRoundedIcon from '@mui/icons-material/AddRounded'
import ArrowBackRoundedIcon from '@mui/icons-material/ArrowBackRounded'
import DeleteOutlineRoundedIcon from '@mui/icons-material/DeleteOutlineRounded'
import EditRoundedIcon from '@mui/icons-material/EditRounded'
import GroupsRoundedIcon from '@mui/icons-material/GroupsRounded'
import HandymanRoundedIcon from '@mui/icons-material/HandymanRounded'
import LocalOfferRoundedIcon from '@mui/icons-material/LocalOfferRounded'
import SearchRoundedIcon from '@mui/icons-material/SearchRounded'
import { Alert, Avatar, Box, Button, Card, CardActionArea, CardActions, CardContent, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, IconButton, InputAdornment, MenuItem, Pagination, Stack, TextField, Tooltip, Typography } from '@mui/material'
import { FormEvent, useCallback, useEffect, useState } from 'react'
import { useNavigate } from 'react-router'
import { EmptyState } from '@/components/ProductComponents'
import { MovieResultContainer } from '@/components/workspace/MovieResults'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { WorkspacePage, WorkspaceLoading, refreshAction } from '@/components/workspace/Workspace'
import { bridge } from '@/services/bridge'
import type { ActorRepairPreview, EntityCard, MediaItem } from '@/types/media'

const pageSize = 48
const readable = (value: string) => value && !value.includes('\uFFFD') ? value : '名称待修复'

export default function EntityPage({ type }: { type: 'actors' | 'tags' }) {
  const actorMode = type === 'actors'
  const navigate = useNavigate()
  const [items, setItems] = useState<EntityCard[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [input, setInput] = useState('')
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState('count')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [selected, setSelected] = useState<EntityCard>()
  const [movies, setMovies] = useState<MediaItem[]>()
  const [notice, setNotice] = useState('')
  const [editItem, setEditItem] = useState<EntityCard | null>()
  const [editName, setEditName] = useState('')
  const [creating, setCreating] = useState(false)
  const [deletePreview, setDeletePreview] = useState<{ item: EntityCard; token: string; count: number } | null>(null)
  const [actorRepair, setActorRepair] = useState<ActorRepairPreview>()
  const [actorAlias, setActorAlias] = useState('')
  const [actorGender, setActorGender] = useState('')
  const [actorBirthDate, setActorBirthDate] = useState('')
  const [actorDescription, setActorDescription] = useState('')
  const [undoAudit, setUndoAudit] = useState<number>()

  const load = useCallback(() => {
    setLoading(true)
    setError('')
    bridge.entities(type, query, sort, pageSize, (page - 1) * pageSize)
      .then((result) => { setItems(result.items); setTotal(result.total) })
      .catch((reason: Error) => setError(reason.message))
      .finally(() => setLoading(false))
  }, [page, query, sort, type])
  useEffect(load, [load])

  const open = (item: EntityCard) => {
    setSelected(item)
    setMovies(undefined)
    bridge.entityMovies(type, item.id).then((result) => setMovies(result.items)).catch((reason: Error) => setError(reason.message))
  }
  const play = (item: MediaItem) => bridge.play(item.dataId).then(() => setNotice(`正在打开：${item.code}`)).catch((reason: Error) => setNotice(reason.message))
  const submit = (event: FormEvent) => { event.preventDefault(); setPage(1); setQuery(input.trim()) }
  const clearFilters = () => { setInput(''); setQuery(''); setSort('count'); setPage(1) }
  const startCreate = () => { setCreating(true); setEditItem({ id: 0, name: '', movieCount: 0 }); setEditName('') }
  const startEdit = (item: EntityCard) => {
    setCreating(false)
    setEditItem(item)
    setEditName(readable(item.name))
    if (actorMode) {
      bridge.actor(item.id)
        .then((actor) => { setEditName(actor.name); setActorAlias(actor.alias || ''); setActorGender(actor.gender === undefined ? '' : String(actor.gender)); setActorBirthDate(actor.birthDate || ''); setActorDescription(actor.description || '') })
        .catch((reason: Error) => setError(reason.message))
    }
  }
  const saveEdit = () => {
    const value = editName.trim()
    if (!value) return
    const action = creating
      ? bridge.createTag({ name: value })
      : actorMode
        ? bridge.updateActor(editItem!.id, { name: value, alias: actorAlias, gender: actorGender === '' ? undefined : Number(actorGender), birthDate: actorBirthDate, description: actorDescription })
        : bridge.updateTag(editItem!.id, { name: value })
    action.then((result) => { setNotice(result.message); setEditItem(null); setCreating(false); load() }).catch((reason: Error) => setError(reason.message))
  }
  const requestDelete = (item: EntityCard) => bridge.previewDeleteTag(item.id).then((preview) => setDeletePreview({ item, token: preview.confirmationToken, count: preview.affectedMovies })).catch((reason: Error) => setError(reason.message))
  const confirmDelete = () => {
    if (!deletePreview) return
    bridge.deleteTag(deletePreview.item.id, deletePreview.token).then((result) => { setNotice(result.message); setUndoAudit(result.auditId); setDeletePreview(null); load() }).catch((reason: Error) => setError(reason.message))
  }
  const undoDelete = () => { if (undoAudit) bridge.rollbackOperation(undoAudit).then((result) => { setNotice(result.message); setUndoAudit(undefined); load() }).catch((reason: Error) => setError(reason.message)) }
  const repairActors = () => bridge.previewActorRepair().then((preview) => {
    if (!preview.affectedRelations) {
      setNotice(preview.warnings.join(' ') || '未发现需要修复的演员关系。')
      return
    }
    setActorRepair(preview)
  }).catch((reason: Error) => setError(reason.message))
  const confirmActorRepair = () => {
    if (!actorRepair) return
    bridge.applyActorRepair(actorRepair.confirmationToken).then((result) => { setNotice(result.message); setActorRepair(undefined); load() }).catch((reason: Error) => setError(reason.message))
  }

  if (selected) {
    return <WorkspacePage title={readable(selected.name)} description={`${selected.movieCount} 部关联影片`}
      primaryActions={[{ key: 'back', label: `返回${actorMode ? '演员' : '标签'}`, icon: <ArrowBackRoundedIcon/>, onClick: () => { setSelected(undefined); setMovies(undefined) } }]}>
      {movies === undefined
        ? <WorkspaceLoading/>
        : <MovieResultContainer items={movies} onPlay={play} onOpen={(item) => navigate(`/movies/${item.dataId}`)} emptyTitle="暂无关联影片" emptyDescription="数据已迁移，但该关联当前没有可展示影片。"/>}
    </WorkspacePage>
  }

  const filters = <Box component="form" onSubmit={submit} sx={{ display: 'flex', gap: 1.25, flexWrap: 'wrap' }}>
    <TextField size="small" value={input} onChange={(event) => setInput(event.target.value)} placeholder={actorMode ? '搜索演员' : '搜索标签'} sx={{ minWidth: 260 }} slotProps={{ input: { startAdornment: <InputAdornment position="start"><SearchRoundedIcon fontSize="small"/></InputAdornment> } }}/>
    <TextField select size="small" value={sort} onChange={(event) => { setPage(1); setSort(event.target.value) }} sx={{ width: 150 }}><MenuItem value="count">作品数量</MenuItem><MenuItem value="name">名称排序</MenuItem></TextField>
    <Button type="submit" variant="contained" size="small">搜索</Button>
  </Box>

  const stats = <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
    <StatusBadge tone="info" label={`${total} 个${actorMode ? '演员' : '标签'}`}/>
    <StatusBadge tone={query ? 'warning' : 'neutral'} label={query ? `搜索：${query}` : '全部'}/>
  </Stack>

  return <WorkspacePage title={actorMode ? '演员' : '标签'} description={actorMode ? '按作品数量或名称浏览演员及其关联影片。' : '浏览自定义标签、兼容标签和影片关联。'} stats={stats}
    filters={filters} activeFilterCount={(query ? 1 : 0) + (sort !== 'count' ? 1 : 0)} onClearFilters={clearFilters} loading={loading} error={error}
    primaryActions={actorMode ? [{ key: 'repair', label: '检查关系', icon: <HandymanRoundedIcon/>, variant: 'outlined', onClick: repairActors }] : [{ key: 'create', label: '新建标签', icon: <AddRoundedIcon/>, variant: 'contained', onClick: startCreate }]}
    secondaryActions={[refreshAction(load)]}>
    {items.length ? <Box sx={{ display: 'grid', gridTemplateColumns: actorMode ? 'repeat(auto-fill,minmax(150px,1fr))' : 'repeat(auto-fill,minmax(190px,1fr))', gap: 1.25 }}>
      {items.map((item) => <Card key={item.id} sx={{ display: 'flex', flexDirection: 'column' }}><CardActionArea onClick={() => open(item)} sx={{ flex: 1 }}><CardContent sx={{ display: 'flex', flexDirection: actorMode ? 'column' : 'row', alignItems: 'center', gap: 1.25, textAlign: actorMode ? 'center' : 'left' }}>
        {actorMode ? <Avatar src={item.imageUrl} alt={readable(item.name)} sx={{ width: 82, height: 82, bgcolor: 'action.selected' }}><GroupsRoundedIcon/></Avatar> : <Box sx={{ width: 38, height: 38, borderRadius: 2, bgcolor: 'action.hover', color: 'primary.main', display: 'grid', placeItems: 'center' }}><LocalOfferRoundedIcon/></Box>}
        <Box sx={{ minWidth: 0, flex: 1 }}><Typography noWrap sx={{ fontWeight: 800 }}>{readable(item.name)}</Typography><Typography variant="body2" color="text.secondary">{item.movieCount} 部影片</Typography></Box>
      </CardContent></CardActionArea><CardActions sx={{ justifyContent: 'flex-end', pt: 0 }}><Tooltip title="编辑"><IconButton size="small" onClick={() => startEdit(item)}><EditRoundedIcon fontSize="small"/></IconButton></Tooltip>{!actorMode && <Tooltip title="删除"><IconButton size="small" color="error" onClick={() => requestDelete(item)}><DeleteOutlineRoundedIcon fontSize="small"/></IconButton></Tooltip>}</CardActions></Card>)}
    </Box> : <EmptyState title="没有匹配内容" description="尝试清除搜索条件。"/>}
    {total > pageSize && <Stack sx={{ pt: 3, alignItems: 'center' }}><Pagination count={Math.ceil(total / pageSize)} page={page} onChange={(_, value) => setPage(value)} color="primary"/></Stack>}
    {notice && <Alert severity="info" onClose={() => setNotice('')} action={undoAudit ? <Button color="inherit" size="small" onClick={undoDelete}>撤销</Button> : undefined} sx={{ mt: 2 }}>{notice}</Alert>}
    <Dialog open={Boolean(editItem)} onClose={() => { setEditItem(null); setCreating(false) }} fullWidth maxWidth={actorMode ? 'sm' : 'xs'}><DialogTitle>{creating ? '新建标签' : `编辑${actorMode ? '演员' : '标签'}`}</DialogTitle><DialogContent dividers><TextField autoFocus fullWidth margin="normal" label="名称" value={editName} onChange={(event) => setEditName(event.target.value)} slotProps={{ htmlInput: { maxLength: 100 } }}/>{actorMode && <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 160px' }, gap: 1.5 }}><TextField label="别名" value={actorAlias} onChange={(event) => setActorAlias(event.target.value)}/><TextField select label="性别" value={actorGender} onChange={(event) => setActorGender(event.target.value)}><MenuItem value="">未设置</MenuItem><MenuItem value="0">未知</MenuItem><MenuItem value="1">男</MenuItem><MenuItem value="2">女</MenuItem></TextField><TextField label="生日" type="date" value={actorBirthDate.slice(0, 10)} onChange={(event) => setActorBirthDate(event.target.value)} slotProps={{ inputLabel: { shrink: true } }}/><TextField label="简介" multiline minRows={3} value={actorDescription} onChange={(event) => setActorDescription(event.target.value)} sx={{ gridColumn: { sm: '1 / -1' } }}/></Box>}</DialogContent><DialogActions><Button onClick={() => { setEditItem(null); setCreating(false) }}>取消</Button><Button variant="contained" disabled={!editName.trim()} onClick={saveEdit}>保存</Button></DialogActions></Dialog>
    <Dialog open={Boolean(deletePreview)} onClose={() => setDeletePreview(null)} fullWidth maxWidth="sm"><DialogTitle>删除标签？</DialogTitle><DialogContent dividers><DialogContentText>“{deletePreview?.item.name}”关联 {deletePreview?.count ?? 0} 部影片。删除只会解除标签关系，不会删除影片；操作会写入审计记录。</DialogContentText></DialogContent><DialogActions><Button onClick={() => setDeletePreview(null)}>取消</Button><Button color="error" variant="contained" onClick={confirmDelete}>确认删除</Button></DialogActions></Dialog>
    <Dialog open={Boolean(actorRepair)} onClose={() => setActorRepair(undefined)} fullWidth maxWidth="sm"><DialogTitle>修复演员关系？</DialogTitle><DialogContent dividers><Alert severity="warning" sx={{ mb: 1.5 }}>将修复 {actorRepair?.affectedRelations ?? 0} 条 ActorID=0 关系。</Alert>{actorRepair?.warnings.map((warning) => <Typography key={warning} variant="body2">{warning}</Typography>)}</DialogContent><DialogActions><Button onClick={() => setActorRepair(undefined)}>取消</Button><Button color="warning" variant="contained" onClick={confirmActorRepair}>确认修复</Button></DialogActions></Dialog>
  </WorkspacePage>
}
