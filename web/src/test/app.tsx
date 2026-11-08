import { QueryClient } from '@tanstack/react-query';
import { render } from '@testing-library/react';
import { vi } from 'vitest';
import { App } from '../app/App';
import { AuthSession } from '../auth/session';

/** A made-up user; tests never use real data. */
export const testUser = { id: 'u1', firstName: 'Deneme', lastName: 'Kullanıcı', email: 'deneme@example.test' };

export function pathOf(input: RequestInfo | URL): string {
  return typeof input === 'string' ? input : input instanceof URL ? input.pathname + input.search : input.url;
}

export function json(status: number, body: unknown, type = 'application/json', headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': type, ...headers } });
}

export function problem(status: number, body: Record<string, unknown>) {
  return json(status, { status, ...body }, 'application/problem+json');
}

export const signedIn = () => json(200, { accessToken: 'test-token', expiresAt: '2030-01-01T00:00:00Z', user: testUser });

export interface Call {
  method: string;
  path: string;
  headers: Headers;
  body: unknown;
}

type Handler = (call: Call) => Response | Promise<Response>;

/**
 * Stubs fetch with routes keyed by "METHOD path" (path with query). Every
 * call is recorded; an unknown route fails like a dropped connection.
 */
export function serveApi(routes: Record<string, Handler>) {
  const calls: Call[] = [];
  const fetchFn = vi.fn<typeof fetch>(async (input, init) => {
    const call: Call = {
      method: (init?.method ?? 'GET').toUpperCase(),
      path: pathOf(input),
      headers: new Headers(init?.headers),
      body: typeof init?.body === 'string' ? (JSON.parse(init.body) as unknown) : undefined,
    };
    calls.push(call);
    const handler = routes[`${call.method} ${call.path}`];
    if (!handler) throw new TypeError(`No route for ${call.method} ${call.path}`);
    return handler(call);
  });
  vi.stubGlobal('fetch', fetchFn);
  return { calls, callsTo: (method: string, path: string) => calls.filter((c) => c.method === method && c.path === path) };
}

/** Renders the whole app at a path with a signed-in session and no query retries. */
export function renderApp(path: string) {
  window.history.replaceState(null, '', path);
  const session = new AuthSession({
    locks: null,
    createChannel: null,
    sleep: () => Promise.resolve(),
    onOnline: null,
    signOutMarker: { isSet: () => false, set: () => undefined, clear: () => undefined },
  });
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false }, mutations: { retry: false } } });
  return { session, queryClient, ...render(<App session={session} queryClient={queryClient} />) };
}

/** The base routes every signed-in page needs. */
export const signedInRoutes: Record<string, Handler> = {
  'GET /health/live': () => json(200, {}),
  'POST /api/auth/refresh': signedIn,
};
