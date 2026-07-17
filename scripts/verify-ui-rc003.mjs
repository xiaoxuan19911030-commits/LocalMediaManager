import { readFileSync } from 'node:fs'

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8')
const assert = (condition, message) => {
  if (!condition) {
    throw new Error(message)
  }
}

const workspacePages = [
  'src/pages/EntityPage.tsx',
  'src/pages/DiagnosticsPage.tsx',
  'src/pages/PluginsPage.tsx',
  'src/pages/AiProvidersPage.tsx',
  'src/pages/PlaceholderPage.tsx',
]

for (const page of workspacePages) {
  const source = read(page)
  assert(source.includes('WorkspacePage'), `${page} must render through WorkspacePage`)
  assert(!source.includes('PageHeader'), `${page} must not keep legacy PageHeader`)
}

const entityPage = read('src/pages/EntityPage.tsx')
assert(!entityPage.includes('window.confirm'), 'EntityPage must use a shared Dialog instead of window.confirm')
assert(entityPage.includes('MovieResultContainer'), 'EntityPage associated movies must reuse MovieResultContainer')

const movieResults = read('src/components/workspace/MovieResults.tsx')
assert(movieResults.includes('MetadataStatusBadge'), 'Movie list metadata status must use MetadataStatusBadge')
assert(movieResults.includes('StatusBadge'), 'Movie list favorite state must use StatusBadge')

const viteConfig = read('vite.config.ts')
assert(viteConfig.includes('git rev-parse --short HEAD'), 'Build info must derive commit from git')
assert(viteConfig.includes('VITE_LMM_BUILD_TIME'), 'Build info must include build time')

const appShell = read('src/layouts/AppShell.tsx')
assert(appShell.includes('shortBuildLabel'), 'AppShell must expose version and commit in the installed UI')

const tauriMain = read('src-tauri/src/main.rs')
assert(tauriMain.includes('windows_subsystem = "windows"'), 'Release Tauri app must not open a console window')

const settings = read('src/pages/SettingsPage.tsx')
assert(settings.includes('Build: {buildInfo.buildTime}'), 'Settings About must show build time')
assert(settings.includes('Commit: {buildInfo.commit}'), 'Settings About must show commit')

const badges = read('src/components/workspace/StatusBadges.tsx')
for (const status of ['Pending', 'Running', 'Completed', 'Failed', 'Cancelled']) {
  assert(badges.includes(status), `Task status badge mapping must include ${status}`)
}

console.log('RC-003 UI source verification passed')
