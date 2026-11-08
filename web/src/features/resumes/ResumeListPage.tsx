import { useId, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { Link, useNavigate } from 'react-router';
import { describeError } from '../../api/describeError';
import { usePageHeading } from '../../components/usePageTitle';
import { Alert } from '../../components/ui/Alert';
import { Button } from '../../components/ui/Button';
import { ButtonLink } from '../../components/ui/ButtonLink';
import { Card } from '../../components/ui/Card';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
import text from '../../components/ui/Text.module.css';
import { cx } from '../../components/ui/cx';
import { TextField } from '../../components/ui/TextField';
import { applyProblem } from '../../forms/problems';
import { messages } from '../../i18n';
import { useSetArchived, useCreateResume, useResumeList, type ResumeListItem } from './api';
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
  const archive = useSetArchived();
  const [notice, setNotice] = useState<{ tone: 'success' | 'error'; text: string } | null>(null);

  /** Archives or unarchives with the ETag the list shows; a 412 refreshes the list. */
  async function setArchived(item: ResumeListItem, archived: boolean) {
    setToArchive(null);
    setNotice(null);
    if (!item.id) return;
    try {
      await archive.mutateAsync({ id: item.id, eTag: item.eTag ?? '', archived });
      const title = item.title ?? '';
      setNotice({ tone: 'success', text: archived ? t.archivedDone(title) : t.unarchivedDone(title) });
    } catch (error) {
      setNotice({ tone: 'error', text: describeError(error) });
    }
  }

  const items = list.data?.items ?? [];
  // Archived resumes go under their own heading, below the active ones.
  const active = items.filter((item) => !item.isArchived);
  const archived = items.filter((item) => item.isArchived);
  const archiveHeadingId = useId();
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
      {list.isSuccess && items.length > 0 && active.length === 0 && <p className={styles.empty}>{t.noActive}</p>}

      {active.length > 0 && (
        <ResumeCards
          items={active}
          label={t.listLabel}
          headingLevel={2}
          busy={archive.isPending ? archive.variables.id : null}
          onArchive={(item) => {
            setNotice(null);
            setToArchive(item);
          }}
        />
      )}

      {archived.length > 0 && (
        <section className={styles.archive} aria-labelledby={archiveHeadingId}>
          <h2 id={archiveHeadingId} className={styles.archiveHeading}>
            {t.archiveSection}
          </h2>
          <ResumeCards
            items={archived}
            label={t.archivedListLabel}
            headingLevel={3}
            busy={archive.isPending ? archive.variables.id : null}
            onUnarchive={(item) => void setArchived(item, false)}
          />
        </section>
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
          onConfirm={() => void setArchived(toArchive, true)}
        />
      )}
    </Card>
  );
}

interface ResumeCardsProps {
  items: ResumeListItem[];
  label: string;
  headingLevel: 2 | 3;
  /** The id being archived or unarchived, which shows its progress. */
  busy: string | null;
  /** For active resumes. */
  onArchive?: (item: ResumeListItem) => void;
  /** For archived resumes. */
  onUnarchive?: (item: ResumeListItem) => void;
}

/** Resume cards. Actions sit in fixed columns so they line up from card to card. */
function ResumeCards({ items, label, headingLevel, busy, onArchive, onUnarchive }: ResumeCardsProps) {
  const Heading = headingLevel === 2 ? 'h2' : 'h3';
  return (
    <ul className={styles.list} aria-label={label}>
      {items.map((item) => {
        const title = item.title ?? '';
        const href = `/app/resumes/${encodeURIComponent(item.id ?? '')}`;
        return (
          <li key={item.id} className={cx(styles.item, item.isArchived && styles.archived)}>
            <div>
              <Heading className={styles.itemTitle}>
                <Link to={href}>{title}</Link>
              </Heading>
              <p className={styles.meta}>
                {item.professionalTitle && <span>{item.professionalTitle}</span>}
                <span>{t.updatedAt(formatDate(item.updatedAt ?? item.createdAt))}</span>
                {item.isMasterResume && <span className={styles.badge}>{t.master}</span>}
                {item.isArchived && <span className={styles.badge}>{t.archived}</span>}
              </p>
            </div>
            <div className={styles.actions}>
              <ButtonLink to={href} aria-label={`${messages.common.edit}: ${title}`}>
                {messages.common.edit}
              </ButtonLink>
              {onArchive && (
                <Button
                  tone="secondary"
                  disabled={busy !== null}
                  aria-label={`${t.archive}: ${title}`}
                  onClick={() => {
                    onArchive(item);
                  }}
                >
                  {busy === item.id ? t.archiving : t.archive}
                </Button>
              )}
              {onUnarchive && (
                <Button
                  tone="secondary"
                  disabled={busy !== null}
                  aria-label={`${t.unarchive}: ${title}`}
                  onClick={() => {
                    onUnarchive(item);
                  }}
                >
                  {busy === item.id ? t.unarchiving : t.unarchive}
                </Button>
              )}
            </div>
          </li>
        );
      })}
    </ul>
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
