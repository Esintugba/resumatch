import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { messages } from '../../../i18n';
import { json, problem, renderApp, serveApi, signedInRoutes, type Call } from '../../../test/app';

const id = 'r1';
const editorPath = `/api/resumes/${id}/editor`;
const x = messages.experience;
const e = messages.education;
const it8 = messages.items;

function experience(n: number, extra: Record<string, unknown> = {}) {
  return {
    id: `x${String(n)}`,
    eTag: `"0000000${String(n)}"`,
    companyName: `Uydurma Şirket ${String(n)}`,
    position: `Geliştirici ${String(n)}`,
    location: null,
    employmentType: 'FullTime',
    startDate: '2020-01-01',
    endDate: null,
    isCurrent: true,
    description: null,
    sortOrder: n - 1,
    bullets: [],
    ...extra,
  };
}

function education(n: number, extra: Record<string, unknown> = {}) {
  return {
    id: `e${String(n)}`,
    institutionName: `Uydurma Üniversitesi ${String(n)}`,
    degree: 'Lisans',
    fieldOfStudy: 'Bilgisayar',
    startDate: '2012-09-01',
    endDate: '2016-06-01',
    location: null,
    grade: null,
    gradeScale: null,
    description: null,
    sortOrder: n - 1,
    ...extra,
  };
}

