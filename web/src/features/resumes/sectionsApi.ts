import { useQueryClient, type QueryClient } from '@tanstack/react-query';
import { apiJson } from '../../api/http';
import type { components } from '../../api/schema';
import { useSession } from '../../auth/AuthContext';
import type { AuthSession } from '../../auth/session';
import { resumeKeys, type ResumeEditor } from './api';

type Schemas = components['schemas'];
export type Experience = Schemas['ExperienceResponse'];
export type ExperienceRequest = Schemas['UpdateExperienceRequest'];
export type Education = Schemas['EducationResponse'];
export type EducationRequest = Schemas['UpdateEducationRequest'];
export type ResumeSkill = Schemas['ResumeSkillResponse'];
export type CatalogSkill = Schemas['SkillResponse'];
export type SkillLevel = Schemas['SkillLevel'];

/** Section limits from the API (ResumeSectionLimits); adding beyond them answers 409 section_limit_reached. */
export const sectionLimits = { experiences: 30, educations: 20, skills: 100 } as const;

const base = (resumeId: string) => `/api/resumes/${encodeURIComponent(resumeId)}`;
const item = (resumeId: string, section: string, id: string) =>
  `${base(resumeId)}/${section}/${encodeURIComponent(id)}`;

/** Changes one array of the cached editor, so the list updates at once. */
function patchEditor(client: QueryClient, resumeId: string, change: (editor: ResumeEditor) => ResumeEditor) {
  client.setQueryData<ResumeEditor>(resumeKeys.editor(resumeId), (current) => (current ? change(current) : current));
}

function upsert<T extends { id?: string }>(list: T[] | undefined, saved: T): T[] {
  const items = list ?? [];
  return items.some((entry) => entry.id === saved.id)
    ? items.map((entry) => (entry.id === saved.id ? saved : entry))
    : [...items, saved];
}

/** Sorted by sortOrder; ties keep the API's order. */
export function bySortOrder<T extends { sortOrder?: number | string }>(items: T[] | undefined): T[] {
  return [...(items ?? [])]
    .map((entry, index) => ({ entry, index }))
    .sort((a, b) => Number(a.entry.sortOrder ?? 0) - Number(b.entry.sortOrder ?? 0) || a.index - b.index)
    .map(({ entry }) => entry);
}

/** The next sortOrder for an item added at the end. */
export function nextSortOrder(items: { sortOrder?: number | string }[] | undefined): number {
  return (items ?? []).reduce((max, entry) => Math.max(max, Number(entry.sortOrder ?? 0)), -1) + 1;
}

/**
 * Experience has its own ETag: updates send If-Match (412 when changed
 * elsewhere). Its PUT replaces the bullets too, so callers send them back.
 */
export function experienceApi(session: AuthSession, client: QueryClient, resumeId: string) {
  const store = (saved: Experience) => {
    patchEditor(client, resumeId, (editor) => ({ ...editor, experiences: upsert(editor.experiences, saved) }));
  };
  return {
    create: async (body: Schemas['CreateExperienceRequest']) => {
      const saved = await apiJson<Experience>(session, `${base(resumeId)}/experiences`, { method: 'POST', body });
      store(saved);
      return saved;
    },
    update: async (id: string, body: ExperienceRequest, eTag: string) => {
      const saved = await apiJson<Experience>(session, item(resumeId, 'experiences', id), {
        method: 'PUT',
        body,
        ifMatch: eTag,
      });
      store(saved);
      return saved;
    },
    remove: async (id: string) => {
      await apiJson<undefined>(session, item(resumeId, 'experiences', id), { method: 'DELETE' });
      patchEditor(client, resumeId, (editor) => ({
        ...editor,
        experiences: (editor.experiences ?? []).filter((entry) => entry.id !== id),
      }));
    },
  };
}

/** Education has no ETag in the API: the last save wins. */
export function educationApi(session: AuthSession, client: QueryClient, resumeId: string) {
  const store = (saved: Education) => {
    patchEditor(client, resumeId, (editor) => ({ ...editor, educations: upsert(editor.educations, saved) }));
  };
  return {
    create: async (body: Schemas['CreateEducationRequest']) => {
      const saved = await apiJson<Education>(session, `${base(resumeId)}/educations`, { method: 'POST', body });
      store(saved);
      return saved;
    },
    update: async (id: string, body: EducationRequest) => {
      const saved = await apiJson<Education>(session, item(resumeId, 'educations', id), { method: 'PUT', body });
      store(saved);
      return saved;
    },
    remove: async (id: string) => {
      await apiJson<undefined>(session, item(resumeId, 'educations', id), { method: 'DELETE' });
      patchEditor(client, resumeId, (editor) => ({
        ...editor,
        educations: (editor.educations ?? []).filter((entry) => entry.id !== id),
      }));
    },
  };
}

export function skillsApi(session: AuthSession, client: QueryClient, resumeId: string) {
  const store = (saved: ResumeSkill) => {
    patchEditor(client, resumeId, (editor) => ({
      ...editor,
      skills: (editor.skills ?? []).some((entry) => entry.skillId === saved.skillId)
        ? (editor.skills ?? []).map((entry) => (entry.skillId === saved.skillId ? saved : entry))
        : [...(editor.skills ?? []), saved],
    }));
  };
  return {
    search: (term: string, signal: AbortSignal) =>
      apiJson<CatalogSkill[]>(session, `/api/skills?search=${encodeURIComponent(term)}&pageSize=10`, { signal }),
    add: async (skillId: string, sortOrder: number) => {
      const saved = await apiJson<ResumeSkill>(session, `${base(resumeId)}/skills`, {
        method: 'POST',
        body: { skillId, level: null, sortOrder, isHighlighted: false },
      });
      store(saved);
      return saved;
    },
    update: async (skill: ResumeSkill, level: SkillLevel | null) => {
      const saved = await apiJson<ResumeSkill>(session, item(resumeId, 'skills', skill.skillId ?? ''), {
        method: 'PUT',
        body: { level, sortOrder: Number(skill.sortOrder ?? 0), isHighlighted: skill.isHighlighted ?? false },
      });
      store(saved);
      return saved;
    },
    remove: async (skillId: string) => {
      await apiJson<undefined>(session, item(resumeId, 'skills', skillId), { method: 'DELETE' });
      patchEditor(client, resumeId, (editor) => ({
        ...editor,
        skills: (editor.skills ?? []).filter((entry) => entry.skillId !== skillId),
      }));
    },
  };
}

/** The section APIs bound to the signed-in session and the query cache. */
export function useSectionApis(resumeId: string) {
  const session = useSession();
  const client = useQueryClient();
  return {
    experience: experienceApi(session, client, resumeId),
    education: educationApi(session, client, resumeId),
    skills: skillsApi(session, client, resumeId),
  };
}
