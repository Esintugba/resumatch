import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { messages } from '../../../i18n';
import { json, pathOf, problem, renderApp, serveApi, signedInRoutes, type Call } from '../../../test/app';

const id = 'r1';
const editorPath = `/api/resumes/${id}/editor`;
const t = messages.skills;
const catalog = [
  { id: 's-react', name: 'React', nameTr: null, category: 'Frontend' },
  { id: 's-redux', name: 'Redux', nameTr: null, category: 'Frontend' },
];

function editor(extra: Record<string, unknown> = {}) {
  return {
    id,
    eTag: '"00000001"',
    title: 'Uydurma özgeçmiş',
    professionalTitle: 'Geliştirici',
    summary: 'Uydurma özet',
    unlistedSkillsNote: null,
    templateKey: 'classic',
    targetRole: 'Ekip lideri',
    contact: null,
    links: [],
    experiences: [],
    educations: [],
    skills: [],
    projects: [],
    certifications: [],
    languages: [],
    isArchived: false,
    createdAt: '2026-11-01T10:00:00Z',
    ...extra,
  };
}

const searchPath = (term: string) => `/api/skills?search=${encodeURIComponent(term)}&pageSize=10`;

afterEach(() => {
  vi.unstubAllGlobals();
});

async function openSkills() {
  fireEvent.click(await screen.findByRole('button', { name: t.title }));
  return screen.findByLabelText(t.search);
}

