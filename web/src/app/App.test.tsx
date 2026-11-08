import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthSession } from '../auth/session';
import { messages } from '../i18n';
import { App } from './App';

const user = { id: 'u1', firstName: 'Deneme', lastName: 'Kullanıcı', email: 'deneme@example.test' };
const accessToken = 'secret-access-token-value';

type Route = (init: RequestInit | undefined) => Response;

function pathOf(input: RequestInfo | URL): string {
  return typeof input === 'string' ? input : input instanceof URL ? input.pathname : input.url;
}

function json(status: number, body: unknown, type = 'application/json', headers: Record<string, string> = {}) {
  return new Response(JSON.stringify(body), { status, headers: { 'content-type': type, ...headers } });
}

const signedIn = () => json(200, { accessToken, expiresAt: '2030-01-01T00:00:00Z', user });
const noSession = () => json(401, { status: 401, code: 'unauthorized' }, 'application/problem+json');

function serve(routes: Record<string, Route>) {
  const fetchFn = vi.fn<typeof fetch>((input, init) => {
    const path = pathOf(input);
    const route = routes[path];
    if (!route) return Promise.reject(new TypeError(`No route for ${path}`));
    return Promise.resolve(route(init));
  });
  vi.stubGlobal('fetch', fetchFn);
  return fetchFn;
}

function renderApp(path: string) {
  window.history.replaceState(null, '', path);
  const session = new AuthSession({ locks: null, createChannel: null, sleep: () => Promise.resolve() });
  render(<App session={session} />);
  return session;
}

async function fillAndSubmit(email: string, password: string) {
  fireEvent.change(await screen.findByLabelText(messages.login.email), { target: { value: email } });
  fireEvent.change(screen.getByLabelText(messages.login.password), { target: { value: password } });
  fireEvent.click(screen.getByRole('button', { name: messages.login.submit }));
}

beforeEach(() => {
  window.localStorage.clear();
  window.sessionStorage.clear();
});

