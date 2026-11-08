import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { ApiError } from '../../../api/http';
import { useSession } from '../../../auth/AuthContext';
import { Alert } from '../../../components/ui/Alert';
import { Button } from '../../../components/ui/Button';
import { CheckboxField, MonthYearField } from '../../../components/ui/ChoiceFields';
import text from '../../../components/ui/Text.module.css';
import { TextField } from '../../../components/ui/TextField';
import { checkMonth, endsBeforeStart, formatRange, toApiDate, toMonthValue } from '../../../forms/dates';
import { applyProblem, type ProblemMapping } from '../../../forms/problems';
import { messages } from '../../../i18n';
import { fetchEditor, resumeKeys, type ResumeEditor } from '../api';
import { SaveBar } from '../editor/EditorSection';
import { ItemListSection, type ItemFormArgs } from '../editor/ItemListSection';
import { bySortOrder, nextSortOrder, sectionLimits, useSectionApis, type Education, type EducationRequest } from '../sectionsApi';

const t = messages.education;

interface EducationValues {
  institutionName: string;
  degree: string;
  fieldOfStudy: string;
  location: string;
  startMonth: string;
  startYear: string;
  endMonth: string;
  endYear: string;
  /** Not an API field: "ongoing" is sent as no end date. */
  isCurrent: boolean;
  grade: string;
  gradeScale: string;
  description: string;
}

const mapping: ProblemMapping<EducationValues> = {
  fields: ['institutionName', 'degree', 'fieldOfStudy', 'location', 'grade', 'gradeScale', 'description'],
};

const label = (item: Education) => [item.institutionName, item.degree].filter(Boolean).join(' – ');
/** Ongoing when it has started and has no end. The API has no field for it. */
const isOngoing = (item: Education | null) => Boolean(item?.startDate) && !item?.endDate;

function toValues(item: Education | null): EducationValues {
  const start = toMonthValue(item?.startDate);
  const end = toMonthValue(item?.endDate);
  return {
    institutionName: item?.institutionName ?? '',
    degree: item?.degree ?? '',
    fieldOfStudy: item?.fieldOfStudy ?? '',
    location: item?.location ?? '',
    startMonth: start.month,
    startYear: start.year,
    endMonth: end.month,
    endYear: end.year,
    isCurrent: isOngoing(item),
    grade: item?.grade === null || item?.grade === undefined ? '' : String(item.grade).replace('.', ','),
    gradeScale: item?.gradeScale ?? '',
    description: item?.description ?? '',
  };
}

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());
/** "3,45" or "3.45" → 3.45; empty → null; anything else → NaN. */
const parseGrade = (value: string) => (value.trim() === '' ? null : Number(value.trim().replace(',', '.')));

export function educationRequest(item: Education, sortOrder = Number(item.sortOrder ?? 0)): EducationRequest {
  return {
    institutionName: item.institutionName ?? '',
    degree: item.degree ?? null,
    fieldOfStudy: item.fieldOfStudy ?? null,
    startDate: item.startDate ?? null,
    endDate: item.endDate ?? null,
    location: item.location ?? null,
    grade: item.grade ?? null,
    gradeScale: item.gradeScale ?? null,
    description: item.description ?? null,
    sortOrder,
  };
}

export function EducationSummary({ resume }: { resume: ResumeEditor }) {
  const items = bySortOrder(resume.educations);
  if (items.length === 0) return <p className={text.muted}>{t.empty}</p>;
  return (
    <ul>
      {items.map((item) => (
        <li key={item.id}>
          {label(item)} <span className={text.muted}>{formatRange(item.startDate, item.endDate, isOngoing(item))}</span>
        </li>
      ))}
    </ul>
  );
}

interface SectionFormProps {
  resume: ResumeEditor;
  onDirtyChange: (dirty: boolean) => void;
  onClose: () => void;
}

export function EducationSection({ resume, onDirtyChange }: SectionFormProps) {
  const id = resume.id ?? '';
  const apis = useSectionApis(id);
  const session = useSession();
  const client = useQueryClient();

  return (
    <ItemListSection
      items={bySortOrder(resume.educations)}
      getId={(item) => item.id ?? ''}
      label={label}
      noun={t.one}
      emptyText={t.empty}
      limit={sectionLimits.educations}
      onDirtyChange={onDirtyChange}
      onDelete={(item) => apis.education.remove(item.id ?? '')}
      onSortOrderChange={async (item, sortOrder) => {
        await apis.education.update(item.id ?? '', educationRequest(item, sortOrder));
      }}
      onStale={() => client.query({ queryKey: resumeKeys.editor(id), queryFn: () => fetchEditor(session, id), staleTime: 0 })}
      renderItem={(item) => (
        <>
          <strong>{label(item)}</strong>
          <div className={text.muted}>
            {[item.fieldOfStudy, formatRange(item.startDate, item.endDate, isOngoing(item))].filter(Boolean).join(' · ')}
          </div>
        </>
      )}
      renderForm={(args) => <EducationForm resume={resume} {...args} />}
    />
  );
}

