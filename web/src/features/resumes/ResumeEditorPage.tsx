import { useCallback, useState, type ComponentType } from 'react';
import { Link, useParams } from 'react-router';
import { describeError } from '../../api/describeError';
import { ApiError } from '../../api/http';
import { useReportUnsavedChanges } from '../../components/unsavedChanges';
import { usePageHeading } from '../../components/usePageTitle';
import { Alert } from '../../components/ui/Alert';
import { Button } from '../../components/ui/Button';
import { Card } from '../../components/ui/Card';
import { ConfirmDialog } from '../../components/ui/ConfirmDialog';
import text from '../../components/ui/Text.module.css';
import { messages } from '../../i18n';
import { useResumeEditor, type ResumeEditor } from './api';
import { EditorSection } from './editor/EditorSection';
import { useUnsavedChangesGuard } from './editor/useUnsavedChangesGuard';
import styles from './ResumeEditorPage.module.css';
import { ContactForm, ContactSummary } from './sections/ContactSection';
import { EducationSection, EducationSummary } from './sections/EducationSection';
import { ExperienceSection, ExperienceSummary } from './sections/ExperienceSection';
import { MainInfoForm, MainInfoSummary } from './sections/MainInfoSection';
import { SkillsSection, SkillsSummary } from './sections/SkillsSection';

export interface SectionFormProps {
  resume: ResumeEditor;
  onDirtyChange: (dirty: boolean) => void;
  onClose: () => void;
}

interface SectionDefinition {
  id: string;
  title: string;
  Summary: ComponentType<{ resume: ResumeEditor }>;
  Form: ComponentType<SectionFormProps>;
}

/** The editor's sections, in order. Further sections (projects, certificates, languages, links) go here. */
const sections: SectionDefinition[] = [
  { id: 'main', title: messages.editor.main.title, Summary: MainInfoSummary, Form: MainInfoForm },
  { id: 'contact', title: messages.editor.contact.title, Summary: ContactSummary, Form: ContactForm },
  { id: 'experience', title: messages.experience.title, Summary: ExperienceSummary, Form: ExperienceSection },
  { id: 'education', title: messages.education.title, Summary: EducationSummary, Form: EducationSection },
  { id: 'skills', title: messages.skills.title, Summary: SkillsSummary, Form: SkillsSection },
];

export function ResumeEditorPage() {
  const { resumeId = '' } = useParams();
  const query = useResumeEditor(resumeId);
  const heading = usePageHeading<HTMLHeadingElement>(query.data?.title ?? messages.common.loading);

  const [open, setOpen] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  // The section a dirty form is waiting to give way to; null means "close".
  const [pendingOpen, setPendingOpen] = useState<string | null | undefined>(undefined);
  const blocker = useUnsavedChangesGuard(dirty);
  useReportUnsavedChanges(dirty);
  const onDirtyChange = useCallback((value: boolean) => {
    setDirty(value);
  }, []);

  function switchTo(next: string | null) {
    setDirty(false);
    setOpen(next);
  }

  function request(next: string | null) {
    if (next === open) return;
    if (dirty) setPendingOpen(next);
    else switchTo(next);
  }

  if (query.isPending) {
    return (
      <Card width="wide">
        <p role="status" className={text.muted}>
          {messages.common.loading}
        </p>
      </Card>
    );
  }

  if (query.isError) {
    const notFound = query.error instanceof ApiError && query.error.status === 404;
    return (
      <Card width="wide">
        <Alert
          tone="error"
          role="alert"
          title={notFound ? messages.editor.notFound : messages.common.loadFailed}
          actions={
            notFound ? undefined : (
              <Button tone="primary" onClick={() => void query.refetch()}>
                {messages.common.retry}
              </Button>
            )
          }
        >
          {!notFound && <p>{describeError(query.error)}</p>}
        </Alert>
        <Link to="/app/resumes">{messages.editor.back}</Link>
      </Card>
    );
  }

  const resume = query.data;
  const askingToLeave = blocker.state === 'blocked' || pendingOpen !== undefined;

  return (
    <Card width="wide">
      <Link to="/app/resumes" className={styles.back}>
        ← {messages.editor.back}
      </Link>
      <h1 ref={heading} tabIndex={-1} className={styles.title}>
        {resume.title}
      </h1>
      {resume.isArchived && (
        <Alert tone="warning" role="status">
          <p>{messages.editor.archivedBanner}</p>
        </Alert>
      )}

      <div className={styles.sections} role="group" aria-label={messages.editor.sectionsLabel}>
        {sections.map(({ id, title, Summary, Form }) => (
          <EditorSection
            key={id}
            title={title}
            open={open === id}
            dirty={dirty}
            onToggle={() => {
              request(open === id ? null : id);
            }}
            summary={<Summary resume={resume} />}
          >
            <Form
              resume={resume}
              onDirtyChange={onDirtyChange}
              onClose={() => {
                request(null);
              }}
            />
          </EditorSection>
        ))}
      </div>

      {askingToLeave && (
        <ConfirmDialog
          title={messages.unsaved.title}
          description={messages.unsaved.description}
          confirmLabel={messages.unsaved.discard}
          cancelLabel={messages.unsaved.stay}
          onCancel={() => {
            if (blocker.state === 'blocked') blocker.reset();
            setPendingOpen(undefined);
          }}
          onConfirm={() => {
            setDirty(false);
            if (blocker.state === 'blocked') {
              blocker.proceed();
            } else if (pendingOpen !== undefined) {
              switchTo(pendingOpen);
            }
            setPendingOpen(undefined);
          }}
        />
      )}
    </Card>
  );
}
