import { readProblem, type ApiProblem } from '../api/problem';
import type { components } from '../api/schema';

// The browser side of docs/api/authentication.md:
// - the access token lives only in this object, never in Web Storage,
//   IndexedDB or a script-readable cookie;
// - the refresh token is the API's HttpOnly cookie, sent with
//   credentials: 'include' to refresh and logout only;
// - one refresh at a time per tab (shared promise) and across tabs (Web Locks);
// - a 409 refresh_token_superseded is retried once after a short wait;
// - sign-in and sign-out are announced to other tabs on a BroadcastChannel.

export type CurrentUser = components['schemas']['CurrentUserResponse'];
type AuthResponse = components['schemas']['AuthResponse'];
/** An AuthResponse with the fields a sign-in needs. */
interface SignedIn {
  accessToken: string;
  user: CurrentUser;
}

export type SessionState =
  | { status: 'unknown' }
  | { status: 'signed-out' }
  | { status: 'signed-in'; user: CurrentUser };

export type LoginResult =
  | { ok: true }
  | { ok: false; problem: ApiProblem | null; retryAfterSeconds: number | null };

interface Channel {
  postMessage(message: unknown): void;
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void;
  close(): void;
}

interface Locks {
  request<T>(name: string, callback: () => Promise<T>): Promise<T>;
}

export interface SessionDependencies {
  baseUrl?: string;
  fetchFn?: typeof fetch;
  /** Web Locks; when the browser has none, refreshes are only serialised within the tab. */
  locks?: Locks | null;
  createChannel?: ((name: string) => Channel) | null;
  sleep?: (ms: number) => Promise<void>;
}

export const refreshLockName = 'resumatch-refresh';
export const authChannelName = 'resumatch-auth';
export const csrfHeader = { 'X-ResuMatch-CSRF': '1' } as const;
const supersededRetryDelayMs = 200;
const defaultRetryAfterSeconds = 60;

type ChannelMessage = { type: 'signed-in' } | { type: 'signed-out' };

export class AuthSession {
  private accessToken: string | null = null;
  private state: SessionState = { status: 'unknown' };
  private readonly listeners = new Set<(state: SessionState) => void>();
  private pendingRefresh: Promise<boolean> | null = null;
  private restoreStarted = false;
  private readonly baseUrl: string;
  private readonly fetchFn: typeof fetch;
  private readonly locks: Locks | null;
  private readonly channel: Channel | null;
  private readonly sleep: (ms: number) => Promise<void>;

