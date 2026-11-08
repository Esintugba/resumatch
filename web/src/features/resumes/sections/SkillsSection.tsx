import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useId, useMemo, useState } from 'react';
import { useForm } from 'react-hook-form';
import { describeError } from '../../../api/describeError';
import { ApiError } from '../../../api/http';
import { useSession } from '../../../auth/AuthContext';
import { Alert } from '../../../components/ui/Alert';
import { Button } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import text from '../../../components/ui/Text.module.css';
import { TextField } from '../../../components/ui/TextField';
import field from '../../../components/ui/TextField.module.css';
import { applyProblem } from '../../../forms/problems';
import { messages } from '../../../i18n';
import { fetchEditor, pickMain, resumeKeys, useUpdateResumeMain, type ResumeEditor } from '../api';
import { ConflictAlert } from '../editor/ConflictAlert';
import { SaveBar } from '../editor/EditorSection';
import {
  bySortOrder,
  nextSortOrder,
  sectionLimits,
  skillsApi,
  useSectionApis,
  type CatalogSkill,
  type ResumeSkill,
  type SkillLevel,
} from '../sectionsApi';
import styles from './SkillsSection.module.css';

const t = messages.skills;
const levels = Object.keys(t.levels) as SkillLevel[];
const searchDelayMs = 250;

export function SkillsSummary({ resume }: { resume: ResumeEditor }) {
  const items = bySortOrder(resume.skills);
  return (
    <>
      <p>{items.length === 0 ? <span className={text.muted}>{t.empty}</span> : items.map((skill) => skill.name).join(', ')}</p>
      {resume.unlistedSkillsNote && (
        <p>
          <span className={text.muted}>{t.note}: </span>
          {resume.unlistedSkillsNote}
        </p>
      )}
    </>
  );
}

interface SearchState {
  term: string;
  status: 'loading' | 'done' | 'error';
  results: CatalogSkill[];
}

/**
 * Searches the catalog after a short pause. Each new term cancels the
 * previous request, and only the answer for the current term is shown. The
 * API itself decides what matches: one character finds exact matches only.
 */
function useSkillSearch(term: string, search: (term: string, signal: AbortSignal) => Promise<CatalogSkill[]>) {
  const [state, setState] = useState<SearchState | null>(null);
  const trimmed = term.trim();

  useEffect(() => {
    if (trimmed === '') return;
    const controller = new AbortController();
    const timer = setTimeout(() => {
      setState({ term: trimmed, status: 'loading', results: [] });
      search(trimmed, controller.signal)
        .then((results) => {
          setState({ term: trimmed, status: 'done', results });
        })
        .catch(() => {
          // A cancelled search is replaced by the next one and shows nothing.
          if (controller.signal.aborted) return;
          setState({ term: trimmed, status: 'error', results: [] });
        });
    }, searchDelayMs);
    return () => {
      clearTimeout(timer);
      controller.abort();
    };
  }, [trimmed, search]);

  return trimmed === '' || state?.term !== trimmed ? null : state;
}

interface SectionFormProps {
  resume: ResumeEditor;
  onDirtyChange: (dirty: boolean) => void;
  onClose: () => void;
}

