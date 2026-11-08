import { describe, expect, it, vi } from 'vitest';
import {
  AuthSession,
  authChannelName,
  parseRetryAfter,
  refreshLockName,
  type SessionDependencies,
  type SignOutMarker,
} from './session';

const user = { id: 'u1', firstName: 'Deneme', lastName: 'Kullanıcı', email: 'deneme@example.test' };
const tokenA = 'access-token-a';
const tokenB = 'access-token-b';

function auth(token: string) {
  return new Response(JSON.stringify({ accessToken: token, expiresAt: '2030-01-01T00:00:00Z', user }), {
    status: 200,
    headers: { 'content-type': 'application/json' },
  });
}

function problem(status: number, code: string, headers: Record<string, string> = {}) {
  return new Response(JSON.stringify({ status, code }), {
    status,
    headers: { 'content-type': 'application/problem+json', ...headers },
  });
}

/** Routes requests by path; each handler returns the next response for that path. */
function fakeApi(routes: Record<string, () => Response | Promise<Response>>) {
  const calls: { path: string; init: RequestInit | undefined }[] = [];
  const fetchFn = vi.fn<typeof fetch>((input, init) => {
    const path = typeof input === 'string' ? input : input instanceof URL ? input.pathname : input.url;
    calls.push({ path, init });
    const handler = routes[path];
    if (!handler) throw new Error(`Unexpected request to ${path}`);
    return Promise.resolve(handler());
  });
  return { fetchFn, calls, callsTo: (path: string) => calls.filter((call) => call.path === path) };
}

class FakeChannel {
  static all: FakeChannel[] = [];
  readonly sent: unknown[] = [];
  private listeners: ((event: MessageEvent) => void)[] = [];
  readonly name: string;
  constructor(name: string) {
    this.name = name;
    FakeChannel.all.push(this);
  }
  postMessage(message: unknown) {
    this.sent.push(message);
  }
  addEventListener(_type: 'message', listener: (event: MessageEvent) => void) {
    this.listeners.push(listener);
  }
  close() {
    // Nothing to release.
  }
  receive(data: unknown) {
    for (const listener of this.listeners) listener(new MessageEvent('message', { data }));
  }
}

function sequence(...responses: (() => Response)[]) {
  let index = 0;
  return () => {
    const next = responses[Math.min(index, responses.length - 1)];
    index++;
    if (!next) throw new Error('No response configured.');
    return next();
  };
}

/** In memory, so tests do not share a flag through jsdom's localStorage. */
function memoryMarker(initial = false): SignOutMarker & { value: boolean } {
  const marker = {
    value: initial,
    isSet: () => marker.value,
    set: () => {
      marker.value = true;
    },
    clear: () => {
      marker.value = false;
    },
  };
  return marker;
}

function createSession(fetchFn: typeof fetch, extra: Partial<SessionDependencies> = {}) {
  const channels: FakeChannel[] = [];
  const session = new AuthSession({
    fetchFn,
    locks: null,
    createChannel: (name) => {
      const channel = new FakeChannel(name);
      channels.push(channel);
      return channel;
    },
    sleep: () => Promise.resolve(),
    signOutMarker: memoryMarker(),
    onOnline: null,
    ...extra,
  });
  const channel = channels[0];
  if (!channel) throw new Error('No channel was created.');
  return { session, channel };
}

function bearer(init: RequestInit | undefined) {
  return new Headers(init?.headers).get('Authorization');
}

