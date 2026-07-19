import { Navigate, createHashRouter } from 'react-router'
import AppShell from '@/layouts/AppShell'
import HomePage from '@/pages/HomePage'
import MediaPage from '@/pages/MediaPage'
import SettingsPage from '@/pages/SettingsPage'
import LibrariesPage from '@/pages/LibrariesPage'
import MovieDetailPage from '@/pages/MovieDetailPage'
import SearchPage from '@/pages/SearchPage'
import TasksPage from '@/pages/TasksPage'
import EntityPage from '@/pages/EntityPage'
import TagCategoriesPage from '@/pages/TagCategoriesPage'
import CollectionPage from '@/pages/CollectionPage'
import MetadataPage from '@/pages/MetadataPage'
import DiagnosticsPage from '@/pages/DiagnosticsPage'
import OrganizerPage from '@/pages/OrganizerPage'
import MaintenancePage from '@/pages/MaintenancePage'
import AiProvidersPage from '@/pages/AiProvidersPage'

export const router = createHashRouter([{ path: '/', Component: AppShell, children: [
  { index: true, Component: HomePage },
  { path: 'media', Component: MediaPage },
  { path: 'movies/:id', Component: MovieDetailPage },
  { path: 'search', Component: SearchPage },
  { path: 'libraries', Component: LibrariesPage },
  { path: 'tags', Component: TagCategoriesPage },
  { path: 'tags/custom', element: <EntityPage type="tags" /> },
  { path: 'tags/movie-tags', element: <EntityPage type="movie-tags" /> },
  { path: 'tags/genres', element: <EntityPage type="genres" /> },
  { path: 'tags/directors', element: <EntityPage type="directors" /> },
  { path: 'tags/series', element: <EntityPage type="series" /> },
  { path: 'tags/studios', element: <EntityPage type="studios" /> },
  { path: 'actors', element: <EntityPage type="actors" /> },
  { path: 'favorites', element: <CollectionPage kind="favorites" /> },
  { path: 'history', element: <CollectionPage kind="history" /> },
  { path: 'metadata', Component: MetadataPage },
  { path: 'diagnostics', Component: DiagnosticsPage },
  { path: 'organizer', element: <Navigate to="/duplicates" replace /> },
  { path: 'duplicates', Component: OrganizerPage },
  { path: 'maintenance', Component: MaintenancePage },
  { path: 'tasks', Component: TasksPage },
  { path: 'plugins', element: <Navigate to="/settings" replace /> },
  { path: 'ai-providers', Component: AiProvidersPage },
  { path: 'settings', Component: SettingsPage },
]}])
