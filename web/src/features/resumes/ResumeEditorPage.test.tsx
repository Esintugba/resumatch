import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { messages } from '../../i18n';
import { json, problem, renderApp, serveApi, signedInRoutes, type Call } from '../../test/app';

const id = 'r1';
const editorPath = `/api/resumes/${id}/editor`;
const resumePath = `/api/resumes/${id}`;
const m = messages.editor.main;

function editor(overrides: Record<string, unknown> = {}) {
  return {
    id,
    eTag: '"00000001"',
    title: 'Yazılım geliştirici',
    professionalTitle: 'Kıdemli geliştirici',
    summary: 'Uydurma özet.',
    unlistedSkillsNote: 'Gizli not',
    templateKey: 'classic',
    targetRole: 'Ekip lideri',
    isMasterResume: false,
    isArchived: false,
    createdAt: '2026-11-01T10:00:00Z',
    updatedAt: null,
    contact: null,
    links: [],
    experiences: [],
    educations: [],
    skills: [],
    projects: [],
    certifications: [],
    languages: [],
    ...overrides,
  };
}

function saved(call: Call, eTag: string) {
  return json(200, { ...editor(), ...(call.body as object), eTag });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

async function openMain() {
  fireEvent.click(await screen.findByRole('button', { name: m.title }));
  return screen.findByLabelText(m.resumeTitle);
}

describe('main info', () => {
  it('saves with If-Match, sends the fields the form does not show unchanged, and uses the new ETag next time', async () => {
    let version = 1;
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`PUT ${resumePath}`]: (call) => saved(call, `"0000000${String(++version)}"`),
    });
    renderApp(`/app/resumes/${id}`);

    const title = await openMain();
    fireEvent.change(title, { target: { value: 'Yeni başlık' } });
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    expect(await screen.findByText(messages.forms.saved(m.title))).toBeTruthy();
    fireEvent.change(title, { target: { value: 'İkinci başlık' } });
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));
    await screen.findByText(messages.forms.saved(m.title));

    const puts = api.callsTo('PUT', resumePath);
    expect(puts.map((call) => call.headers.get('If-Match'))).toEqual(['"00000001"', '"00000002"']);
    expect(puts[0]?.body).toEqual({
      title: 'Yeni başlık',
      professionalTitle: 'Kıdemli geliştirici',
      summary: 'Uydurma özet.',
      unlistedSkillsNote: 'Gizli not',
      templateKey: 'classic',
      targetRole: 'Ekip lideri',
    });
  });

  it('keeps the typed text after a 412, never overwrites, and loads the new version only after confirmation', async () => {
    let fresh = false;
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () =>
        json(200, fresh ? editor({ eTag: '"00000009"', title: 'Başka sekmeden' }) : editor()),
      [`PUT ${resumePath}`]: (call) =>
        call.headers.get('If-Match') === '"00000009"'
          ? saved(call, '"0000000A"')
          : problem(412, { code: 'precondition_failed' }),
    });
    renderApp(`/app/resumes/${id}`);

    const title = await openMain();
    fireEvent.change(title, { target: { value: 'Benim yazdığım' } });
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    const alert = await screen.findByRole('alert');
    expect(alert.textContent).toContain(messages.conflict.title);
    expect(screen.getByLabelText<HTMLInputElement>(m.resumeTitle).value).toBe('Benim yazdığım');
    expect(screen.getByRole<HTMLButtonElement>('button', { name: messages.common.save }).disabled).toBe(true);

    // Asking to reload asks first; cancelling keeps the text.
    fireEvent.click(within(alert).getByRole('button', { name: messages.conflict.reload }));
    let dialog = await screen.findByRole('alertdialog', { name: messages.conflict.confirmTitle });
    fireEvent.click(within(dialog).getByRole('button', { name: messages.common.cancel }));
    expect(screen.queryByRole('alertdialog')).toBeNull();
    expect(screen.getByLabelText<HTMLInputElement>(m.resumeTitle).value).toBe('Benim yazdığım');
    expect(api.callsTo('GET', editorPath)).toHaveLength(1);

    // Confirming loads the saved version and its ETag.
    fresh = true;
    fireEvent.click(screen.getByRole('button', { name: messages.conflict.reload }));
    dialog = await screen.findByRole('alertdialog', { name: messages.conflict.confirmTitle });
    fireEvent.click(within(dialog).getByRole('button', { name: messages.conflict.confirm }));

    await waitFor(() => {
      expect(screen.getByLabelText<HTMLInputElement>(m.resumeTitle).value).toBe('Başka sekmeden');
    });
    expect(screen.getByText(messages.conflict.reloaded)).toBeTruthy();
    expect(screen.queryByText(messages.conflict.title)).toBeNull();

    fireEvent.change(screen.getByLabelText(m.resumeTitle), { target: { value: 'Son hali' } });
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));
    await screen.findByText(messages.forms.saved(m.title));
    expect(api.callsTo('PUT', resumePath).map((call) => call.headers.get('If-Match'))).toEqual([
      '"00000001"',
      '"00000009"',
    ]);
  });

  it('puts field errors from a 400 on the field in Turkish', async () => {
    serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`PUT ${resumePath}`]: () =>
        problem(400, { code: 'validation_failed', errors: { Title: ['The supplied value is invalid.'] } }),
    });
    renderApp(`/app/resumes/${id}`);

    const title = await openMain();
    fireEvent.change(title, { target: { value: 'Geçersiz' } });
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    expect((await screen.findByRole('alert')).textContent).toContain(messages.errors.codes.validation_failed);
    await waitFor(() => {
      expect(title.getAttribute('aria-invalid')).toBe('true');
    });
    expect(screen.getByText(messages.forms.fieldInvalid).id).toBe(title.getAttribute('aria-describedby'));
  });

  it('checks the fields before sending', async () => {
    const api = serveApi({ ...signedInRoutes, [`GET ${editorPath}`]: () => json(200, editor()) });
    renderApp(`/app/resumes/${id}`);

    const title = await openMain();
    fireEvent.change(title, { target: { value: '   ' } });
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    expect(await screen.findByText(messages.forms.required)).toBeTruthy();
    expect(api.callsTo('PUT', resumePath)).toHaveLength(0);
  });

  it('locks the save button while saving', async () => {
    let finish: (response: Response) => void = () => undefined;
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`PUT ${resumePath}`]: () =>
        new Promise<Response>((resolve) => {
          finish = resolve;
        }),
    });
    renderApp(`/app/resumes/${id}`);

    const title = await openMain();
    fireEvent.change(title, { target: { value: 'Yeni' } });
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    const saving = await screen.findByRole<HTMLButtonElement>('button', { name: messages.common.saving });
    expect(saving.disabled).toBe(true);
    fireEvent.click(saving);
    expect(api.callsTo('PUT', resumePath)).toHaveLength(1);
    await act(async () => {
      finish(json(200, editor({ eTag: '"00000002"', title: 'Yeni' })));
      await Promise.resolve();
    });
  });
});

