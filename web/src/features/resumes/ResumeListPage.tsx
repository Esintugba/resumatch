import { useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useNavigate } from 'react-router';
import { describeError } from '../../api/describeError';
import { usePageHeading } from '../../components/usePageTitle';
import { Alert } from '../../components/ui/Alert';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
import text from '../../components/ui/Text.module.css';
import { TextField } from '../../components/ui/TextField';
import { applyProblem } from '../../forms/problems';
import { messages } from '../../i18n';
import { useArchiveResume, useCreateResume, useResumeList, type ResumeListItem } from './api';
import styles from './ResumeListPage.module.css';

const t = messages.resumes;
const dateFormat = new Intl.DateTimeFormat('tr-TR', { dateStyle: 'medium', timeStyle: 'short' });

function formatDate(value: string | null | undefined): string {
  const date = value ? new Date(value) : null;
  return date && !Number.isNaN(date.getTime()) ? dateFormat.format(date) : '';
}

export function ResumeListPage() {
  const heading = usePageHeading<HTMLHeadingElement>(t.title);
  const [page, setPage] = useState(1);
  const list = useResumeList(page);
  const [toArchive, setToArchive] = useState<ResumeListItem | null>(null);
  const archive = useArchiveResume();
  const [notice, setNotice] = useState<{ tone: 'success' | 'error'; text: string } | null>(null);

  async function confirmArchive(item: ResumeListItem) {
    setToArchive(null);
    if (!item.id) return;
    try {
      await archive.mutateAsync(item.id);
      setNotice({ tone: 'success', text: t.archivedDone(item.title ?? '') });
    } catch (error) {
      setNotice({ tone: 'error', text: describeError(error) });
    }
  }

  const items = list.data?.items ?? [];
  const totalPages = Number(list.data?.totalPages ?? 0);

  return (
    <Card width="wide">
      <h1 ref={heading} tabIndex={-1}>
        {t.title}
      </h1>

      <CreateResumeForm />

      {notice && (
        <Alert tone={notice.tone} role={notice.tone === 'error' ? 'alert' : 'status'}>
          <p>{notice.text}</p>
        </Alert>
      )}

      {list.isPending && (
        <p role="status" className={text.muted}>
          {messages.common.loading}
        </p>
      )}
      {list.isError && (
        <Alert
          tone="error"
          role="alert"
          title={messages.common.loadFailed}
          actions={
            <Button tone="primary" onClick={() => void list.refetch()}>
              {messages.common.retry}
            </Button>
          }
        >
          <p>{describeError(list.error)}</p>
        </Alert>
      )}

      {list.isSuccess && items.length === 0 && <p className={styles.empty}>{t.empty}</p>}

      {items.length > 0 && (
        <ul className={styles.list} aria-label={t.listLabel}>
          {items.map((item) => {
            const title = item.title ?? '';
            const href = `/app/resumes/${item.id ?? ''}`;
            return (
              <li key={item.id} className={styles.item}>
                <div>
                  <h2 className={styles.itemTitle}>
                    <Link to={href}>{title}</Link>
                  </h2>
                  <p className={styles.meta}>
                    {item.professionalTitle && <span>{item.professionalTitle}</span>}
                    <span>{t.updatedAt(formatDate(item.updatedAt ?? item.createdAt))}</span>
                    {item.isMasterResume && <span className={styles.badge}>{t.master}</span>}
                    {item.isArchived && <span className={styles.badge}>{t.archived}</span>}
                  </p>
                </div>
                <div className={styles.actions}>
                  <Link to={href} className={styles.edit} aria-label={`${messages.common.edit}: ${title}`}>
                    {messages.common.edit}
                  </Link>
                  {!item.isArchived && (
                    <Button
                      tone="secondary"
                      disabled={archive.isPending}
                      aria-label={`${t.archive}: ${title}`}
                      onClick={() => {
                        setNotice(null);
                        setToArchive(item);
                      }}
                    >
                      {archive.isPending && archive.variables === item.id ? t.archiving : t.archive}
                    </Button>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}

      {totalPages > 1 && (
        <nav className={styles.pager} aria-label={t.page(page, totalPages)}>
          <Button
            tone="secondary"
            disabled={page <= 1}
            onClick={() => {
              setPage(page - 1);
            }}
          >
            {t.previous}
          </Button>
          <span>{t.page(page, totalPages)}</span>
          <Button
            tone="secondary"
            disabled={page >= totalPages}
            onClick={() => {
              setPage(page + 1);
            }}
          >
            {t.next}
          </Button>
        </nav>
      )}

      {toArchive && (
        <ConfirmDialog
          title={t.archiveTitle(toArchive.title ?? '')}
          description={t.archiveDescription}
          confirmLabel={t.archive}
          cancelLabel={messages.common.cancel}
          onCancel={() => {
            setToArchive(null);
          }}
          onConfirm={() => void confirmArchive(toArchive)}
        />
      )}
    </Card>
  );
}

function CreateResumeForm() {
  const navigate = useNavigate();
  const create = useCreateResume();
  const form = useForm<{ title: string }>({ defaultValues: { title: '' } });
  const [failure, setFailure] = useState<string | null>(null);
  const failureRef = useRef<HTMLDivElement>(null);

  const submit = async ({ title }: { title: string }) => {
    if (create.isPending) return;
    setFailure(null);
    try {
      const created = await create.mutateAsync(title.trim());
      await navigate(`/app/resumes/${created.id ?? ''}`);
    } catch (error) {
      setFailure(applyProblem(error, { fields: ['title'] }, form.setError));
      requestAnimationFrame(() => failureRef.current?.focus());
    }
  };

  return (
    <form noValidate onSubmit={(event) => void form.handleSubmit(submit)(event)} aria-label={t.createTitle}>
      {failure !== null && (
        <Alert ref={failureRef} tone="error" role="alert" title={messages.forms.errorSummary}>
          <p>{failure}</p>
        </Alert>
      )}
      <div className={styles.create}>
        <div className={styles.createField}>
          <TextField
            label={t.createLabel}
            hint={t.createHint}
            registration={form.register('title', {
              required: { value: true, message: messages.forms.required },
              maxLength: { value: 200, message: messages.forms.maxLength(200) },
              validate: (value) => value.trim() !== '' || messages.forms.required,
            })}
            error={form.formState.errors.title?.message}
            autoComplete="off"
          />
        </div>
        <Button type="submit" tone="primary" disabled={create.isPending} aria-busy={create.isPending || undefined}>
          {create.isPending ? t.creating : t.create}
        </Button>
      </div>
    </form>
  );
}