function editor(extra: Record<string, unknown> = {}) {
  return {
    id,
    eTag: '"00000001"',
    title: 'Uydurma özgeçmiş',
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

afterEach(() => {
  vi.unstubAllGlobals();
});

function nth(elements: HTMLElement[], index: number): HTMLElement {
  const element = elements[index];
  if (!element) throw new Error(`No element at ${String(index)}.`);
  return element;
}

async function openSection(title: string) {
  fireEvent.click(await screen.findByRole('button', { name: title }));
}

async function fillExperience(values: { position: string; company: string; startMonth: string; startYear: string }) {
  fireEvent.change(await screen.findByLabelText(x.position), { target: { value: values.position } });
  fireEvent.change(screen.getByLabelText(x.companyName), { target: { value: values.company } });
  fireEvent.change(screen.getByLabelText(x.employmentType), { target: { value: 'FullTime' } });
  const [startMonth] = screen.getAllByLabelText(messages.dates.month);
  const [startYear] = screen.getAllByLabelText(messages.dates.year);
  if (!startMonth || !startYear) throw new Error('No date fields.');
  fireEvent.change(startMonth, { target: { value: values.startMonth } });
  fireEvent.change(startYear, { target: { value: values.startYear } });
}

describe('experience', () => {
  it('adds an experience with month dates, ongoing without an end date, at the end of the list', async () => {
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor({ experiences: [experience(1)] })),
      [`POST /api/resumes/${id}/experiences`]: (call) =>
        json(201, { ...experience(2), ...(call.body as object), id: 'x2', eTag: '"00000002"' }),
    });
    renderApp(`/app/resumes/${id}`);
    await openSection(x.title);

    fireEvent.click(await screen.findByRole('button', { name: it8.add(x.one) }));
    await fillExperience({ position: 'Kıdemli geliştirici', company: 'Uydurma A.Ş.', startMonth: '03', startYear: '2021' });
    fireEvent.click(screen.getByLabelText(x.isCurrent));
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    expect(await screen.findByText(it8.saved('Kıdemli geliştirici – Uydurma A.Ş.'))).toBeTruthy();
    expect(api.callsTo('POST', `/api/resumes/${id}/experiences`)[0]?.body).toMatchObject({
      position: 'Kıdemli geliştirici',
      companyName: 'Uydurma A.Ş.',
      employmentType: 'FullTime',
      startDate: '2021-03-01',
      endDate: null,
      isCurrent: true,
      sortOrder: 1,
      bullets: [],
    });
    expect(screen.getByText('Kıdemli geliştirici – Uydurma A.Ş.')).toBeTruthy();
  });

  it('refuses an end date before the start in Turkish, before sending', async () => {
    const api = serveApi({ ...signedInRoutes, [`GET ${editorPath}`]: () => json(200, editor()) });
    renderApp(`/app/resumes/${id}`);
    await openSection(x.title);
    fireEvent.click(await screen.findByRole('button', { name: it8.add(x.one) }));
    await fillExperience({ position: 'Geliştirici', company: 'Uydurma', startMonth: '06', startYear: '2022' });
    const months = screen.getAllByLabelText(messages.dates.month);
    const years = screen.getAllByLabelText(messages.dates.year);
    fireEvent.change(nth(months, 1), { target: { value: '01' } });
    fireEvent.change(nth(years, 1), { target: { value: '2022' } });

    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    expect(await screen.findByText(messages.dates.endBeforeStart)).toBeTruthy();
    expect(api.callsTo('POST', `/api/resumes/${id}/experiences`)).toHaveLength(0);
  });

  it('shows the API date-order error on the end date in Turkish', async () => {
    serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`POST /api/resumes/${id}/experiences`]: () =>
        problem(400, {
          code: 'validation_failed',
          errors: { StartDate: ['The EndDate field must be on or after StartDate.'], EndDate: ['…'] },
        }),
    });
    renderApp(`/app/resumes/${id}`);
    await openSection(x.title);
    fireEvent.click(await screen.findByRole('button', { name: it8.add(x.one) }));
    await fillExperience({ position: 'Geliştirici', company: 'Uydurma', startMonth: '06', startYear: '2022' });

    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    expect(await screen.findByText(messages.dates.endBeforeStart)).toBeTruthy();
    expect((await screen.findByRole('alert')).textContent).toContain(messages.errors.codes.validation_failed);
  });

  it('shows the section limit in Turkish', async () => {
    serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor()),
      [`POST /api/resumes/${id}/experiences`]: () => problem(409, { code: 'section_limit_reached' }),
    });
    renderApp(`/app/resumes/${id}`);
    await openSection(x.title);
    fireEvent.click(await screen.findByRole('button', { name: it8.add(x.one) }));
    await fillExperience({ position: 'Geliştirici', company: 'Uydurma', startMonth: '06', startYear: '2022' });

    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    expect((await screen.findByRole('alert')).textContent).toContain(messages.errors.codes.section_limit_reached);
  });

  it('updates with If-Match and sends the existing bullets back unchanged', async () => {
    const bullets = [{ id: 'b1', content: 'Uydurma madde', sortOrder: 0, isAiGenerated: false }];
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor({ experiences: [experience(1, { bullets })] })),
      [`PUT /api/resumes/${id}/experiences/x1`]: (call) =>
        json(200, { ...experience(1, { bullets }), ...(call.body as object), eTag: '"00000009"' }),
    });
    renderApp(`/app/resumes/${id}`);
    await openSection(x.title);

    fireEvent.click(await screen.findByRole('button', { name: it8.edit('Geliştirici 1 – Uydurma Şirket 1') }));
    expect(screen.getByText(x.bulletsKept(1))).toBeTruthy();
    fireEvent.change(screen.getByLabelText(x.position), { target: { value: 'Ekip lideri' } });
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    await screen.findByText(it8.saved('Ekip lideri – Uydurma Şirket 1'));
    const put = api.callsTo('PUT', `/api/resumes/${id}/experiences/x1`)[0];
    expect(put?.headers.get('If-Match')).toBe('"00000001"');
    expect(put?.body).toMatchObject({ position: 'Ekip lideri', bullets: [{ id: 'b1', content: 'Uydurma madde', sortOrder: 0 }] });
  });

  it('keeps the text after a 412 and offers the saved version', async () => {
    serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor({ experiences: [experience(1)] })),
      [`PUT /api/resumes/${id}/experiences/x1`]: () => problem(412, { code: 'precondition_failed' }),
    });
    renderApp(`/app/resumes/${id}`);
    await openSection(x.title);
    fireEvent.click(await screen.findByRole('button', { name: it8.edit('Geliştirici 1 – Uydurma Şirket 1') }));
    fireEvent.change(screen.getByLabelText(x.position), { target: { value: 'Benim yazdığım' } });

    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    expect((await screen.findByRole('alert')).textContent).toContain(messages.conflict.title);
    expect(screen.getByLabelText<HTMLInputElement>(x.position).value).toBe('Benim yazdığım');
  });

  it('deletes only after confirmation', async () => {
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor({ experiences: [experience(1)] })),
      [`DELETE /api/resumes/${id}/experiences/x1`]: () => new Response(null, { status: 204 }),
    });
    renderApp(`/app/resumes/${id}`);
    await openSection(x.title);
    const name = 'Geliştirici 1 – Uydurma Şirket 1';

    fireEvent.click(await screen.findByRole('button', { name: it8.delete(name) }));
    let dialog = await screen.findByRole('alertdialog', { name: it8.deleteTitle(name) });
    fireEvent.click(within(dialog).getByRole('button', { name: messages.common.cancel }));
    expect(api.callsTo('DELETE', `/api/resumes/${id}/experiences/x1`)).toHaveLength(0);

    fireEvent.click(screen.getByRole('button', { name: it8.delete(name) }));
    dialog = await screen.findByRole('alertdialog', { name: it8.deleteTitle(name) });
    fireEvent.click(within(dialog).getByRole('button', { name: it8.deleteConfirm }));

    expect(await screen.findByText(it8.deleted(name))).toBeTruthy();
    expect(screen.getByText(x.empty)).toBeTruthy();
  });

  it('moves an item down with its ETag, announces it and keeps focus on the moved item', async () => {
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor({ experiences: [experience(1), experience(2), experience(3)] })),
      ...Object.fromEntries(
        [1, 2, 3].map((n) => [
          `PUT /api/resumes/${id}/experiences/x${String(n)}`,
          (call: Call) => json(200, { ...experience(n), ...(call.body as object), eTag: `"0000001${String(n)}"` }),
        ]),
      ),
    });
    renderApp(`/app/resumes/${id}`);
    await openSection(x.title);

    fireEvent.click(await screen.findByRole('button', { name: it8.moveDown('Geliştirici 1 – Uydurma Şirket 1') }));

    expect(await screen.findByText(it8.moved('Geliştirici 1 – Uydurma Şirket 1', 2, 3))).toBeTruthy();
    const puts = api.calls.filter((call) => call.method === 'PUT');
    expect(puts.map((call) => [call.path.split('/').at(-1), (call.body as { sortOrder: number }).sortOrder, call.headers.get('If-Match')])).toEqual([
      ['x2', 0, '"00000002"'],
      ['x1', 1, '"00000001"'],
    ]);
    const order = screen
      .getAllByRole('listitem')
      .map((item) => /Geliştirici (\d)/.exec(item.textContent)?.[1])
      .filter(Boolean);
    expect(order).toEqual(['2', '1', '3']);
    await waitFor(() => {
      expect(document.activeElement).toBe(screen.getByRole('button', { name: it8.moveDown('Geliştirici 1 – Uydurma Şirket 1') }));
    });
  });

  it('asks before leaving a changed form for another item', async () => {
    serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor({ experiences: [experience(1), experience(2)] })),
    });
    renderApp(`/app/resumes/${id}`);
    await openSection(x.title);
    fireEvent.click(await screen.findByRole('button', { name: it8.edit('Geliştirici 1 – Uydurma Şirket 1') }));
    fireEvent.change(screen.getByLabelText(x.position), { target: { value: 'Yarım kalan' } });

    fireEvent.click(screen.getByRole('button', { name: it8.edit('Geliştirici 2 – Uydurma Şirket 2') }));
    const dialog = await screen.findByRole('alertdialog', { name: it8.unsavedTitle });
    fireEvent.click(within(dialog).getByRole('button', { name: messages.common.cancel }));

    expect(screen.getByLabelText<HTMLInputElement>(x.position).value).toBe('Yarım kalan');
  });
});

