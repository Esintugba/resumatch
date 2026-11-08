import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { messages } from '../../i18n';
import { json, problem, renderApp, serveApi, signedInRoutes } from '../../test/app';

const listPath = 'GET /api/resumes?page=1&pageSize=20';
const t = messages.resumes;

function page(items: object[], totalPages = items.length ? 1 : 0) {
  return json(200, { items, page: 1, pageSize: 20, totalCount: items.length, totalPages });
}

const item = (id: string, title: string, extra: object = {}) => ({
  id,
  eTag: '"1"',
  title,
  professionalTitle: 'Uydurma unvan',
  isMasterResume: false,
  isArchived: false,
  createdAt: '2026-11-01T10:00:00Z',
  updatedAt: '2026-11-02T12:30:00Z',
  ...extra,
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('resume list', () => {
  it('shows the empty state', async () => {
    serveApi({ ...signedInRoutes, [listPath]: () => page([]) });
    renderApp('/app/resumes');

    expect(await screen.findByText(t.empty)).toBeTruthy();
    expect(screen.queryByRole('list', { name: t.listLabel })).toBeNull();
  });

  it('lists resumes with their badges and archives only active ones', async () => {
    serveApi({
      ...signedInRoutes,
      [listPath]: () =>
        page([item('a', 'Birinci özgeçmiş', { isMasterResume: true }), item('b', 'Eski özgeçmiş', { isArchived: true })]),
    });
    renderApp('/app/resumes');

    const list = await screen.findByRole('list', { name: t.listLabel });
    const [first, second] = within(list).getAllByRole('listitem');
    if (!first || !second) throw new Error('Expected two items.');
    expect(within(first).getByRole('link', { name: 'Birinci özgeçmiş' }).getAttribute('href')).toBe('/app/resumes/a');
    expect(within(first).getByText(t.master)).toBeTruthy();
    expect(within(first).getByRole('button', { name: `${t.archive}: Birinci özgeçmiş` })).toBeTruthy();
    expect(within(second).getByText(t.archived)).toBeTruthy();
    expect(within(second).queryByRole('button', { name: new RegExp(t.archive) })).toBeNull();
  });

  it('creates a resume and opens it in the editor', async () => {
    const api = serveApi({
      ...signedInRoutes,
      [listPath]: () => page([]),
      'POST /api/resumes': () => json(201, item('new', 'Yeni özgeçmiş')),
      'GET /api/resumes/new/editor': () =>
        json(200, { ...item('new', 'Yeni özgeçmiş'), contact: null, links: [], experiences: [], educations: [], skills: [] }),
    });
    renderApp('/app/resumes');

    fireEvent.change(await screen.findByLabelText(t.createLabel), { target: { value: '  Yeni özgeçmiş  ' } });
    fireEvent.click(screen.getByRole('button', { name: t.create }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Yeni özgeçmiş' })).toBeTruthy();
    expect(window.location.pathname).toBe('/app/resumes/new');
    expect(api.callsTo('POST', '/api/resumes')[0]?.body).toEqual({ title: 'Yeni özgeçmiş' });
  });

  it('requires a name before creating', async () => {
    const api = serveApi({ ...signedInRoutes, [listPath]: () => page([]) });
    renderApp('/app/resumes');

    fireEvent.click(await screen.findByRole('button', { name: t.create }));

    expect(await screen.findByText(messages.forms.required)).toBeTruthy();
    expect(api.callsTo('POST', '/api/resumes')).toHaveLength(0);
  });

  it('archives after confirmation and refreshes the list', async () => {
    let archived = false;
    const api = serveApi({
      ...signedInRoutes,
      [listPath]: () => page([item('a', 'Birinci özgeçmiş', { isArchived: archived })]),
      'PATCH /api/resumes/a/archive': () => {
        archived = true;
        return new Response(null, { status: 204 });
      },
    });
    renderApp('/app/resumes');

    fireEvent.click(await screen.findByRole('button', { name: `${t.archive}: Birinci özgeçmiş` }));
    const dialog = await screen.findByRole('alertdialog', { name: t.archiveTitle('Birinci özgeçmiş') });
    // The safe choice has focus.
    expect(document.activeElement).toBe(within(dialog).getByRole('button', { name: messages.common.cancel }));
    fireEvent.click(within(dialog).getByRole('button', { name: t.archive }));

    expect(await screen.findByText(t.archivedDone('Birinci özgeçmiş'))).toBeTruthy();
    await waitFor(() => {
      expect(screen.getByText(t.archived)).toBeTruthy();
    });
    expect(api.callsTo('PATCH', '/api/resumes/a/archive')).toHaveLength(1);
  });

  it('does not archive when cancelled', async () => {
    const api = serveApi({ ...signedInRoutes, [listPath]: () => page([item('a', 'Birinci özgeçmiş')]) });
    renderApp('/app/resumes');

    fireEvent.click(await screen.findByRole('button', { name: `${t.archive}: Birinci özgeçmiş` }));
    const dialog = await screen.findByRole('alertdialog');
    fireEvent.click(within(dialog).getByRole('button', { name: messages.common.cancel }));

    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(api.callsTo('PATCH', '/api/resumes/a/archive')).toHaveLength(0);
  });

  it('does not retry a 4xx and shows the error with a retry button', async () => {
    const api = serveApi({ ...signedInRoutes, [listPath]: () => problem(404, { code: 'not_found' }) });
    renderApp('/app/resumes');

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain(messages.common.loadFailed);
    expect(within(alert).getByRole('button', { name: messages.common.retry })).toBeTruthy();
    expect(api.callsTo('GET', '/api/resumes?page=1&pageSize=20')).toHaveLength(1);
  });
});

describe('another user in the same tab', () => {
  it('never shows the previous user\'s cached resumes after sign-out and a new sign-in', async () => {
    let user: 'a' | 'b' = 'a';
    let releaseB: () => void = () => undefined;
    const bReady = new Promise<void>((resolve) => {
      releaseB = resolve;
    });
    const person = (id: 'a' | 'b') => ({
      id,
      firstName: id === 'a' ? 'Ayşe' : 'Burak',
      lastName: 'Deneme',
      email: `${id}@example.test`,
    });
    serveApi({
      ...signedInRoutes,
      'POST /api/auth/refresh': () =>
        json(200, { accessToken: 'token-a', expiresAt: '2030-01-01T00:00:00Z', user: person('a') }),
      'POST /api/auth/logout': () => new Response(null, { status: 204 }),
      'POST /api/auth/login': () => {
        user = 'b';
        return json(200, { accessToken: 'token-b', expiresAt: '2030-01-01T00:00:00Z', user: person('b') });
      },
      [listPath]: async () => {
        if (user === 'b') {
          await bReady;
          return page([item('b1', 'Burak özgeçmişi')]);
        }
        return page([item('a1', 'Ayşe özgeçmişi')]);
      },
    });
    renderApp('/app/resumes');
    expect(await screen.findByText('Ayşe özgeçmişi')).toBeTruthy();

    fireEvent.click(screen.getByRole('button', { name: messages.nav.signOut }));
    fireEvent.change(await screen.findByLabelText(messages.login.email), { target: { value: 'b@example.test' } });
    fireEvent.change(screen.getByLabelText(messages.login.password), { target: { value: 'parola' } });
    fireEvent.click(screen.getByRole('button', { name: messages.login.submit }));

    // B's list is still loading: A's cached list must not be shown meanwhile.
    expect(await screen.findByRole('heading', { level: 1, name: t.title })).toBeTruthy();
    expect(screen.queryByText('Ayşe özgeçmişi')).toBeNull();
    releaseB();
    expect(await screen.findByText('Burak özgeçmişi')).toBeTruthy();
    expect(screen.queryByText('Ayşe özgeçmişi')).toBeNull();
  });
});
