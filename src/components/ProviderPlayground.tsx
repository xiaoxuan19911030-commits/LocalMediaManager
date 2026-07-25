import DeleteSweepRoundedIcon from '@mui/icons-material/DeleteSweepRounded'
import PlayArrowRoundedIcon from '@mui/icons-material/PlayArrowRounded'
import RefreshRoundedIcon from '@mui/icons-material/RefreshRounded'
import { Alert, Box, Button, Chip, Divider, LinearProgress, List, ListItemButton, ListItemText, Paper, Stack, TextField, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { SurfaceSection } from '@/components/ProductComponents'
import { StatusBadge } from '@/components/workspace/StatusBadges'
import { bridge } from '@/services/bridge'
import type { ProviderDashboardItem, ProviderHealthStatus, ProviderPlaygroundResult } from '@/types/providerEngine'

type View = 'raw' | 'result' | 'merge' | 'diagnostics'

export function ProviderPlayground() {
  const [providers, setProviders] = useState<ProviderDashboardItem[]>([])
  const [selected, setSelected] = useState('MetaTube')
  const [code, setCode] = useState('SSIS-845')
  const [result, setResult] = useState<ProviderPlaygroundResult>()
  const [view, setView] = useState<View>('result')
  const [rawFormat, setRawFormat] = useState<'json' | 'html' | 'xml'>('json')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const load = useCallback(async () => {
    setError('')
    try {
      const value = await bridge.providerDashboard()
      setProviders(value)
      if (value.length > 0 && !value.some(item => item.descriptor.name === selected)) setSelected(value[0].descriptor.name)
    } catch (reason) { setError((reason as Error).message) }
  }, [selected])

  useEffect(() => { void load() }, [load])

  const run = async () => {
    if (!code.trim()) return
    setBusy(true); setError(''); setNotice('')
    try {
      const value = await bridge.testProvider(selected, code.trim())
      setResult(value)
      setView('result')
      const firstFormat = value.rawResponses[0]?.format
      if (firstFormat) setRawFormat(firstFormat)
      await load()
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const clearCache = async () => {
    setBusy(true); setError('')
    try {
      const value = await bridge.clearProviderCache()
      setNotice(`已清除 ${value.clearedEntries} 条 Provider 缓存。`)
    } catch (reason) { setError((reason as Error).message) }
    finally { setBusy(false) }
  }

  const selectedDashboard = providers.find(item => item.descriptor.name === selected)
  const raw = result?.rawResponses.filter(item => item.format === rawFormat).map(item => item.content).join('\n\n') ?? ''
  const panel = useMemo(() => {
    if (!result) return ''
    if (view === 'raw') return raw || `本次响应没有 ${rawFormat.toUpperCase()} 内容。`
    if (view === 'result') return JSON.stringify(result.providerResult ?? { error: result.error, failureCategory: result.failureCategory }, null, 2)
    if (view === 'merge') return JSON.stringify(result.mergePreview, null, 2)
    return JSON.stringify(result.diagnostics, null, 2)
  }, [raw, rawFormat, result, view])

  return <Stack spacing={2}>
    {error && <Alert severity="error">{error}</Alert>}
    {notice && <Alert severity="success">{notice}</Alert>}
    <SurfaceSection title="Provider Playground" description="按番号检查 Provider 原始响应、解析结果、合并预览和诊断；测试不会写入影片数据库。"
      action={<Stack direction="row" spacing={1}><Button size="small" startIcon={<DeleteSweepRoundedIcon/>} disabled={busy} onClick={() => void clearCache()}>清空缓存</Button><Button size="small" startIcon={<RefreshRoundedIcon/>} disabled={busy} onClick={() => void load()}>刷新</Button></Stack>}>
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: '240px minmax(0, 1fr)' }, gap: 2 }}>
        <Paper variant="outlined" sx={{ p: 1, alignSelf: 'start' }}>
          <List dense disablePadding>{providers.map(item => <ListItemButton key={item.descriptor.name} selected={selected === item.descriptor.name}
            onClick={() => { setSelected(item.descriptor.name); setResult(undefined) }} sx={{ borderRadius: 1 }}>
            <ListItemText primary={item.descriptor.name} secondary={`${statusLabel(item.status)} · P${item.descriptor.priority}`}/>
          </ListItemButton>)}</List>
        </Paper>
        <Stack spacing={2} sx={{ minWidth: 0 }}>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} sx={{ alignItems: { sm: 'center' } }}>
            <TextField size="small" fullWidth label="影片番号" value={code} onChange={event => setCode(event.target.value)} onKeyDown={event => { if (event.key === 'Enter') void run() }}/>
            <Button variant="contained" startIcon={<PlayArrowRoundedIcon/>} disabled={busy || !code.trim()} onClick={() => void run()} sx={{ minWidth: 108 }}>{busy ? '测试中' : '测试'}</Button>
          </Stack>
          {busy && <LinearProgress/>}
          {selectedDashboard && <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }}>
            <StatusBadge tone={statusTone(selectedDashboard.status)} label={statusLabel(selectedDashboard.status)}/>
            <Chip size="small" label={`Capability ${selectedDashboard.descriptor.capabilities.length}`}/>
            <Chip size="small" label={`平均 ${selectedDashboard.benchmark.averageElapsedMilliseconds.toFixed(0)} ms`}/>
            <Chip size="small" label={`成功率 ${selectedDashboard.benchmark.successRate.toFixed(0)}%`}/>
            <Chip size="small" label={`Parser ${selectedDashboard.benchmark.parserSuccessRate.toFixed(0)}%`}/>
          </Stack>}
          {selectedDashboard && <Stack direction="row" spacing={.75} useFlexGap sx={{ flexWrap: 'wrap' }}>
            {selectedDashboard.descriptor.capabilities.map(value => <Chip key={value} size="small" variant="outlined" label={capabilityLabel(value)}/>)}
          </Stack>}
          {result && <>
            <Divider/>
            <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap', alignItems: 'center' }}>
              <StatusBadge tone={result.parserSucceeded ? 'success' : 'error'} label={result.parserSucceeded ? '解析成功' : '解析失败'}/>
              <Chip size="small" label={`HTTP ${result.httpStatus ?? '-'}`}/>
              <Chip size="small" label={`${result.elapsedMilliseconds} ms`}/>
              <Chip size="small" label={`${result.fieldCount} 个字段`}/>
              <Chip size="small" color={result.cache.hit ? 'info' : 'default'} label={result.cache.hit ? '缓存命中' : '实时请求'}/>
            </Stack>
            {result.error && <Alert severity="warning">{result.failureCategory}: {result.error}</Alert>}
            <ToggleButtonGroup size="small" exclusive value={view} onChange={(_, value: View | null) => value && setView(value)} sx={{ flexWrap: 'wrap' }}>
              <ToggleButton value="raw">Raw</ToggleButton><ToggleButton value="result">ProviderResult</ToggleButton><ToggleButton value="merge">Merge Preview</ToggleButton><ToggleButton value="diagnostics">Diagnostics</ToggleButton>
            </ToggleButtonGroup>
            {view === 'raw' && <ToggleButtonGroup size="small" exclusive value={rawFormat} onChange={(_, value) => value && setRawFormat(value)}>
              <ToggleButton value="json">JSON</ToggleButton><ToggleButton value="html">HTML</ToggleButton><ToggleButton value="xml">XML</ToggleButton>
            </ToggleButtonGroup>}
            <Paper variant="outlined" sx={{ p: 1.5, maxHeight: 480, overflow: 'auto', bgcolor: 'action.hover' }}>
              <Typography component="pre" variant="body2" sx={{ m: 0, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere', fontFamily: 'monospace' }}>{panel}</Typography>
            </Paper>
          </>}
        </Stack>
      </Box>
    </SurfaceSection>
    <SurfaceSection title="Provider Dashboard" description="当前进程的请求、解析和性能统计。">
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, minmax(0, 1fr))' }, gap: 1.5 }}>
        {providers.map(item => <Paper key={item.descriptor.name} variant="outlined" sx={{ p: 1.5 }}>
          <Stack spacing={1}>
            <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center' }}><Typography sx={{ fontWeight: 800 }}>{item.descriptor.name}</Typography><StatusBadge tone={statusTone(item.status)} label={statusLabel(item.status)}/></Stack>
            <Typography variant="body2" color="text.secondary">请求 {item.benchmark.requestCount} · 成功 {item.benchmark.successCount} · 失败 {item.benchmark.failureCount}</Typography>
            <Typography variant="body2" color="text.secondary">最近请求：{formatTime(item.benchmark.lastRequestAt)}</Typography>
            {item.benchmark.lastError && <Typography variant="body2" color="error.main" sx={{ overflowWrap: 'anywhere' }}>{item.benchmark.lastFailureCategory}: {item.benchmark.lastError}</Typography>}
          </Stack>
        </Paper>)}
      </Box>
    </SurfaceSection>
  </Stack>
}