describe('unsaved changes', () => {
  function routes() {
    return serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      'GET /api/resumes?page=1&pageSize=20': () =>
        json(200, { items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 }),
    });
  }

  it('asks before opening another section, and keeps the text when the user stays', async () => {
    routes();
    renderApp(`/app/resumes/${id}`);
    const title = await openMain();
    fireEvent.change(title, { target: { value: 'Yarım kalan' } });

    fireEvent.click(screen.getByRole('button', { name: messages.editor.contact.title }));
    let dialog = await screen.findByRole('alertdialog', { name: messages.unsaved.title });
    fireEvent.click(within(dialog).getByRole('button', { name: messages.unsaved.stay }));

    expect(screen.getByLabelText<HTMLInputElement>(m.resumeTitle).value).toBe('Yarım kalan');
    expect(screen.queryByLabelText(messages.editor.contact.firstName)).toBeNull();

    fireEvent.click(screen.getByRole('button', { name: messages.editor.contact.title }));
    dialog = await screen.findByRole('alertdialog', { name: messages.unsaved.title });
    fireEvent.click(within(dialog).getByRole('button', { name: messages.unsaved.discard }));

    expect(await screen.findByLabelText(messages.editor.contact.firstName)).toBeTruthy();
    expect(screen.queryByLabelText(m.resumeTitle)).toBeNull();
  });

  it('opens another section at once when nothing changed, with only one form open', async () => {
    routes();
    renderApp(`/app/resumes/${id}`);
    await openMain();

    fireEvent.click(screen.getByRole('button', { name: messages.editor.contact.title }));

    expect(await screen.findByLabelText(messages.editor.contact.firstName)).toBeTruthy();
    expect(screen.queryByLabelText(m.resumeTitle)).toBeNull();
    expect(screen.queryByRole('alertdialog')).toBeNull();
    // A new contact starts from the signed-in user's own name.
    expect(screen.getByLabelText<HTMLInputElement>(messages.editor.contact.firstName).value).toBe('Deneme');
  });

  it('blocks leaving the page with unsaved changes until the user agrees', async () => {
    routes();
    renderApp(`/app/resumes/${id}`);
    const title = await openMain();
    fireEvent.change(title, { target: { value: 'Yarım kalan' } });

    fireEvent.click(screen.getByRole('link', { name: `← ${messages.editor.back}` }));
    let dialog = await screen.findByRole('alertdialog', { name: messages.unsaved.title });
    fireEvent.click(within(dialog).getByRole('button', { name: messages.unsaved.stay }));
    expect(window.location.pathname).toBe(`/app/resumes/${id}`);

    fireEvent.click(screen.getByRole('link', { name: `← ${messages.editor.back}` }));
    dialog = await screen.findByRole('alertdialog', { name: messages.unsaved.title });
    fireEvent.click(within(dialog).getByRole('button', { name: messages.unsaved.discard }));

    expect(await screen.findByRole('heading', { level: 1, name: messages.resumes.title })).toBeTruthy();
    expect(window.location.pathname).toBe('/app/resumes');
  });

  it('asks the browser to confirm closing the tab only while there are unsaved changes', async () => {
    routes();
    renderApp(`/app/resumes/${id}`);
    const title = await openMain();

    const before = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(before);
    expect(before.defaultPrevented).toBe(false);

    fireEvent.change(title, { target: { value: 'Yarım kalan' } });
    await waitFor(() => {
      const dirty = new Event('beforeunload', { cancelable: true });
      window.dispatchEvent(dirty);
      expect(dirty.defaultPrevented).toBe(true);
    });
  });
});