describe('skills', () => {
  it('waits while typing and searches once for the last term', async () => {
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`GET ${searchPath('rea')}`]: () => json(200, [catalog[0]]),
    });
    renderApp(`/app/resumes/${id}`);
    const search = await openSkills();

    fireEvent.change(search, { target: { value: 'r' } });
    fireEvent.change(search, { target: { value: 're' } });
    fireEvent.change(search, { target: { value: 'rea' } });

    expect(await screen.findByRole('button', { name: t.add('React') })).toBeTruthy();
    expect(api.calls.filter((call) => call.path.startsWith('/api/skills'))).toHaveLength(1);
    expect(screen.getByText(t.results(1))).toBeTruthy();
  });

  it('cancels a slower earlier search and never shows its answer', async () => {
    let answerFirst: (response: Response) => void = () => undefined;
    const signals: AbortSignal[] = [];
    serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`GET ${searchPath('re')}`]: () =>
        new Promise<Response>((resolve) => {
          answerFirst = resolve;
        }),
      [`GET ${searchPath('red')}`]: () => json(200, [catalog[1]]),
    });
    const realFetch = globalThis.fetch;
    vi.stubGlobal('fetch', (input: RequestInfo | URL, init?: RequestInit) => {
      if (init?.signal && pathOf(input).startsWith('/api/skills')) signals.push(init.signal);
      return realFetch(input, init);
    });
    renderApp(`/app/resumes/${id}`);
    const search = await openSkills();

    fireEvent.change(search, { target: { value: 're' } });
    await waitFor(() => {
      expect(signals).toHaveLength(1);
    });
    fireEvent.change(search, { target: { value: 'red' } });
    expect(await screen.findByRole('button', { name: t.add('Redux') })).toBeTruthy();
    expect(signals[0]?.aborted).toBe(true);

    await act(async () => {
      answerFirst(json(200, [catalog[0]]));
      await Promise.resolve();
    });
    expect(screen.queryByRole('button', { name: t.add('React') })).toBeNull();
  });

  it('explains one-character search and sends it as typed', async () => {
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`GET ${searchPath('r')}`]: () => json(200, [{ id: 's-r', name: 'R', nameTr: null, category: 'ProgrammingLanguage' }]),
    });
    renderApp(`/app/resumes/${id}`);
    const search = await openSkills();
    expect(screen.getByText(t.searchHint, { exact: false })).toBeTruthy();

    fireEvent.change(search, { target: { value: 'r' } });

    expect(await screen.findByRole('button', { name: t.add('R') })).toBeTruthy();
    expect(api.calls.some((call) => call.path === searchPath('r'))).toBe(true);
  });

  it('adds a skill, then shows it as added; a duplicate shows the Turkish message', async () => {
    let added = false;
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`GET ${searchPath('react')}`]: () => json(200, [catalog[0]]),
      [`POST /api/resumes/${id}/skills`]: () => {
        if (added) return problem(409, { code: 'duplicate_skill' });
        added = true;
        return json(201, { skillId: 's-react', name: 'React', category: 'Frontend', level: null, sortOrder: 0, isHighlighted: false });
      },
    });
    renderApp(`/app/resumes/${id}`);
    fireEvent.change(await openSkills(), { target: { value: 'react' } });

    fireEvent.click(await screen.findByRole('button', { name: t.add('React') }));

    expect(await screen.findByText(t.addedDone('React'))).toBeTruthy();
    expect(api.callsTo('POST', `/api/resumes/${id}/skills`)[0]?.body).toEqual({
      skillId: 's-react',
      level: null,
      sortOrder: 0,
      isHighlighted: false,
    });
    const list = screen.getByRole('list', { name: t.listLabel });
    expect(within(list).getByText('React')).toBeTruthy();
    expect(screen.getByRole<HTMLButtonElement>('button', { name: `${t.added}: React` }).disabled).toBe(true);
  });

  it('shows a duplicate skill error from the API in Turkish', async () => {
    serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`GET ${searchPath('react')}`]: () => json(200, [catalog[0]]),
      [`POST /api/resumes/${id}/skills`]: () => problem(409, { code: 'duplicate_skill' }),
    });
    renderApp(`/app/resumes/${id}`);
    fireEvent.change(await openSkills(), { target: { value: 'react' } });

    fireEvent.click(await screen.findByRole('button', { name: t.add('React') }));

    expect((await screen.findByRole('alert')).textContent).toContain(messages.errors.codes.duplicate_skill);
  });

  it('changes the level and deletes only after confirmation', async () => {
    const skill = { skillId: 's-react', name: 'React', category: 'Frontend', level: null, sortOrder: 0, isHighlighted: false };
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor({ skills: [skill] })),
      [`PUT /api/resumes/${id}/skills/s-react`]: (call: Call) => json(200, { ...skill, ...(call.body as object) }),
      [`DELETE /api/resumes/${id}/skills/s-react`]: () => new Response(null, { status: 204 }),
    });
    renderApp(`/app/resumes/${id}`);
    await openSkills();

    fireEvent.change(screen.getByLabelText(t.levelFor('React')), { target: { value: 'Advanced' } });
    expect(await screen.findByText(t.levelSaved('React'))).toBeTruthy();
    expect(api.callsTo('PUT', `/api/resumes/${id}/skills/s-react`)[0]?.body).toEqual({
      level: 'Advanced',
      sortOrder: 0,
      isHighlighted: false,
    });

    fireEvent.click(screen.getByRole('button', { name: messages.items.delete('React') }));
    const dialog = await screen.findByRole('alertdialog', { name: messages.items.deleteTitle('React') });
    fireEvent.click(within(dialog).getByRole('button', { name: messages.items.deleteConfirm }));
    expect(await screen.findByText(messages.items.deleted('React'))).toBeTruthy();
    expect(screen.getByText(t.empty)).toBeTruthy();
  });

  it('offers the unlisted note when the catalog has nothing, and saves it with the resume ETag and the other fields unchanged', async () => {
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor({ unlistedSkillsNote: 'COBOL' })),
      [`GET ${searchPath('Uydurma Araç')}`]: () => json(200, []),
      [`PUT /api/resumes/${id}`]: (call) => json(200, { ...editor(), ...(call.body as object), eTag: '"00000002"' }),
    });
    renderApp(`/app/resumes/${id}`);
    fireEvent.change(await openSkills(), { target: { value: 'Uydurma Araç' } });

    expect(await screen.findByText(t.notFound('Uydurma Araç'))).toBeTruthy();
    fireEvent.click(screen.getByRole('button', { name: t.addToNote('Uydurma Araç') }));
    const note = screen.getByLabelText<HTMLTextAreaElement>(t.note);
    expect(note.value).toBe('COBOL, Uydurma Araç');
    await waitFor(() => {
      expect(document.activeElement).toBe(note);
    });

    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));
    expect(await screen.findByText(t.noteSaved)).toBeTruthy();
    const put = api.callsTo('PUT', `/api/resumes/${id}`)[0];
    expect(put?.headers.get('If-Match')).toBe('"00000001"');
    expect(put?.body).toEqual({
      title: 'Uydurma özgeçmiş',
      professionalTitle: 'Geliştirici',
      summary: 'Uydurma özet',
      unlistedSkillsNote: 'COBOL, Uydurma Araç',
      templateKey: 'classic',
      targetRole: 'Ekip lideri',
    });
  });
});
