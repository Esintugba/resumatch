import { BrowserRouter, Navigate, Route, Routes } from 'react-router';
import { apiBaseUrl } from '../api/config';
import { AuthProvider } from '../auth/AuthContext';
import { RequireAuth } from '../auth/RequireAuth';
import type { AuthSession } from '../auth/session';
import { ServerWakeGate } from '../components/ServerWakeGate';
import { HomePage } from '../features/home/HomePage';
import { LoginPage } from '../features/login/LoginPage';
import { messages } from '../i18n';

export function App({ session }: { session: AuthSession }) {
  return (
    <BrowserRouter>
      <a className="skip-link" href="#main">
        {messages.skipToContent}
      </a>
      <header className="site-header">
        <span className="brand">{messages.appName}</span>
      </header>
      <main id="main" className="site-main">
        {/* The session check waits for the API, so a cold start shows the waking message first. */}
        <ServerWakeGate baseUrl={apiBaseUrl}>
          <AuthProvider session={session}>
            <AppRoutes />
          </AuthProvider>
        </ServerWakeGate>
      </main>
    </BrowserRouter>
  );
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/"
        element={
          <RequireAuth>
            <HomePage />
          </RequireAuth>
        }
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}
