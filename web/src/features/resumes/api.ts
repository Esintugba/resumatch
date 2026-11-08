import { useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query';
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

/**
 * Fields stored on the resume row. They share one ETag (the row version): a
 * save of any of them changes the ETag every other form editing one of them
 * must send as If-Match. PR 8b: the skills section's "not in the catalog"
 * note is `unlistedSkillsNote`, so it shares this ETag with the main fields.
 * Contact has its own ETag; experience, education and skills are separate rows.
 */
export const fieldsSharingResumeETag = [
  'title',
  'professionalTitle',
  'summary',
  'unlistedSkillsNote',
  'templateKey',
  'targetRole',
  'isArchived',
] as const;

/**
 * Writes the ETag (and changed fields) a save returned into every cached copy
 * of the resume, so the next form the user opens sends it and their own save
 * never comes back as 412.
 */
export function rememberResumeETag(
  client: QueryClient,
  id: string,
  eTag: string,
  changes: Partial<ResumeEditor> = {},
): void {
  client.setQueryData<ResumeEditor>(resumeKeys.editor(id), (current) =>
    current ? { ...current, ...changes, eTag } : current,
  );
  client.setQueriesData<ResumeListPage>({ queryKey: [...resumeKeys.all, 'list'] }, (current) =>
    current?.items
      ? {
          ...current,
          items: current.items.map((item) =>
            item.id === id ? { ...item, ...pickListFields(changes), eTag } : item,
          ),
        }
      : current,
  );
}

function pickListFields(changes: Partial<ResumeEditor>): Partial<ResumeListItem> {
  const picked: Partial<ResumeListItem> = {};
  if (changes.title !== undefined) picked.title = changes.title;
  if (changes.professionalTitle !== undefined) picked.professionalTitle = changes.professionalTitle;
  if (changes.targetRole !== undefined) picked.targetRole = changes.targetRole;
  if (changes.updatedAt !== undefined) picked.updatedAt = changes.updatedAt;
  return picked;
}

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

/**
 * Archives or unarchives with If-Match set to the ETag the list shows. The
 * API answers 200 with the new ETag, which every cached copy then gets.
 */
export function useSetArchived() {
  const session = useSession();
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ id, eTag, archived }: { id: string; eTag: string; archived: boolean }) =>
      apiJson<ResumeResponse>(
        session,
        `/api/resumes/${encodeURIComponent(id)}/${archived ? 'archive' : 'unarchive'}`,
        { method: 'PATCH', ifMatch: eTag },
      ),
    onSuccess: (saved, { id }) => {
      if (saved.eTag !== undefined) {
        rememberResumeETag(client, id, saved.eTag, {
          isArchived: saved.isArchived ?? false,
          ...(saved.updatedAt === undefined ? {} : { updatedAt: saved.updatedAt }),
        });
      }
      return client.invalidateQueries({ queryKey: [...resumeKeys.all, 'list'] });
    },
    // A 412 means the list is out of date; fetch it again for fresh ETags.
    onError: () => client.invalidateQueries({ queryKey: [...resumeKeys.all, 'list'] }),
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
      if (saved.eTag !== undefined) {
        rememberResumeETag(client, id, saved.eTag, {
          ...pickMain(saved),
          ...(saved.updatedAt === undefined ? {} : { updatedAt: saved.updatedAt }),
        });
      }
      // The list order depends on the update time, so it is fetched again too.
      return client.invalidateQueries({ queryKey: [...resumeKeys.all, 'list'] });
    },
  });
}

/**
 * Saves the contact. It has its own ETag: an existing contact is sent with
 * If-Match (412 when changed elsewhere); a new one is created without it.
 */
export function useUpdateContact(id: string) {
  const session = useSession();
  const client = useQueryClient();
  return useMutation({
    mutationFn: ({ body, eTag }: { body: ContactRequest; eTag: string | null }) =>
      apiJson<ContactResponse>(session, `/api/resumes/${encodeURIComponent(id)}/contact`, {
        method: 'PUT',
        body,
        ...(eTag ? { ifMatch: eTag } : {}),
      }),
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