afterEach(() => {
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

describe('routing', () => {
  it('sends a signed-out visitor from the home page to sign-in', async () => {
    serve({ '/health/live': () => json(200, {}), '/api/auth/refresh': noSession });

    renderApp('/');

    expect(await screen.findByRole('heading', { level: 1, name: messages.login.title })).toBeTruthy();
    expect(window.location.pathname).toBe('/login');
  });

  it('restores the session silently on page load and shows the home page', async () => {
    serve({
      '/health/live': () => json(200, {}),
      '/api/auth/refresh': signedIn,
      '/api/auth/me': () => json(200, user),
    });

    renderApp('/');

    expect(await screen.findByRole('heading', { name: messages.home.greeting('Deneme') })).toBeTruthy();
    expect(window.location.pathname).toBe('/');
  });

  it('sends a signed-in user away from the sign-in page', async () => {
    serve({ '/health/live': () => json(200, {}), '/api/auth/refresh': signedIn, '/api/auth/me': () => json(200, user) });

    renderApp('/login');

    expect(await screen.findByRole('heading', { name: messages.home.greeting('Deneme') })).toBeTruthy();
    expect(window.location.pathname).toBe('/');
  });

  it('signs out and returns to the sign-in page', async () => {
    const fetchFn = serve({
      '/health/live': () => json(200, {}),
      '/api/auth/refresh': signedIn,
      '/api/auth/me': () => json(200, user),
      '/api/auth/logout': () => new Response(null, { status: 204 }),
    });
    renderApp('/');

    fireEvent.click(await screen.findByRole('button', { name: messages.home.signOut }));

    expect(await screen.findByRole('heading', { level: 1, name: messages.login.title })).toBeTruthy();
    expect(fetchFn.mock.calls.some(([input]) => pathOf(input) === '/api/auth/logout')).toBe(true);
  });
});

describe('sign-in form', () => {
  beforeEach(() => {
    serve({ '/health/live': () => json(200, {}), '/api/auth/refresh': noSession });
  });

  it('labels its fields and checks them before sending', async () => {
    const fetchFn = vi.mocked(fetch);
    renderApp('/login');

    fireEvent.click(await screen.findByRole('button', { name: messages.login.submit }));

    const email = screen.getByLabelText(messages.login.email);
    expect(email.getAttribute('type')).toBe('email');
    expect(email.getAttribute('autocomplete')).toBe('username');
    expect(email.getAttribute('aria-invalid')).toBe('true');
    expect(screen.getByText(messages.login.emailRequired).id).toBe(email.getAttribute('aria-describedby'));
    expect(screen.getByText(messages.login.passwordRequired)).toBeTruthy();
    expect(document.activeElement).toBe(email);
    expect(fetchFn.mock.calls.some(([input]) => pathOf(input) === '/api/auth/login')).toBe(false);

    fireEvent.change(email, { target: { value: 'not-an-email' } });
    fireEvent.click(screen.getByRole('button', { name: messages.login.submit }));
    expect(screen.getByText(messages.login.emailInvalid)).toBeTruthy();
  });

  it('shows the Turkish text for the problem code', async () => {
    serve({
      '/health/live': () => json(200, {}),
      '/api/auth/refresh': noSession,
      '/api/auth/login': () => json(401, { status: 401, code: 'unauthorized' }, 'application/problem+json'),
    });
    renderApp('/login');

    await fillAndSubmit('deneme@example.test', 'yanlis-parola');

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain(messages.errors.codes.unauthorized);
    expect(screen.getByLabelText<HTMLInputElement>(messages.login.password).value).toBe('');
  });

  it('marks the fields named in a validation problem', async () => {
    serve({
      '/health/live': () => json(200, {}),
      '/api/auth/refresh': noSession,
      '/api/auth/login': () =>
        json(
          400,
          { status: 400, code: 'validation_failed', errors: { Email: ['The supplied value is invalid.'] } },
          'application/problem+json',
        ),
    });
    renderApp('/login');

    await fillAndSubmit('deneme@example.test', 'parola');

    expect((await screen.findByRole('alert')).textContent).toContain(messages.errors.codes.validation_failed);
    expect(screen.getByLabelText(messages.login.email).getAttribute('aria-invalid')).toBe('true');
    expect(screen.getByText(messages.login.fieldInvalid)).toBeTruthy();
  });

  it('adds the support code to an unexpected error', async () => {
    serve({
      '/health/live': () => json(200, {}),
      '/api/auth/refresh': noSession,
      '/api/auth/login': () =>
        json(500, { status: 500, code: 'internal_error', traceId: '00-abc-01' }, 'application/problem+json'),
    });
    renderApp('/login');

    await fillAndSubmit('deneme@example.test', 'parola');

    expect((await screen.findByRole('alert')).textContent).toContain(messages.errors.supportCode('00-abc-01'));
  });

  it('reports a connection error when the answer is not a problem document', async () => {
    serve({
      '/health/live': () => json(200, {}),
      '/api/auth/refresh': noSession,
      '/api/auth/login': () => new Response('<html>Bad gateway</html>', { status: 502, headers: { 'content-type': 'text/html' } }),
    });
    renderApp('/login');

    await fillAndSubmit('deneme@example.test', 'parola');

    expect((await screen.findByRole('alert')).textContent).toContain(messages.errors.network);
  });

  it('waits for Retry-After after a 429 and then allows another attempt', async () => {
    serve({
      '/health/live': () => json(200, {}),
      '/api/auth/refresh': noSession,
      '/api/auth/login': () =>
        json(429, { status: 429, code: 'rate_limited' }, 'application/problem+json', { 'Retry-After': '3' }),
    });
    renderApp('/login');
    await fillAndSubmit('deneme@example.test', 'parola');

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain(messages.login.rateLimited(3));
    const button = screen.getByRole<HTMLButtonElement>('button', { name: messages.login.waitCountdown(3) });
    expect(button.disabled).toBe(true);

    await waitFor(
      () => {
        expect(screen.getByRole('alert').textContent).toContain(messages.login.rateLimitOver);
      },
      { timeout: 5000 },
    );
    expect(screen.getByRole<HTMLButtonElement>('button', { name: messages.login.submit }).disabled).toBe(false);
  });

  it('sends once when submitted twice in a row', async () => {
    let answer: (response: Response) => void = () => undefined;
    const fetchFn = serve({
      '/health/live': () => json(200, {}),
      '/api/auth/refresh': noSession,
    });
    fetchFn.mockImplementation((input) => {
      const path = pathOf(input);
      if (path === '/health/live') return Promise.resolve(json(200, {}));
      if (path === '/api/auth/refresh') return Promise.resolve(noSession());
      return new Promise<Response>((resolve) => {
        answer = resolve;
      });
    });
    renderApp('/login');
    fireEvent.change(await screen.findByLabelText(messages.login.email), { target: { value: 'deneme@example.test' } });
    fireEvent.change(screen.getByLabelText(messages.login.password), { target: { value: 'parola' } });
    const form = screen.getByRole('button', { name: messages.login.submit }).closest('form');
    if (!form) throw new Error('No form.');

    fireEvent.submit(form);
    fireEvent.submit(form);
    fireEvent.submit(form);

    expect(fetchFn.mock.calls.filter(([input]) => pathOf(input) === '/api/auth/login')).toHaveLength(1);
    expect(screen.getByRole('button', { name: messages.login.submitting })).toBeTruthy();
    await act(async () => {
      answer(json(401, { status: 401, code: 'unauthorized' }, 'application/problem+json'));
      await Promise.resolve();
    });
  });
});

describe('token storage', () => {
  it('never writes the access token to Web Storage, cookies or IndexedDB', async () => {
    const setItem = vi.spyOn(Storage.prototype, 'setItem');
    const cookie = Object.getOwnPropertyDescriptor(Document.prototype, 'cookie');
    const cookieWrites: string[] = [];
    Object.defineProperty(document, 'cookie', {
      configurable: true,
      get: () => cookie?.get?.call(document) as string,
      set: (value: string) => {
        cookieWrites.push(value);
      },
    });
    const indexedDbOpen = vi.fn();
    vi.stubGlobal('indexedDB', { open: indexedDbOpen });
    serve({
      '/health/live': () => json(200, {}),
      '/api/auth/refresh': noSession,
      '/api/auth/login': signedIn,
      '/api/auth/me': () => json(200, user),
    });

    try {
      renderApp('/login');
      await fillAndSubmit('deneme@example.test', 'parola');
      expect(await screen.findByRole('heading', { name: messages.home.greeting('Deneme') })).toBeTruthy();

      expect(setItem).not.toHaveBeenCalled();
      expect(window.localStorage.length).toBe(0);
      expect(window.sessionStorage.length).toBe(0);
      expect(cookieWrites).toEqual([]);
      expect(document.cookie).not.toContain(accessToken);
      expect(indexedDbOpen).not.toHaveBeenCalled();
    } finally {
      delete (document as { cookie?: string }).cookie;
    }
  });
});