export function SkillsSection({ resume, onDirtyChange }: SectionFormProps) {
  const id = resume.id ?? '';
  const apis = useSectionApis(id);
  const session = useSession();
  const client = useQueryClient();
  const skills = bySortOrder(resume.skills);
  const [term, setTerm] = useState('');
  // Stable, so a re-render does not restart the pending search.
  const searchCatalog = useMemo(() => skillsApi(session, client, id).search, [session, client, id]);
  const search = useSkillSearch(term, searchCatalog);
  const [toDelete, setToDelete] = useState<ResumeSkill | null>(null);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const [noteToAdd, setNoteToAdd] = useState<string | null>(null);
  const searchId = useId();
  const added = new Set(skills.map((skill) => skill.skillId));
  const full = skills.length >= sectionLimits.skills;

  async function run(action: () => Promise<string>) {
    setBusy(true);
    setFailure(null);
    setStatus(null);
    try {
      setStatus(await action());
    } catch (error) {
      setFailure(describeError(error));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      {failure !== null && (
        <Alert tone="error" role="alert">
          <p>{failure}</p>
        </Alert>
      )}
      {status !== null && (
        <Alert tone="success" role="status">
          <p>{status}</p>
        </Alert>
      )}

      {skills.length === 0 ? (
        <p className={text.muted}>{t.empty}</p>
      ) : (
        <ul className={styles.list} aria-label={t.listLabel}>
          {skills.map((skill) => {
            const name = skill.name ?? '';
            return (
              <li key={skill.skillId} className={styles.skill}>
                <span className={styles.name}>
                  {name}
                  {skill.category && <span className={styles.category}>{t.categories[skill.category]}</span>}
                </span>
                <select
                  aria-label={t.levelFor(name)}
                  value={skill.level ?? ''}
                  disabled={busy}
                  onChange={(event) => {
                    const level = event.target.value === '' ? null : (event.target.value as SkillLevel);
                    void run(async () => {
                      await apis.skills.update(skill, level);
                      return t.levelSaved(name);
                    });
                  }}
                >
                  <option value="">{t.noLevel}</option>
                  {levels.map((level) => (
                    <option key={level} value={level}>
                      {t.levels[level]}
                    </option>
                  ))}
                </select>
                <Button
                  disabled={busy}
                  aria-label={messages.items.delete(name)}
                  onClick={() => {
                    setToDelete(skill);
                  }}
                >
                  {messages.items.deleteConfirm}
                </Button>
              </li>
            );
          })}
        </ul>
      )}

      <div className={styles.search} role="search">
        <label htmlFor={searchId}>{t.search}</label>
        <p id={`${searchId}-hint`} className={text.muted}>
          {t.searchHint} {messages.items.count(skills.length, sectionLimits.skills)}
        </p>
        <input
          id={searchId}
          type="search"
          className={field.input}
          aria-describedby={`${searchId}-hint ${searchId}-count`}
          autoComplete="off"
          value={term}
          onChange={(event) => {
            setTerm(event.target.value);
          }}
        />
        <p id={`${searchId}-count`} role="status" className={text.muted}>
          {search?.status === 'loading' ? t.searching : search?.status === 'done' ? t.results(search.results.length) : ''}
        </p>
        {search?.status === 'error' && (
          <Alert tone="error" role="alert">
            <p>{messages.errors.network}</p>
          </Alert>
        )}
        {search?.status === 'done' && search.results.length > 0 && (
          <ul className={styles.results} aria-label={t.resultsLabel}>
            {search.results.map((skill) => {
              const name = skill.name ?? '';
              const already = added.has(skill.id);
              return (
                <li key={skill.id} className={styles.result}>
                  <span>
                    {name}
                    {skill.nameTr && skill.nameTr !== name && <span className={text.muted}> ({skill.nameTr})</span>}
                    {skill.category && <span className={styles.category}>{t.categories[skill.category]}</span>}
                  </span>
                  <Button
                    tone={already ? 'secondary' : 'primary'}
                    disabled={already || busy || full}
                    aria-label={already ? `${t.added}: ${name}` : t.add(name)}
                    onClick={() => {
                      void run(async () => {
                        await apis.skills.add(skill.id ?? '', nextSortOrder(resume.skills));
                        return t.addedDone(name);
                      });
                    }}
                  >
                    {already ? t.added : t.addShort}
                  </Button>
                </li>
              );
            })}
          </ul>
        )}
        {search?.status === 'done' && search.results.length === 0 && (
          <div className={styles.notFound}>
            <p>
              <strong>{t.notFound(search.term)}</strong> {t.notFoundHint}
            </p>
            <Button
              onClick={() => {
                setNoteToAdd(search.term);
              }}
            >
              {t.addToNote(search.term)}
            </Button>
          </div>
        )}
      </div>

      <UnlistedSkillsNote
        resume={resume}
        onDirtyChange={onDirtyChange}
        append={noteToAdd}
        onAppended={() => {
          setNoteToAdd(null);
          setTerm('');
        }}
      />

      {toDelete !== null && (
        <ConfirmDialog
          title={messages.items.deleteTitle(toDelete.name ?? '')}
          description={messages.items.deleteDescription}
          confirmLabel={messages.items.deleteConfirm}
          cancelLabel={messages.common.cancel}
          onCancel={() => {
            setToDelete(null);
          }}
          onConfirm={() => {
            const skill = toDelete;
            setToDelete(null);
            void run(async () => {
              await apis.skills.remove(skill.skillId ?? '');
              return messages.items.deleted(skill.name ?? '');
            });
          }}
        />
      )}
    </div>
  );
}

interface NoteProps {
  resume: ResumeEditor;
  onDirtyChange: (dirty: boolean) => void;
  /** A term to add to the note, from a search without results. */
  append: string | null;
  onAppended: () => void;
}

/**
 * The "not in the catalog" note. It is a field of the resume row, so it
 * shares the resume ETag (fieldsSharingResumeETag) and is saved with the
 * other main fields exactly as the form opened them.
 */
function UnlistedSkillsNote({ resume, onDirtyChange, append, onAppended }: NoteProps) {
  const id = resume.id ?? '';
  const session = useSession();
  const client = useQueryClient();
  const [base, setBase] = useState(() => ({ eTag: resume.eTag ?? '', fields: pickMain(resume) }));
  const form = useForm<{ note: string }>({ defaultValues: { note: base.fields.unlistedSkillsNote ?? '' } });
  const { errors, isDirty } = form.formState;
  const save = useUpdateResumeMain(id);
  const [conflict, setConflict] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);

  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  useEffect(() => {
    if (append === null) return;
    const current = form.getValues('note').trim();
    form.setValue('note', current ? `${current}, ${append}` : append, { shouldDirty: true });
    form.setFocus('note');
    onAppended();
  }, [append, form, onAppended]);

  const submit = async ({ note }: { note: string }) => {
    if (save.isPending || conflict) return;
    setFailure(null);
    setStatus(null);
    try {
      const saved = await save.mutateAsync({
        eTag: base.eTag,
        body: { ...base.fields, unlistedSkillsNote: note.trim() === '' ? null : note.trim() },
      });
      const fields = pickMain(saved);
      setBase({ eTag: saved.eTag ?? base.eTag, fields });
      form.reset({ note: fields.unlistedSkillsNote ?? '' });
      setStatus(t.noteSaved);
    } catch (error) {
      if (error instanceof ApiError && error.status === 412) {
        setConflict(true);
        return;
      }
      setFailure(applyProblem(error, { fields: [] }, form.setError));
    }
  };

  async function reload() {
    setReloading(true);
    try {
      const fresh = await client.query({ queryKey: resumeKeys.editor(id), queryFn: () => fetchEditor(session, id), staleTime: 0 });
      const fields = pickMain(fresh);
      setBase({ eTag: fresh.eTag ?? '', fields });
      form.reset({ note: fields.unlistedSkillsNote ?? '' });
      setConflict(false);
      setStatus(messages.conflict.reloaded);
    } catch (error) {
      setFailure(describeError(error));
    } finally {
      setReloading(false);
    }
  }

  const visibleStatus = isDirty ? null : status;

  return (
    <form className={styles.note} noValidate onSubmit={(event) => void form.handleSubmit(submit)(event)}>
      {conflict && <ConflictAlert reloading={reloading} onReload={() => void reload()} />}
      {failure !== null && (
        <Alert tone="error" role="alert" title={messages.forms.errorSummary}>
          <p>{failure}</p>
        </Alert>
      )}
      {visibleStatus !== null && (
        <Alert tone="success" role="status">
          <p>{visibleStatus}</p>
        </Alert>
      )}
      <TextField
        multiline
        label={t.note}
        hint={t.noteHint}
        rows={3}
        registration={form.register('note', { maxLength: { value: 500, message: messages.forms.maxLength(500) } })}
        error={errors.note?.message}
      />
      <SaveBar>
        <Button type="submit" tone="primary" disabled={save.isPending || conflict} aria-busy={save.isPending || undefined}>
          {save.isPending ? messages.common.saving : messages.common.save}
        </Button>
      </SaveBar>
    </form>
  );
}
