import { useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { ApiError } from '../../../api/http';
import { useSession } from '../../../auth/AuthContext';
import { Alert } from '../../../components/ui/Alert';
import { Button } from '../../../components/ui/Button';
import { CheckboxField, MonthYearField, SelectField } from '../../../components/ui/ChoiceFields';
import text from '../../../components/ui/Text.module.css';
import { TextField } from '../../../components/ui/TextField';
import { checkMonth, endsBeforeStart, formatRange, toApiDate, toMonthValue } from '../../../forms/dates';
import { applyProblem, type ProblemMapping } from '../../../forms/problems';
import { messages } from '../../../i18n';
import { fetchEditor, resumeKeys, type ResumeEditor } from '../api';
import { ConflictAlert } from '../editor/ConflictAlert';
import { SaveBar } from '../editor/EditorSection';
import { ItemListSection, type ItemFormArgs } from '../editor/ItemListSection';
import { bySortOrder, nextSortOrder, sectionLimits, useSectionApis, type Experience, type ExperienceRequest } from '../sectionsApi';

const t = messages.experience;
type EmploymentType = keyof typeof t.employmentTypes;
const employmentTypes = Object.keys(t.employmentTypes) as EmploymentType[];

interface ExperienceValues {
  companyName: string;
  position: string;
  employmentType: EmploymentType | '';
  location: string;
  startMonth: string;
  startYear: string;
  endMonth: string;
  endYear: string;
  isCurrent: boolean;
  description: string;
}

const mapping: ProblemMapping<ExperienceValues> = {
  fields: ['companyName', 'position', 'employmentType', 'location', 'description'],
};

const label = (item: Experience) => [item.position, item.companyName].filter(Boolean).join(' – ');

function toValues(item: Experience | null): ExperienceValues {
  const start = toMonthValue(item?.startDate);
  const end = toMonthValue(item?.endDate);
  return {
    companyName: item?.companyName ?? '',
    position: item?.position ?? '',
    employmentType: item?.employmentType ?? '',
    location: item?.location ?? '',
    startMonth: start.month,
    startYear: start.year,
    endMonth: end.month,
    endYear: end.year,
    isCurrent: item?.isCurrent ?? false,
    description: item?.description ?? '',
  };
}

const orNull = (value: string) => (value.trim() === '' ? null : value.trim());

/** The full PUT body for an item as it is, used when only its position changes. */
export function experienceRequest(item: Experience, sortOrder = Number(item.sortOrder ?? 0)): ExperienceRequest {
  return {
    companyName: item.companyName ?? '',
    position: item.position ?? '',
    location: item.location ?? null,
    employmentType: item.employmentType ?? null,
    startDate: item.startDate ?? null,
    endDate: item.isCurrent ? null : (item.endDate ?? null),
    isCurrent: item.isCurrent ?? false,
    description: item.description ?? null,
    sortOrder,
    // PUT replaces the bullets, so the existing ones go back unchanged.
    bullets: (item.bullets ?? []).map((bullet) => ({
      id: bullet.id ?? null,
      content: bullet.content ?? '',
      sortOrder: Number(bullet.sortOrder ?? 0),
    })),
  };
}

export function ExperienceSummary({ resume }: { resume: ResumeEditor }) {
  const items = bySortOrder(resume.experiences);
  if (items.length === 0) return <p className={text.muted}>{t.empty}</p>;
  return (
    <ul>
      {items.map((item) => (
        <li key={item.id}>
          {label(item)} <span className={text.muted}>{formatRange(item.startDate, item.endDate, item.isCurrent ?? false)}</span>
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

export function ExperienceSection({ resume, onDirtyChange }: SectionFormProps) {
  const id = resume.id ?? '';
  const apis = useSectionApis(id);
  const session = useSession();
  const client = useQueryClient();
  const items = bySortOrder(resume.experiences);

  return (
    <ItemListSection
      items={items}
      getId={(item) => item.id ?? ''}
      label={label}
      noun={t.one}
      emptyText={t.empty}
      limit={sectionLimits.experiences}
      onDirtyChange={onDirtyChange}
      onDelete={(item) => apis.experience.remove(item.id ?? '')}
      onSortOrderChange={async (item, sortOrder) => {
        await apis.experience.update(item.id ?? '', experienceRequest(item, sortOrder), item.eTag ?? '');
      }}
      onStale={() => client.query({ queryKey: resumeKeys.editor(id), queryFn: () => fetchEditor(session, id), staleTime: 0 })}
      renderItem={(item) => (
        <>
          <strong>{label(item)}</strong>
          <div className={text.muted}>
            {[formatRange(item.startDate, item.endDate, item.isCurrent ?? false), item.location].filter(Boolean).join(' · ')}
          </div>
        </>
      )}
      renderForm={(args) => <ExperienceForm resume={resume} {...args} />}
    />
  );
}

function ExperienceForm({ resume, item, onDirtyChange, onSaved, onCancel }: ItemFormArgs<Experience> & { resume: ResumeEditor }) {
  const id = resume.id ?? '';
  const apis = useSectionApis(id);
  const session = useSession();
  const client = useQueryClient();
  const [current, setCurrent] = useState<Experience | null>(item);
  const form = useForm<ExperienceValues>({ defaultValues: toValues(item) });
  const { errors, isDirty, isSubmitting } = form.formState;
  const isCurrent = useWatch({ control: form.control, name: 'isCurrent' });
  const [conflict, setConflict] = useState(false);
  const [reloading, setReloading] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);
  const failureRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    onDirtyChange(isDirty);
  }, [isDirty, onDirtyChange]);

  const submit = async (values: ExperienceValues) => {
    if (conflict) return;
    setFailure(null);
    const start = { month: values.startMonth, year: values.startYear.trim() };
    const end = { month: values.endMonth, year: values.endYear.trim() };
    if (!values.isCurrent && endsBeforeStart(start, end)) {
      form.setError('endYear', { type: 'order', message: messages.dates.endBeforeStart }, { shouldFocus: true });
      return;
    }
    const body: ExperienceRequest = {
      ...(current ? experienceRequest(current) : { bullets: [], sortOrder: nextSortOrder(resume.experiences) }),
      companyName: values.companyName.trim(),
      position: values.position.trim(),
      employmentType: values.employmentType === '' ? null : values.employmentType,
      location: orNull(values.location),
      startDate: toApiDate(start),
      endDate: values.isCurrent ? null : toApiDate(end),
      isCurrent: values.isCurrent,
      description: orNull(values.description),
    };
    try {
      const saved = current
        ? await apis.experience.update(current.id ?? '', body, current.eTag ?? '')
        : await apis.experience.create({ ...body, bullets: [] });
      onDirtyChange(false);
      onSaved(label(saved));
    } catch (error) {
      if (error instanceof ApiError && error.status === 412) {
        setConflict(true);
        return;
      }
      setFailure(applyProblem(error, mapping, form.setError));
      // The API reports the date order on StartDate and EndDate together.
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

  async function reload() {
    setReloading(true);
    try {
      const fresh = await client.query({ queryKey: resumeKeys.editor(id), queryFn: () => fetchEditor(session, id), staleTime: 0 });
      const latest = (fresh.experiences ?? []).find((entry) => entry.id === current?.id) ?? null;
      setCurrent(latest);
      form.reset(toValues(latest));
      setConflict(false);
      if (!latest) setFailure(messages.errors.codes.not_found);
    } catch (error) {
      setFailure(applyProblem(error, mapping, form.setError));
    } finally {
      setReloading(false);
    }
  }

  const required = { value: true, message: messages.forms.required };
  const max = (length: number) => ({ value: length, message: messages.forms.maxLength(length) });
  const notBlank = (value: string) => value.trim() !== '' || messages.forms.required;
  const monthCheck = (name: 'start' | 'end', isRequired: boolean) => () =>
    checkMonth({ month: form.getValues(`${name}Month`), year: form.getValues(`${name}Year`) }, isRequired);
  const bullets = current?.bullets?.length ?? 0;

  return (
    <form noValidate onSubmit={(event) => void form.handleSubmit(submit)(event)}>
      {conflict && <ConflictAlert reloading={reloading} onReload={() => void reload()} />}
      {failure !== null && (
        <Alert ref={failureRef} tone="error" role="alert" title={messages.forms.errorSummary}>
          <p>{failure}</p>
        </Alert>
      )}
      <TextField
        label={t.position}
        registration={form.register('position', { required, maxLength: max(200), validate: notBlank })}
        error={errors.position?.message}
        autoComplete="organization-title"
      />
      <TextField
        label={t.companyName}
        registration={form.register('companyName', { required, maxLength: max(200), validate: notBlank })}
        error={errors.companyName?.message}
        autoComplete="organization"
      />
      <SelectField
        label={t.employmentType}
        registration={form.register('employmentType', { required })}
        error={errors.employmentType?.message}
      >
        <option value="">{t.chooseEmploymentType}</option>
        {employmentTypes.map((type) => (
          <option key={type} value={type}>
            {t.employmentTypes[type]}
          </option>
        ))}
      </SelectField>
      <TextField label={t.location} registration={form.register('location', { maxLength: max(200) })} error={errors.location?.message} />
      <MonthYearField
        legend={t.startDate}
        month={form.register('startMonth')}
        year={form.register('startYear', { validate: monthCheck('start', true) })}
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
          year={form.register('endYear', { validate: (_value, all) => (all.isCurrent ? true : monthCheck('end', false)()) })}
          error={errors.endYear?.message}
        />
      )}
      <TextField
        multiline
        label={t.description}
        registration={form.register('description', { maxLength: max(5000) })}
        error={errors.description?.message}
        rows={4}
      />
      {bullets > 0 && <p className={text.muted}>{t.bulletsKept(bullets)}</p>}
      <SaveBar>
        <Button type="submit" tone="primary" disabled={isSubmitting || conflict} aria-busy={isSubmitting || undefined}>
          {isSubmitting ? messages.common.saving : messages.common.save}
        </Button>
        <Button tone="secondary" onClick={onCancel}>
          {messages.common.cancel}
        </Button>
      </SaveBar>
    </form>
  );
}