describe('education', () => {
  it('adds an ongoing education without an end date, reads a comma grade, and updates without If-Match', async () => {
    const api = serveApi({
      ...signedInRoutes,
      [`GET ${editorPath}`]: () => json(200, editor({ educations: [education(1)] })),
      [`POST /api/resumes/${id}/educations`]: (call) => json(201, { ...education(2), ...(call.body as object), id: 'e2' }),
      [`PUT /api/resumes/${id}/educations/e1`]: (call) => json(200, { ...education(1), ...(call.body as object) }),
    });
    renderApp(`/app/resumes/${id}`);
    await openSection(e.title);

    fireEvent.click(await screen.findByRole('button', { name: it8.add(e.one) }));
    fireEvent.change(await screen.findByLabelText(e.institutionName), { target: { value: 'Uydurma Enstitüsü' } });
    fireEvent.change(screen.getByLabelText(e.grade), { target: { value: '3,45' } });
    fireEvent.change(nth(screen.getAllByLabelText(messages.dates.month), 0), { target: { value: '09' } });
    fireEvent.change(nth(screen.getAllByLabelText(messages.dates.year), 0), { target: { value: '2024' } });
    fireEvent.click(screen.getByLabelText(e.isCurrent));
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));
    await screen.findByText(it8.saved('Uydurma Enstitüsü'));

    expect(api.callsTo('POST', `/api/resumes/${id}/educations`)[0]?.body).toMatchObject({
      institutionName: 'Uydurma Enstitüsü',
      startDate: '2024-09-01',
      endDate: null,
      grade: 3.45,
      sortOrder: 1,
    });

    fireEvent.click(screen.getByRole('button', { name: it8.edit('Uydurma Üniversitesi 1 – Lisans') }));
    fireEvent.change(screen.getByLabelText(e.fieldOfStudy), { target: { value: 'Matematik' } });
    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));
    await screen.findByText(it8.saved('Uydurma Üniversitesi 1 – Lisans'));
    expect(api.callsTo('PUT', `/api/resumes/${id}/educations/e1`)[0]?.headers.get('If-Match')).toBeNull();
  });

  it('rejects a grade that is not a number', async () => {
    const api = serveApi({ ...signedInRoutes, [`GET ${editorPath}`]: () => json(200, editor()) });
    renderApp(`/app/resumes/${id}`);
    await openSection(e.title);
    fireEvent.click(await screen.findByRole('button', { name: it8.add(e.one) }));
    fireEvent.change(await screen.findByLabelText(e.institutionName), { target: { value: 'Uydurma' } });
    fireEvent.change(screen.getByLabelText(e.grade), { target: { value: 'iyi' } });

    fireEvent.click(screen.getByRole('button', { name: messages.common.save }));

    expect(await screen.findByText(e.gradeInvalid)).toBeTruthy();
    expect(api.callsTo('POST', `/api/resumes/${id}/educations`)).toHaveLength(0);
  });
});
