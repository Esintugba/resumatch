import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../../api/http';
import { useSession } from '../../../auth/AuthContext';
import { Alert } from '../../../components/ui/Alert';
import { Button } from '../../../components/ui/Button';
import { TextField } from '../../../components/ui/TextField';
import { applyProblem, type ProblemMapping } from '../../../forms/problems';
import { messages } from '../../../i18n';
import { fetchEditor, pickMain, resumeKeys, useUpdateResumeMain, type ResumeEditor, type UpdateResumeRequest } from '../api';
import { ConflictAlert } from '../editor/ConflictAlert';
import { SaveBar, SectionSummary } from '../editor/EditorSection';

const t = messages.editor.main;

interface MainValues {
  title: string;
  professionalTitle: string;
  targetRole: string;
  summary: string;
}

const mapping: ProblemMapping<MainValues> = { fields: ['title', 'professionalTitle', 'targetRole', 'summary'] };

const limits = { title: 200, professionalTitle: 200, targetRole: 200, summary: 3000 } as const;

function toValues(resume: UpdateResumeRequest): MainValues {
  return {
    title: resume.title,
    professionalTitle: resume.professionalTitle ?? '',
    targetRole: resume.targetRole ?? '',
    summary: resume.summary ?? '',
  };
}

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

export function MainInfoSummary({ resume }: { resume: ResumeEditor }) {
  return (
    <SectionSummary
      rows={[
        [t.resumeTitle, resume.title],
        [t.professionalTitle, resume.professionalTitle],
        [t.targetRole, resume.targetRole],
        [t.summary, resume.summary],
      ]}
    />
  );
}

interface SectionFormProps {
  resume: ResumeEditor;
  onDirtyChange: (dirty: boolean) => void;
  onClose: () => void;
}

/**
 * Edits the main fields. The ETag and the fields the form does not show are
 * taken when the form opens, so a newer version saved elsewhere answers 412
 * instead of being overwritten.
 */
export function MainInfoForm({ resume, onDirtyChange, onClose }: SectionFormProps) {
  const session = useSession();
  const client = useQueryClient();
  const id = resume.id ?? '';
  const [base, setBase] = useState(() => ({ eTag: resume.eTag ?? '', fields: pickMain(resume) }));
  const form = useForm<MainValues>({ defaultValues: toValues(base.fields), mode: 'onSubmit' });
  const { errors, isDirty } = form.formState;
  const save = useUpdateResumeMain(id);
  const [conflict, setConflict] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const failureRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);
  // The saved message goes away as soon as the user changes something again.
  const visibleStatus = isDirty ? null : status;

  const submit = async (values: MainValues) => {
    if (save.isPending || conflict) return;
    setFailure(null);
    setStatus(null);
    try {
      const saved = await save.mutateAsync({
        eTag: base.eTag,
        body: {
          ...base.fields,
          title: values.title.trim(),
          professionalTitle: orNull(values.professionalTitle),
          targetRole: orNull(values.targetRole),
          summary: orNull(values.summary),
        },
      });
      const fields = pickMain(saved);
      setBase({ eTag: saved.eTag ?? base.eTag, fields });
      form.reset(toValues(fields));
      setStatus(messages.forms.saved(t.title));
    } catch (error) {
      if (error instanceof ApiError && error.status === 412) {
        setConflict(true);
        return;
      }
      setFailure(applyProblem(error, mapping, form.setError));
      requestAnimationFrame(() => failureRef.current?.focus());
    }
  };

  async function reload() {
    setReloading(true);
    setFailure(null);
    try {
      const fresh = await client.query({
        queryKey: resumeKeys.editor(id),
        queryFn: ({ signal }) => fetchEditor(session, id, signal),
        staleTime: 0,
      });
      const fields = pickMain(fresh);
      setBase({ eTag: fresh.eTag ?? '', fields });
      form.reset(toValues(fields));
      setConflict(false);
      setStatus(messages.conflict.reloaded);
    } catch (error) {
      setFailure(applyProblem(error, mapping, form.setError));
    } finally {
      setReloading(false);
    }
  }

  const required = { value: true, message: messages.forms.required };
  const max = (length: number) => ({ value: length, message: messages.forms.maxLength(length) });

  return (
    <form noValidate onSubmit={(event) => void form.handleSubmit(submit)(event)}>
      {conflict && <ConflictAlert reloading={reloading} onReload={() => void reload()} />}
      {failure !== null && (
        <Alert ref={failureRef} tone="error" role="alert" title={messages.forms.errorSummary}>
          <p>{failure}</p>
        </Alert>
      )}
      {visibleStatus !== null && (
        <Alert tone="success" role="status">
          <p>{visibleStatus}</p>
        </Alert>
      )}

      <TextField
        label={t.resumeTitle}
        registration={form.register('title', {
          required,
          maxLength: max(limits.title),
          validate: (value) => value.trim() !== '' || messages.forms.required,
        })}
        error={errors.title?.message}
        autoComplete="off"
      />
      <TextField
        label={t.professionalTitle}
        registration={form.register('professionalTitle', { maxLength: max(limits.professionalTitle) })}
        error={errors.professionalTitle?.message}
        autoComplete="organization-title"
      />
      <TextField
        label={t.targetRole}
        registration={form.register('targetRole', { maxLength: max(limits.targetRole) })}
        error={errors.targetRole?.message}
        autoComplete="off"
      />
      <TextField
        multiline
        label={t.summary}
        hint={t.summaryHint}
        registration={form.register('summary', { maxLength: max(limits.summary) })}
        error={errors.summary?.message}
        rows={6}
      />

      <SaveBar>
        <Button type="submit" tone="primary" disabled={save.isPending || conflict} aria-busy={save.isPending || undefined}>
          {save.isPending ? messages.common.saving : messages.common.save}
        </Button>
        <Button tone="secondary" onClick={onClose}>
          {messages.common.close}
        </Button>
      </SaveBar>
    </form>
  );
}
