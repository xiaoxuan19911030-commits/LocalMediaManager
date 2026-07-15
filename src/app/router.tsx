import { createHashRouter } from 'react-router'
import AppShell from '@/layouts/AppShell'
import HomePage from '@/pages/HomePage'
import MediaPage from '@/pages/MediaPage'
import PlaceholderPage from '@/pages/PlaceholderPage'
import SettingsPage from '@/pages/SettingsPage'
import LibrariesPage from '@/pages/LibrariesPage'
import MovieDetailPage from '@/pages/MovieDetailPage'
import SearchPage from '@/pages/SearchPage'
import TasksPage from '@/pages/TasksPage'

export const router = createHashRouter([{ path: '/', Component: AppShell, children: [
  { index: true, Component: HomePage },
  { path: 'media', Component: MediaPage },
  { path: 'movies/:id', Component: MovieDetailPage },
  { path: 'search', Component: SearchPage },
  { path: 'libraries', Component: LibrariesPage },
  { path: 'tasks', Component: TasksPage },
  { path: 'settings', Component: SettingsPage },
  ...['tags', 'actors', 'favorites', 'history', 'plugins'].map((path) => ({
    path,
    element: <PlaceholderPage title={({ libraries: '媒体库', tags: '标签', actors: '演员', favorites: '收藏', history: '最近播放', tasks: '任务', plugins: '插件', settings: '设置' } as Record<string,string>)[path]} />,
  })),
]}])