  constructor(dependencies: SessionDependencies = {}) {
    this.baseUrl = dependencies.baseUrl ?? '';
    this.fetchFn = dependencies.fetchFn ?? ((input, init) => fetch(input, init));
    this.locks = dependencies.locks === undefined ? defaultLocks() : dependencies.locks;
    this.sleep = dependencies.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms)));
    const createChannel = dependencies.createChannel === undefined ? defaultChannel() : dependencies.createChannel;
    this.channel = createChannel ? createChannel(authChannelName) : null;
    this.channel?.addEventListener('message', (event) => {
      void this.onChannelMessage(event.data);
    });
  }

  getState(): SessionState {
    return this.state;
  }

  subscribe(listener: (state: SessionState) => void): () => void {
    this.listeners.add(listener);
    return () => this.listeners.delete(listener);
  }

  /** On page load: a refresh tells whether the browser still has a session. Runs once. */
  restore(): Promise<boolean> {
    if (this.restoreStarted) return this.pendingRefresh ?? Promise.resolve(this.state.status === 'signed-in');
    this.restoreStarted = true;
    return this.refresh();
  }

  async login(email: string, password: string): Promise<LoginResult> {
    let response: Response;
    try {
      response = await this.fetchFn(`${this.baseUrl}/api/auth/login`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      });
    } catch {
      return { ok: false, problem: null, retryAfterSeconds: null };
    }

    if (response.ok) {
      const body = await readAuthResponse(response);
      if (body) {
        this.signIn(body);
        this.broadcast({ type: 'signed-in' });
        return { ok: true };
      }
      return { ok: false, problem: null, retryAfterSeconds: null };
    }

    return {
      ok: false,
      problem: await readProblem(response),
      retryAfterSeconds: response.status === 429 ? parseRetryAfter(response.headers.get('Retry-After')) : null,
    };
  }

  /** Signs out here and in every other tab. The local sign-out happens even if the request fails. */
  async logout(): Promise<void> {
    try {
      await this.fetchFn(`${this.baseUrl}/api/auth/logout`, {
        method: 'POST',
        credentials: 'include',
        headers: csrfHeader,
      });
    } catch {
      // The cookie may outlive a failed request; the API revokes it on the next logout or expiry.
    } finally {
      this.signOut();
      this.broadcast({ type: 'signed-out' });
    }
  }

  /**
   * Calls the API with the access token. On a 401 it refreshes once and
   * retries the request once; a second 401 is returned as it is.
   */
  async authorizedFetch(path: string, init: RequestInit = {}): Promise<Response> {
    // The token goes only to the API: a path, never a URL or a //host reference.
    if (!path.startsWith('/') || path.startsWith('//') || path.startsWith('/\\')) {
      throw new Error(`authorizedFetch needs an API path, got "${path}".`);
    }
    const send = (token: string | null) => {
      const headers = new Headers(init.headers);
      if (token) headers.set('Authorization', `Bearer ${token}`);
      return this.fetchFn(`${this.baseUrl}${path}`, { ...init, headers });
    };

    const tokenUsed = this.accessToken;
    const response = await send(tokenUsed);
    if (response.status !== 401) return response;

    // Another request may already have refreshed while this one was in flight.
    const refreshed = this.accessToken !== tokenUsed && this.accessToken !== null ? true : await this.refresh();
    if (!refreshed) return response;
    return send(this.accessToken);
  }

  /** One refresh at a time in this tab; concurrent callers share it. */
  refresh(): Promise<boolean> {
    this.pendingRefresh ??= this.runRefresh().finally(() => {
      this.pendingRefresh = null;
    });
    return this.pendingRefresh;
  }

  private async runRefresh(): Promise<boolean> {
    const attempt = async (): Promise<SignedIn | null> => {
      for (let tries = 0; tries < 2; tries++) {
        let response: Response;
        try {
          response = await this.fetchFn(`${this.baseUrl}/api/auth/refresh`, {
            method: 'POST',
            credentials: 'include',
            headers: csrfHeader,
          });
        } catch {
          return null;
        }

        if (response.ok) return readAuthResponse(response);
        if (response.status === 409 && tries === 0) {
          // Another tab rotated the cookie a moment ago; the browser now holds the new one.
          const problem = await readProblem(response);
          if (problem?.code === 'refresh_token_superseded') {
            await this.sleep(supersededRetryDelayMs);
            continue;
          }
        }
        // 401: no session. 403 csrf_rejected and 429 are not retried either.
        return null;
      }
      return null;
    };

    const body = this.locks ? await this.locks.request(refreshLockName, attempt) : await attempt();
    if (body) {
      this.signIn(body);
      return true;
    }
    this.signOut();
    return false;
  }

  private async onChannelMessage(data: unknown): Promise<void> {
    if (!isChannelMessage(data)) return;
    if (data.type === 'signed-out') {
      this.signOut();
    } else if (this.state.status !== 'signed-in') {
      // Another tab signed in; the cookie is shared, so get this tab's own token.
      await this.refresh();
    }
  }

  private signIn(body: SignedIn): void {
    this.accessToken = body.accessToken;
    this.setState({ status: 'signed-in', user: body.user });
  }

  private signOut(): void {
    this.accessToken = null;
    if (this.state.status !== 'signed-out') this.setState({ status: 'signed-out' });
  }

  private setState(state: SessionState): void {
    this.state = state;
    for (const listener of this.listeners) listener(state);
  }

  private broadcast(message: ChannelMessage): void {
    this.channel?.postMessage(message);
  }
}

async function readAuthResponse(response: Response): Promise<SignedIn | null> {
  try {
    const body = (await response.json()) as AuthResponse;
    return typeof body.accessToken === 'string' && body.accessToken !== '' && body.user
      ? { accessToken: body.accessToken, user: body.user }
      : null;
  } catch {
    return null;
  }
}

/** Seconds from a Retry-After header (delta-seconds or HTTP date); 60 when missing or unreadable. */
export function parseRetryAfter(value: string | null, now: number = Date.now()): number {
  if (value === null || value.trim() === '') return defaultRetryAfterSeconds;
  const trimmed = value.trim();
  if (/^\d+$/.test(trimmed)) return Math.max(1, Number(trimmed));
  const date = Date.parse(trimmed);
  return Number.isNaN(date) ? defaultRetryAfterSeconds : Math.max(1, Math.ceil((date - now) / 1000));
}

function isChannelMessage(value: unknown): value is ChannelMessage {
  return (
    typeof value === 'object' &&
    value !== null &&
    ((value as { type?: unknown }).type === 'signed-in' || (value as { type?: unknown }).type === 'signed-out')
  );
}

function defaultLocks(): Locks | null {
  return typeof navigator !== 'undefined' && 'locks' in navigator
    ? navigator.locks
    : null;
}

function defaultChannel(): ((name: string) => Channel) | null {
  return typeof BroadcastChannel === 'undefined' ? null : (name) => new BroadcastChannel(name);
}