function EducationForm({ resume, item, onDirtyChange, onSaved, onCancel }: ItemFormArgs<Education> & { resume: ResumeEditor }) {
  const apis = useSectionApis(resume.id ?? '');
  const form = useForm<EducationValues>({ defaultValues: toValues(item) });
  const { errors, isDirty, isSubmitting } = form.formState;
  const isCurrent = useWatch({ control: form.control, name: 'isCurrent' });
  const [failure, setFailure] = useState<string | null>(null);
  const failureRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  const submit = async (values: EducationValues) => {
    setFailure(null);
    const start = { month: values.startMonth, year: values.startYear.trim() };
    const end = { month: values.endMonth, year: values.endYear.trim() };
    if (!values.isCurrent && endsBeforeStart(start, end)) {
      form.setError('endYear', { type: 'order', message: messages.dates.endBeforeStart }, { shouldFocus: true });
      return;
    }
    const body: EducationRequest = {
      institutionName: values.institutionName.trim(),
      degree: orNull(values.degree),
      fieldOfStudy: orNull(values.fieldOfStudy),
      location: orNull(values.location),
      startDate: toApiDate(start),
      endDate: values.isCurrent ? null : toApiDate(end),
      grade: parseGrade(values.grade),
      gradeScale: orNull(values.gradeScale),
      description: orNull(values.description),
      sortOrder: item ? Number(item.sortOrder ?? 0) : nextSortOrder(resume.educations),
    };
    try {
      const saved = item ? await apis.education.update(item.id ?? '', body) : await apis.education.create(body);
      onDirtyChange(false);
      onSaved(label(saved));
    } catch (error) {
      setFailure(applyProblem(error, mapping, form.setError));
      const keys = Object.keys((error instanceof ApiError ? error.problem?.errors : undefined) ?? {}).map((key) =>
        key.replace(/^\$\./, '').toLowerCase(),
      );
      if (keys.includes('enddate')) {
        form.setError('endYear', {
          type: 'server',
          message: keys.includes('startdate') ? messages.dates.endBeforeStart : messages.forms.fieldInvalid,
        });
      } else if (keys.includes('startdate')) {
        form.setError('startYear', { type: 'server', message: messages.forms.fieldInvalid });
      }
      requestAnimationFrame(() => failureRef.current?.focus());
    }
  };

  const required = { value: true, message: messages.forms.required };
  const max = (length: number) => ({ value: length, message: messages.forms.maxLength(length) });
  const monthCheck = (name: 'start' | 'end') => () =>
    checkMonth({ month: form.getValues(`${name}Month`), year: form.getValues(`${name}Year`) }, false);

  return (
    <form noValidate onSubmit={(event) => void form.handleSubmit(submit)(event)}>
      {failure !== null && (
        <Alert ref={failureRef} tone="error" role="alert" title={messages.forms.errorSummary}>
          <p>{failure}</p>
        </Alert>
      )}
      <TextField
        label={t.institutionName}
        registration={form.register('institutionName', {
          required,
          maxLength: max(250),
          validate: (value) => value.trim() !== '' || messages.forms.required,
        })}
        error={errors.institutionName?.message}
        autoComplete="organization"
      />
      <TextField label={t.degree} registration={form.register('degree', { maxLength: max(200) })} error={errors.degree?.message} />
      <TextField
        label={t.fieldOfStudy}
        registration={form.register('fieldOfStudy', { maxLength: max(200) })}
        error={errors.fieldOfStudy?.message}
      />
      <TextField label={t.location} registration={form.register('location', { maxLength: max(200) })} error={errors.location?.message} />
      <MonthYearField
        legend={t.startDate}
        month={form.register('startMonth')}
        year={form.register('startYear', { validate: monthCheck('start') })}
        error={errors.startYear?.message}
      />
      <CheckboxField
        label={t.isCurrent}
        registration={form.register('isCurrent', {
          // Ongoing has no end: an end typed before is dropped, not kept hidden.
          onChange: (event: { target: { checked: boolean } }) => {
            if (!event.target.checked) return;
            form.setValue('endMonth', '', { shouldDirty: true });
            form.setValue('endYear', '', { shouldDirty: true });
            form.clearErrors('endYear');
          },
        })}
      />
      {!isCurrent && (
        <MonthYearField
          legend={t.endDate}
          month={form.register('endMonth')}
          year={form.register('endYear', { validate: (_value, all) => (all.isCurrent ? true : monthCheck('end')()) })}
          error={errors.endYear?.message}
        />
      )}
      <TextField
        label={t.grade}
        hint={t.gradeHint}
        inputMode="decimal"
        registration={form.register('grade', {
          validate: (value) => {
            const grade = parseGrade(value);
            return grade === null || (Number.isFinite(grade) && grade >= 0) || t.gradeInvalid;
          },
        })}
        error={errors.grade?.message}
      />
      <TextField label={t.gradeScale} registration={form.register('gradeScale', { maxLength: max(20) })} error={errors.gradeScale?.message} />
      <TextField
        multiline
        label={t.description}
        registration={form.register('description', { maxLength: max(3000) })}
        error={errors.description?.message}
        rows={4}
      />
      <SaveBar>
        <Button type="submit" tone="primary" disabled={isSubmitting} aria-busy={isSubmitting || undefined}>
          {isSubmitting ? messages.common.saving : messages.common.save}
        </Button>
        <Button tone="secondary" onClick={onCancel}>
          {messages.common.cancel}
        </Button>
      </SaveBar>
    </form>
  );
}