function statusLabel(value: ProviderHealthStatus) {
  return ({ Available: '可用', Partial: '部分可用', Offline: '离线', AuthenticationRequired: '需要认证', RateLimited: '已限速', ConfigurationError: '配置错误', ParserBroken: '解析异常', Unsupported: '未启用' } as const)[value] ?? value
}

function statusTone(value: ProviderHealthStatus): 'success' | 'warning' | 'error' | 'neutral' {
  if (value === 'Available') return 'success'
  if (value === 'Partial' || value === 'RateLimited') return 'warning'
  if (value === 'Unsupported') return 'neutral'
  return 'error'
}

function capabilityLabel(value: string) {
  return ({ Title: '标题', OriginalTitle: '原标题', Actors: '演员', Genres: '类型', Director: '导演', Studio: '片商', Publisher: '发行商', Series: '系列', ReleaseDate: '发行日期', Duration: '时长', Description: '简介', Plot: '简介', Poster: '封面', Fanart: '背景图', Preview: '预览图', Rating: '评分', NFO: 'NFO', SourceURL: '来源地址', SearchByCode: '按番号', SearchByTitle: '按标题', SearchByFile: '按文件', SearchByPath: '按路径' } as Record<string, string>)[value] ?? value
}

function formatTime(value?: string) { return value ? new Date(value).toLocaleString() : '尚无' }
