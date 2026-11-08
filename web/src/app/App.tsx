import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { createBrowserRouter, Navigate, RouterProvider, type RouteObject } from 'react-router';
import { apiBaseUrl } from '../api/config';
import { createQueryClient } from '../api/queryClient';
import { AuthProvider } from '../auth/AuthContext';
import { RequireAuth } from '../auth/RequireAuth';
import type { AuthSession } from '../auth/session';
import { ServerWakeGate } from '../components/ServerWakeGate';
import { LoginPage } from '../features/login/LoginPage';
import { ResumeEditorPage } from '../features/resumes/ResumeEditorPage';
import { ResumeListPage } from '../features/resumes/ResumeListPage';
import { RootLayout } from './RootLayout';

export const routes: RouteObject[] = [
  {
    element: <RootLayout />,
    children: [
      { path: '/login', element: <LoginPage /> },
      { path: '/', element: <Navigate to="/app/resumes" replace /> },
      {
        path: '/app/resumes',
        element: (
          <RequireAuth>
            <ResumeListPage />
          </RequireAuth>
        ),
      },
      {
        path: '/app/resumes/:resumeId',
        element: (
          <RequireAuth>
            <ResumeEditorPage />
          </RequireAuth>
        ),
      },
      { path: '*', element: <Navigate to="/" replace /> },
    ],
  },
];

export function App({ session, queryClient }: { session: AuthSession; queryClient?: QueryClient }) {
  const [client] = useState(() => queryClient ?? createQueryClient());
  // A data router, because the editor blocks navigation while a form has unsaved changes.
  const [router] = useState(() => createBrowserRouter(routes));

  return (
    <QueryClientProvider client={client}>
      {/* The session check waits for the API, so a cold start shows the waking message first. */}
      <ServerWakeGate baseUrl={apiBaseUrl}>
        <AuthProvider session={session}>
          <RouterProvider router={router} />
        </AuthProvider>
      </ServerWakeGate>
    </QueryClientProvider>
  );
}
