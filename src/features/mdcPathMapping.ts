import type { MdcNgPathMapping } from '@/types/settings'

export interface MdcPathMappingTestResult {
  status: 'matched' | 'unmatched'
  providerPath?: string
  mapping?: MdcNgPathMapping
}

const normalizeLocal = (value: string) => value.trim().replaceAll('/', '\\').replace(/\\+$/, '')
const normalizeProvider = (value: string) => value.trim().replace(/\\/g, '/').replace(/\/+$/, '') || '/'

export function testMdcPathMapping(path: string, mappings: MdcNgPathMapping[]): MdcPathMappingTestResult {
  const input = normalizeLocal(path)
  const match = mappings
    .filter(item => item.enabled)
    .map(item => ({ item, prefix: normalizeLocal(item.localPathPrefix) }))
    .filter(({ prefix }) => input.localeCompare(prefix, undefined, { sensitivity: 'accent' }) === 0
      || (input.length > prefix.length && input.slice(0, prefix.length).localeCompare(prefix, undefined, { sensitivity: 'accent' }) === 0 && input[prefix.length] === '\\'))
    .sort((left, right) => right.prefix.length - left.prefix.length || left.item.order - right.item.order)[0]

  if (!match) return { status: 'unmatched' }
  const relative = input.slice(match.prefix.length).replace(/^\\+/, '').replaceAll('\\', '/')
  const providerPrefix = normalizeProvider(match.item.providerPathPrefix)
  return {
    status: 'matched',
    mapping: match.item,
    providerPath: relative ? `${providerPrefix === '/' ? '' : providerPrefix}/${relative}` : providerPrefix,
  }
}
