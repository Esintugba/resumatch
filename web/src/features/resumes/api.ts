import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiJson } from '../../api/http';
import type { components } from '../../api/schema';
import { useSession } from '../../auth/AuthContext';
import type { AuthSession } from '../../auth/session';

type Schemas = components['schemas'];
export type ResumeListPage = Schemas['PagedResponseOfResumeListItemResponse'];
export type ResumeListItem = Schemas['ResumeListItemResponse'];
export type ResumeEditor = Schemas['ResumeEditorResponse'];
export type ResumeResponse = Schemas['ResumeResponse'];
export type UpdateResumeRequest = Schemas['UpdateResumeRequest'];
export type ContactRequest = Schemas['ResumeContactRequest'];
export type ContactResponse = Schemas['ResumeContactResponse'];

export const resumeListPageSize = 20;

export const resumeKeys = {
  all: ['resumes'] as const,
  list: (page: number) => ['resumes', 'list', page] as const,
  editor: (id: string) => ['resumes', 'editor', id] as const,
};

export function fetchEditor(session: AuthSession, id: string, signal?: AbortSignal) {
  return apiJson<ResumeEditor>(session, `/api/resumes/${encodeURIComponent(id)}/editor`, signal ? { signal } : {});
}

export function useResumeList(page: number) {
  const session = useSession();
  return useQuery({
    queryKey: resumeKeys.list(page),
    queryFn: ({ signal }) =>
      apiJson<ResumeListPage>(session, `/api/resumes?page=${String(page)}&pageSize=${String(resumeListPageSize)}`, {
        signal,
      }),
  });
}

export function useResumeEditor(id: string) {
  const session = useSession();
  return useQuery({
    queryKey: resumeKeys.editor(id),
    queryFn: ({ signal }) => fetchEditor(session, id, signal),
  });
}

export function useCreateResume() {
  const session = useSession();
  const client = useQueryClient();
  return useMutation({
    mutationFn: (title: string) =>
      apiJson<ResumeResponse>(session, '/api/resumes', { method: 'POST', body: { title } }),
    onSuccess: () => client.invalidateQueries({ queryKey: [...resumeKeys.all, 'list'] }),
  });
}

export function useArchiveResume() {
  const session = useSession();
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      apiJson<undefined>(session, `/api/resumes/${encodeURIComponent(id)}/archive`, { method: 'PATCH' }),
    onSuccess: (_result, id) =>
      Promise.all([
        client.invalidateQueries({ queryKey: [...resumeKeys.all, 'list'] }),
        client.invalidateQueries({ queryKey: resumeKeys.editor(id) }),
      ]),
  });
}

/**
 * Saves the main fields with If-Match set to the ETag the form started from.
 * PUT replaces every field, so fields the form does not show are sent back
 * unchanged. A 412 surfaces as ApiError; the cache is updated only on success.
 */
export function useUpdateResumeMain(id: string) {
  const session = useSession();
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ body, eTag }: { body: UpdateResumeRequest; eTag: string }) =>
      apiJson<ResumeResponse>(session, `/api/resumes/${encodeURIComponent(id)}`, {
        method: 'PUT',
        body,
        ifMatch: eTag,
      }),
    onSuccess: (saved) => {
      client.setQueryData<ResumeEditor>(resumeKeys.editor(id), (current) =>
        current
          ? {
              ...current,
              ...pickMain(saved),
              ...(saved.eTag === undefined ? {} : { eTag: saved.eTag }),
              ...(saved.updatedAt === undefined ? {} : { updatedAt: saved.updatedAt }),
            }
          : current,
      );
      return client.invalidateQueries({ queryKey: [...resumeKeys.all, 'list'] });
    },
  });
}

/** Contact has no ETag in the API, so the last save wins (see the PR notes). */
export function useUpdateContact(id: string) {
  const session = useSession();
  const client = useQueryClient();
  return useMutation({
    mutationFn: (body: ContactRequest) =>
      apiJson<ContactResponse>(session, `/api/resumes/${encodeURIComponent(id)}/contact`, { method: 'PUT', body }),
    onSuccess: (contact) => {
      client.setQueryData<ResumeEditor>(resumeKeys.editor(id), (current) =>
        current ? { ...current, contact } : current,
      );
    },
  });
}

/** The fields UpdateResumeRequest replaces. */
export function pickMain(resume: ResumeEditor | ResumeResponse): UpdateResumeRequest {
  return {
    title: resume.title ?? '',
    professionalTitle: resume.professionalTitle ?? null,
    summary: resume.summary ?? null,
    unlistedSkillsNote: resume.unlistedSkillsNote ?? null,
    templateKey: resume.templateKey ?? null,
    targetRole: resume.targetRole ?? null,
  };
}