describe('AuthSession', () => {
  it('restores a session on page load with the refresh cookie and the CSRF header', async () => {
    const api = fakeApi({ '/api/auth/refresh': () => auth(tokenA) });
    const { session } = createSession(api.fetchFn);

    expect(await session.restore()).toBe(true);

    expect(session.getState()).toEqual({ status: 'signed-in', user });
    const [refresh] = api.callsTo('/api/auth/refresh');
    expect(refresh?.init).toMatchObject({ method: 'POST', credentials: 'include' });
    expect(new Headers(refresh?.init?.headers).get('X-ResuMatch-CSRF')).toBe('1');
  });

  it('treats a 401 on page load as signed out and does not retry', async () => {
    const api = fakeApi({ '/api/auth/refresh': () => problem(401, 'unauthorized') });
    const { session } = createSession(api.fetchFn);

    expect(await session.restore()).toBe(false);

    expect(session.getState()).toEqual({ status: 'signed-out', signOutIncomplete: false });
    expect(api.callsTo('/api/auth/refresh')).toHaveLength(1);
  });

  it('restores only once, even when called again (React StrictMode)', async () => {
    const api = fakeApi({ '/api/auth/refresh': () => auth(tokenA) });
    const { session } = createSession(api.fetchFn);

    await Promise.all([session.restore(), session.restore()]);
    await session.restore();

    expect(api.callsTo('/api/auth/refresh')).toHaveLength(1);
  });

  it('waits and retries a superseded refresh once, then succeeds', async () => {
    const sleep = vi.fn(() => Promise.resolve());
    const api = fakeApi({
      '/api/auth/refresh': sequence(() => problem(409, 'refresh_token_superseded'), () => auth(tokenA)),
    });
    const { session } = createSession(api.fetchFn, { sleep });

    expect(await session.refresh()).toBe(true);

    expect(api.callsTo('/api/auth/refresh')).toHaveLength(2);
    expect(sleep).toHaveBeenCalledTimes(1);
    expect(sleep.mock.calls[0]).toEqual([200]);
  });

  it('gives up after a second superseded refresh instead of looping', async () => {
    const api = fakeApi({ '/api/auth/refresh': () => problem(409, 'refresh_token_superseded') });
    const { session } = createSession(api.fetchFn);

    expect(await session.refresh()).toBe(false);

    expect(api.callsTo('/api/auth/refresh')).toHaveLength(2);
    expect(session.getState()).toEqual({ status: 'signed-out', signOutIncomplete: false });
  });

  it.each([
    [403, 'csrf_rejected'],
    [429, 'rate_limited'],
  ])('does not retry a %i %s refresh', async (status, code) => {
    const api = fakeApi({ '/api/auth/refresh': () => problem(status, code) });
    const { session } = createSession(api.fetchFn);

    expect(await session.refresh()).toBe(false);
    expect(api.callsTo('/api/auth/refresh')).toHaveLength(1);
  });

  it('takes the cross-tab Web Lock around each refresh', async () => {
    const lockNames: string[] = [];
    const locks = {
      request<T>(name: string, callback: () => Promise<T>): Promise<T> {
        lockNames.push(name);
        return callback();
      },
    };
    const api = fakeApi({ '/api/auth/refresh': () => auth(tokenA) });
    const { session } = createSession(api.fetchFn, { locks });

    await session.refresh();

    expect(lockNames).toEqual([refreshLockName]);
  });

  it('sends the access token, and on a 401 refreshes once and retries once', async () => {
    const api = fakeApi({
      '/api/auth/refresh': sequence(() => auth(tokenA), () => auth(tokenB)),
      '/api/auth/me': () => new Response('{}', { status: 200 }),
    });
    const { session } = createSession(api.fetchFn);
    await session.restore();
    api.fetchFn.mockClear();
    api.calls.length = 0;

    let meCalls = 0;
    const meRoute = () => {
      meCalls++;
      return meCalls === 1 ? problem(401, 'unauthorized') : new Response('{}', { status: 200 });
    };
    const retrying = fakeApi({ '/api/auth/refresh': () => auth(tokenB), '/api/auth/me': meRoute });
    api.fetchFn.mockImplementation(retrying.fetchFn);

    const response = await session.authorizedFetch('/api/auth/me');

    expect(response.status).toBe(200);
    const me = retrying.callsTo('/api/auth/me');
    expect(me.map((call) => bearer(call.init))).toEqual([`Bearer ${tokenA}`, `Bearer ${tokenB}`]);
    expect(retrying.callsTo('/api/auth/refresh')).toHaveLength(1);
  });

  it('returns the second 401 without another refresh', async () => {
    const api = fakeApi({
      '/api/auth/refresh': () => auth(tokenA),
      '/api/auth/me': () => problem(401, 'unauthorized'),
    });
    const { session } = createSession(api.fetchFn);
    await session.restore();

    const response = await session.authorizedFetch('/api/auth/me');

    expect(response.status).toBe(401);
    expect(api.callsTo('/api/auth/me')).toHaveLength(2);
    expect(api.callsTo('/api/auth/refresh')).toHaveLength(2); // restore + the single retry refresh
  });

  it.each(['https://evil.example/x', '//evil.example/x', '/\\evil.example', 'api/auth/me'])(
    'refuses to send the token to %s',
    async (path) => {
      const api = fakeApi({ '/api/auth/refresh': () => auth(tokenA) });
      const { session } = createSession(api.fetchFn);
      await session.restore();

      await expect(session.authorizedFetch(path)).rejects.toThrow('needs an API path');
      expect(api.calls).toHaveLength(1); // only the restore
    },
  );

  describe('when the refresh brings back a different user', () => {
    const other = { id: 'u2', firstName: 'Başka', lastName: 'Kişi', email: 'baska@example.test' };
    const authAs = (token: string, who: typeof user) =>
      new Response(JSON.stringify({ accessToken: token, expiresAt: '2030-01-01T00:00:00Z', user: who }), {
        status: 200,
        headers: { 'content-type': 'application/json' },
      });

    function setup(secondUser: typeof user) {
      let refreshes = 0;
      const api = fakeApi({
        '/api/auth/refresh': () => (++refreshes === 1 ? authAs(tokenA, user) : authAs(tokenB, secondUser)),
        '/api/resumes/r1': () => problem(401, 'unauthorized'),
      });
      return { api, ...createSession(api.fetchFn) };
    }

    it('does not replay a write as the other user', async () => {
      const { api, session } = setup(other);
      await session.restore();

      const response = await session.authorizedFetch('/api/resumes/r1', { method: 'PUT', body: '{}' });

      expect(response.status).toBe(401);
      const writes = api.callsTo('/api/resumes/r1');
      expect(writes).toHaveLength(1);
      expect(bearer(writes[0]?.init)).toBe(`Bearer ${tokenA}`);
      expect(session.getState()).toEqual({ status: 'signed-in', user: other });
    });

    it('still replays a write for the same user', async () => {
      const { api, session } = setup(user);
      await session.restore();

      await session.authorizedFetch('/api/resumes/r1', { method: 'DELETE' });

      expect(api.callsTo('/api/resumes/r1').map((call) => bearer(call.init))).toEqual([
        `Bearer ${tokenA}`,
        `Bearer ${tokenB}`,
      ]);
    });

    it('replays a read for the new user, since it changes nothing', async () => {
      const { api, session } = setup(other);
      await session.restore();

      await session.authorizedFetch('/api/resumes/r1');

      expect(api.callsTo('/api/resumes/r1')).toHaveLength(2);
    });
  });

  it('shares one refresh between concurrent 401s', async () => {
    let refreshes = 0;
    const tokens = new Map<string, number>();
    const api = fakeApi({
      '/api/auth/refresh': () => {
        refreshes++;
        return auth(refreshes === 1 ? tokenA : tokenB);
      },
      '/api/resumes': () => new Response('[]', { status: 200 }),
    });
    const { session } = createSession(api.fetchFn);
    await session.restore();
    api.fetchFn.mockImplementation((input, init) => {
      const path = typeof input === 'string' ? input : input instanceof URL ? input.pathname : input.url;
      const token = bearer(init) ?? '';
      tokens.set(token, (tokens.get(token) ?? 0) + 1);
      if (path === '/api/auth/refresh') {
        refreshes++;
        return Promise.resolve(auth(tokenB));
      }
      return Promise.resolve(
        token === `Bearer ${tokenA}` ? problem(401, 'unauthorized') : new Response('[]', { status: 200 }),
      );
    });

    const responses = await Promise.all([1, 2, 3].map(() => session.authorizedFetch('/api/resumes')));

    expect(responses.map((response) => response.status)).toEqual([200, 200, 200]);
    expect(refreshes).toBe(2); // restore + one shared refresh
    expect(tokens.get(`Bearer ${tokenB}`)).toBe(3);
  });

  it('signs in, announces it to other tabs and returns the problem on failure', async () => {
    const api = fakeApi({
      '/api/auth/login': sequence(() => problem(401, 'unauthorized'), () => auth(tokenA)),
    });
    const { session, channel } = createSession(api.fetchFn);

    const failed = await session.login('deneme@example.test', 'yanlis');
    expect(failed).toEqual({
      ok: false,
      problem: { status: 401, code: 'unauthorized' },
      retryAfterSeconds: null,
    });
    expect(channel.sent).toEqual([]);

    expect(await session.login('deneme@example.test', 'dogru')).toEqual({ ok: true });
    expect(session.getState()).toEqual({ status: 'signed-in', user });
    expect(channel.name).toBe(authChannelName);
    expect(channel.sent).toEqual([{ type: 'signed-in' }]);
    const [, login] = api.callsTo('/api/auth/login');
    expect(login?.init).toMatchObject({ method: 'POST', credentials: 'include' });
  });

  it('reads Retry-After from a 429 login', async () => {
    const api = fakeApi({ '/api/auth/login': () => problem(429, 'rate_limited', { 'Retry-After': '42' }) });
    const { session } = createSession(api.fetchFn);

    const result = await session.login('deneme@example.test', 'x');

    expect(result).toMatchObject({ ok: false, retryAfterSeconds: 42, problem: { code: 'rate_limited' } });
  });

  it('reports a network failure as a missing problem', async () => {
    const { session } = createSession(vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch')));

    expect(await session.login('deneme@example.test', 'x')).toEqual({
      ok: false,
      problem: null,
      retryAfterSeconds: null,
    });
  });

  it('signs out with the cookie and CSRF header, and tells other tabs', async () => {
    const api = fakeApi({
      '/api/auth/refresh': () => auth(tokenA),
      '/api/auth/logout': () => new Response(null, { status: 204 }),
      '/api/auth/me': () => problem(401, 'unauthorized'),
    });
    const { session, channel } = createSession(api.fetchFn);
    await session.restore();

    await session.logout();

    expect(session.getState()).toEqual({ status: 'signed-out', signOutIncomplete: false });
    expect(channel.sent).toEqual([{ type: 'signed-out' }]);
    const [logout] = api.callsTo('/api/auth/logout');
    expect(logout?.init).toMatchObject({ method: 'POST', credentials: 'include' });
    expect(new Headers(logout?.init?.headers).get('X-ResuMatch-CSRF')).toBe('1');
    // The token is gone: the next call goes without one.
    await session.authorizedFetch('/api/auth/me');
    expect(bearer(api.callsTo('/api/auth/me')[0]?.init)).toBeNull();
  });

  it('signs out locally when the logout request fails, and keeps only the sign-out flag', async () => {
    const marker = memoryMarker();
    const api = fakeApi({ '/api/auth/refresh': () => auth(tokenA) });
    const { session, channel } = createSession(api.fetchFn, { signOutMarker: marker });
    await session.restore();
    api.fetchFn.mockRejectedValueOnce(new TypeError('Failed to fetch'));

    expect(await session.logout()).toEqual({ completed: false });

    expect(session.getState()).toEqual({ status: 'signed-out', signOutIncomplete: true });
    expect(channel.sent).toEqual([{ type: 'signed-out' }]);
    expect(marker.value).toBe(true);
  });

  it('clears the sign-out flag when the logout reaches the API', async () => {
    const marker = memoryMarker();
    const api = fakeApi({
      '/api/auth/refresh': () => auth(tokenA),
      '/api/auth/logout': () => new Response(null, { status: 204 }),
    });
    const { session } = createSession(api.fetchFn, { signOutMarker: marker });
    await session.restore();

    expect(await session.logout()).toEqual({ completed: true });
    expect(marker.value).toBe(false);
  });

  it('sends the unfinished logout on page load instead of refreshing', async () => {
    const marker = memoryMarker(true);
    const api = fakeApi({
      '/api/auth/refresh': () => auth(tokenA),
      '/api/auth/logout': () => new Response(null, { status: 204 }),
    });
    const { session } = createSession(api.fetchFn, { signOutMarker: marker });

    expect(await session.restore()).toBe(false);

    expect(api.callsTo('/api/auth/refresh')).toHaveLength(0);
    expect(api.callsTo('/api/auth/logout')).toHaveLength(1);
    expect(new Headers(api.callsTo('/api/auth/logout')[0]?.init?.headers).get('X-ResuMatch-CSRF')).toBe('1');
    expect(marker.value).toBe(false);
    expect(session.getState()).toEqual({ status: 'signed-out', signOutIncomplete: false });
  });

  it('stays signed out and keeps the flag when the logout fails again on page load', async () => {
    const marker = memoryMarker(true);
    const api = fakeApi({ '/api/auth/refresh': () => auth(tokenA) });
    api.fetchFn.mockRejectedValue(new TypeError('Failed to fetch'));
    const { session } = createSession(api.fetchFn, { signOutMarker: marker });

    expect(await session.restore()).toBe(false);

    expect(api.callsTo('/api/auth/refresh')).toHaveLength(0);
    expect(marker.value).toBe(true);
    expect(session.getState()).toEqual({ status: 'signed-out', signOutIncomplete: true });
  });

  it('never refreshes while a logout is unfinished, so a leftover cookie cannot reopen the session', async () => {
    const marker = memoryMarker(true);
    const api = fakeApi({
      '/api/auth/refresh': () => auth(tokenA),
      '/api/auth/me': () => problem(401, 'unauthorized'),
    });
    const { session, channel } = createSession(api.fetchFn, { signOutMarker: marker });

    expect(await session.refresh()).toBe(false);
    await session.authorizedFetch('/api/auth/me');
    channel.receive({ type: 'signed-in' });
    await Promise.resolve();

    expect(api.callsTo('/api/auth/refresh')).toHaveLength(0);
    expect(session.getState().status).toBe('signed-out');
  });

  it('clears the flag on a new sign-in', async () => {
    const marker = memoryMarker(true);
    const api = fakeApi({ '/api/auth/login': () => auth(tokenA) });
    const { session } = createSession(api.fetchFn, { signOutMarker: marker });

    expect(await session.login('deneme@example.test', 'parola')).toEqual({ ok: true });

    expect(marker.value).toBe(false);
    expect(session.getState().status).toBe('signed-in');
  });

  it('retries the unfinished logout when the browser comes back online', async () => {
    const marker = memoryMarker(true);
    let online: () => void = () => undefined;
    const api = fakeApi({ '/api/auth/logout': () => new Response(null, { status: 204 }) });
    createSession(api.fetchFn, {
      signOutMarker: marker,
      onOnline: (listener) => {
        online = listener;
      },
    });

    online();

    await vi.waitFor(() => {
      expect(marker.value).toBe(false);
    });
    expect(api.callsTo('/api/auth/logout')).toHaveLength(1);
  });

  it('signs out when another tab signs out', async () => {
    const api = fakeApi({ '/api/auth/refresh': () => auth(tokenA) });
    const { session, channel } = createSession(api.fetchFn);
    await session.restore();
    const states: string[] = [];
    session.subscribe((state) => states.push(state.status));

    channel.receive({ type: 'signed-out' });

    expect(session.getState()).toEqual({ status: 'signed-out', signOutIncomplete: false });
    expect(states).toEqual(['signed-out']);
    expect(channel.sent).toEqual([]); // never echoed back
  });

  it('gets its own token when another tab signs in', async () => {
    const api = fakeApi({
      '/api/auth/refresh': sequence(() => problem(401, 'unauthorized'), () => auth(tokenA)),
    });
    const { session, channel } = createSession(api.fetchFn);
    await session.restore();
    expect(session.getState().status).toBe('signed-out');

    channel.receive({ type: 'signed-in' });
    await vi.waitFor(() => {
      expect(session.getState().status).toBe('signed-in');
    });
    expect(api.callsTo('/api/auth/refresh')).toHaveLength(2);
  });

  it('ignores unrelated channel messages', async () => {
    const api = fakeApi({ '/api/auth/refresh': () => auth(tokenA) });
    const { session, channel } = createSession(api.fetchFn);
    await session.restore();

    channel.receive('signed-out');
    channel.receive({ type: 'something-else' });

    expect(session.getState().status).toBe('signed-in');
  });
});

describe('parseRetryAfter', () => {
  it.each([
    ['42', 42],
    ['0', 1],
    [' 7 ', 7],
    [null, 60],
    ['', 60],
    ['soon', 60],
  ])('reads %j as %i seconds', (value, expected) => {
    expect(parseRetryAfter(value)).toBe(expected);
  });

  it('reads an HTTP date', () => {
    const now = Date.parse('2026-11-08T10:00:00Z');
    expect(parseRetryAfter('Sun, 08 Nov 2026 10:00:30 GMT', now)).toBe(30);
  });
});
