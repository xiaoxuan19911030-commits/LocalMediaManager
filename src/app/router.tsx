import { createHashRouter } from 'react-router'
import AppShell from '@/layouts/AppShell'
import HomePage from '@/pages/HomePage'
import MediaPage from '@/pages/MediaPage'
import SettingsPage from '@/pages/SettingsPage'
import LibrariesPage from '@/pages/LibrariesPage'
import MovieDetailPage from '@/pages/MovieDetailPage'
import SearchPage from '@/pages/SearchPage'
import TasksPage from '@/pages/TasksPage'
import EntityPage from '@/pages/EntityPage'
import CollectionPage from '@/pages/CollectionPage'
import MetadataPage from '@/pages/MetadataPage'
import DiagnosticsPage from '@/pages/DiagnosticsPage'
import PluginsPage from '@/pages/PluginsPage'
import AiProvidersPage from '@/pages/AiProvidersPage'

export const router = createHashRouter([{ path: '/', Component: AppShell, children: [
  { index: true, Component: HomePage },
  { path: 'media', Component: MediaPage },
  { path: 'movies/:id', Component: MovieDetailPage },
  { path: 'search', Component: SearchPage },
  { path: 'libraries', Component: LibrariesPage },
  { path: 'tags', element: <EntityPage type="tags" /> },
  { path: 'actors', element: <EntityPage type="actors" /> },
  { path: 'favorites', element: <CollectionPage kind="favorites" /> },
  { path: 'history', element: <CollectionPage kind="history" /> },
  { path: 'metadata', Component: MetadataPage },
  { path: 'diagnostics', Component: DiagnosticsPage },
  { path: 'tasks', Component: TasksPage },
  { path: 'plugins', Component: PluginsPage },
  { path: 'ai-providers', Component: AiProvidersPage },
  { path: 'settings', Component: SettingsPage },
]}])
