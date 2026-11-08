import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { ApiError } from '../../../api/http';
import { useSession, useSessionState } from '../../../auth/AuthContext';
import { Alert } from '../../../components/ui/Alert';
import { Button } from '../../../components/ui/Button';
import { TextField } from '../../../components/ui/TextField';
import { applyProblem, type ProblemMapping } from '../../../forms/problems';
import { messages } from '../../../i18n';
import { fetchEditor, resumeKeys, useUpdateContact, type ContactResponse, type ResumeEditor } from '../api';
import { ConflictAlert } from '../editor/ConflictAlert';
import { SaveBar, SectionSummary } from '../editor/EditorSection';

const t = messages.editor.contact;

interface ContactValues {
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
  city: string;
  country: string;
  postalCode: string;
}

const fields = ['firstName', 'lastName', 'email', 'phoneNumber', 'city', 'country', 'postalCode'] as const;
const mapping: ProblemMapping<ContactValues> = { fields };
const limits: Record<keyof ContactValues, number> = {
  firstName: 100,
  lastName: 100,
  email: 255,
  phoneNumber: 30,
  city: 100,
  country: 100,
  postalCode: 20,
};

function toValues(contact: ContactResponse | null | undefined, fallback: Partial<ContactValues>): ContactValues {
  return {
    firstName: contact?.firstName ?? fallback.firstName ?? '',
    lastName: contact?.lastName ?? fallback.lastName ?? '',
    email: contact?.email ?? fallback.email ?? '',
    phoneNumber: contact?.phoneNumber ?? '',
    city: contact?.city ?? '',
    country: contact?.country ?? '',
    postalCode: contact?.postalCode ?? '',
  };
}

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

export function ContactSummary({ resume }: { resume: ResumeEditor }) {
  const contact = resume.contact;
  const name = [contact?.firstName, contact?.lastName].filter(Boolean).join(' ');
  const place = [contact?.city, contact?.country].filter(Boolean).join(', ');
  return (
    <SectionSummary
      rows={[
        [`${t.firstName} ${t.lastName}`, name],
        [t.email, contact?.email],
        [t.phoneNumber, contact?.phoneNumber],
        [`${t.city}, ${t.country}`, place],
        [t.postalCode, contact?.postalCode],
      ]}
    />
  );
}

interface SectionFormProps {
  resume: ResumeEditor;
  onDirtyChange: (dirty: boolean) => void;
  onClose: () => void;
}

/** Edits the contact block. A resume without one starts from the signed-in user's name and e-mail. */
export function ContactForm({ resume, onDirtyChange, onClose }: SectionFormProps) {
  const state = useSessionState();
  const user = state.status === 'signed-in' ? state.user : null;
  const fallback = { firstName: user?.firstName ?? '', lastName: user?.lastName ?? '', email: user?.email ?? '' };
  const form = useForm<ContactValues>({ defaultValues: toValues(resume.contact, fallback), mode: 'onSubmit' });
  const { errors, isDirty } = form.formState;
  const session = useSession();
  const client = useQueryClient();
  const id = resume.id ?? '';
  const save = useUpdateContact(id);
  // The contact's own ETag when the form opened; null while there is no contact yet.
  const [eTag, setETag] = useState<string | null>(() => resume.contact?.eTag ?? null);
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

  const submit = async (values: ContactValues) => {
    if (save.isPending || conflict) return;
    setFailure(null);
    setStatus(null);
    try {
      const saved = await save.mutateAsync({
        eTag,
        body: {
          firstName: values.firstName.trim(),
          lastName: values.lastName.trim(),
          email: values.email.trim(),
          phoneNumber: orNull(values.phoneNumber),
          city: orNull(values.city),
          country: orNull(values.country),
          postalCode: orNull(values.postalCode),
        },
      });
      setETag(saved.eTag ?? null);
      form.reset(toValues(saved, {}));
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
      setETag(fresh.contact?.eTag ?? null);
      form.reset(toValues(fresh.contact, fallback));
      setConflict(false);
      setStatus(messages.conflict.reloaded);
    } catch (error) {
      setFailure(applyProblem(error, mapping, form.setError));
    } finally {
      setReloading(false);
    }
  }

  const required = { value: true, message: messages.forms.required };
  const max = (field: keyof ContactValues) => ({ value: limits[field], message: messages.forms.maxLength(limits[field]) });
  const notBlank = (value: string) => value.trim() !== '' || messages.forms.required;

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
        label={t.firstName}
        registration={form.register('firstName', { required, maxLength: max('firstName'), validate: notBlank })}
        error={errors.firstName?.message}
        autoComplete="given-name"
      />
      <TextField
        label={t.lastName}
        registration={form.register('lastName', { required, maxLength: max('lastName'), validate: notBlank })}
        error={errors.lastName?.message}
        autoComplete="family-name"
      />
      <TextField
        label={t.email}
        type="email"
        inputMode="email"
        registration={form.register('email', {
          required,
          maxLength: max('email'),
          pattern: { value: /^[^\s@]+@[^\s@]+\.[^\s@]+$/, message: messages.forms.emailInvalid },
        })}
        error={errors.email?.message}
        autoComplete="email"
      />
      <TextField
        label={t.phoneNumber}
        type="tel"
        inputMode="tel"
        registration={form.register('phoneNumber', { maxLength: max('phoneNumber') })}
        error={errors.phoneNumber?.message}
        autoComplete="tel"
      />
      <TextField
        label={t.city}
        registration={form.register('city', { maxLength: max('city') })}
        error={errors.city?.message}
        autoComplete="address-level2"
      />
      <TextField
        label={t.country}
        registration={form.register('country', { maxLength: max('country') })}
        error={errors.country?.message}
        autoComplete="country-name"
      />
      <TextField
        label={t.postalCode}
        registration={form.register('postalCode', { maxLength: max('postalCode') })}
        error={errors.postalCode?.message}
        autoComplete="postal-code"
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
