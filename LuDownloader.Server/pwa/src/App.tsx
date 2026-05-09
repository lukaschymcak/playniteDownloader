import { BrowserRouter, Routes, Route, NavLink } from 'react-router-dom'
import LibraryView from './views/LibraryView'
import TasksView from './views/TasksView'
import SearchView from './views/SearchView'
import SettingsView from './views/SettingsView'

export default function App(): JSX.Element {
  return (
    <BrowserRouter>
      <div style={{ minHeight: '100dvh', background: '#0B0C0F', color: '#fff', display: 'flex', flexDirection: 'column' }}>
        <div style={{ flex: 1, overflowY: 'auto', paddingBottom: '4rem' }}>
          <Routes>
            <Route path="/" element={<LibraryView />} />
            <Route path="/tasks" element={<TasksView />} />
            <Route path="/search" element={<SearchView />} />
            <Route path="/settings" element={<SettingsView />} />
          </Routes>
        </div>
        <nav style={{ position: 'fixed', bottom: 0, left: 0, right: 0, background: '#0d0f14', borderTop: '1px solid #1f2937', display: 'flex', justifyContent: 'space-around', padding: '0.5rem 0', zIndex: 100 }}>
          <NavItem to="/" label="Library" />
          <NavItem to="/tasks" label="Tasks" />
          <NavItem to="/search" label="Search" />
          <NavItem to="/settings" label="Settings" />
        </nav>
      </div>
    </BrowserRouter>
  )
}

function NavItem({ to, label }: { to: string; label: string }): JSX.Element {
  return (
    <NavLink
      to={to}
      end={to === '/'}
      style={({ isActive }) => ({
        color: isActive ? '#3b82f6' : '#9ca3af',
        textDecoration: 'none',
        fontSize: '0.75rem',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: '0.1rem',
        padding: '0.25rem 0.5rem',
      })}
    >
      {label}
    </NavLink>
  )
}
