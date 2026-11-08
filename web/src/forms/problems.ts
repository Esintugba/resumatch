import type { FieldValues, Path, UseFormSetError } from 'react-hook-form';
import { describeError } from '../api/describeError';
import { ApiError } from '../api/http';
import type { ApiErrorCode } from '../api/problem';
import { messages } from '../i18n';

export interface ProblemMapping<T extends FieldValues> {
  /** The form's fields; API field names are matched to them without regard to case or a "$." prefix. */
  fields: readonly Path<T>[];
  /** Codes that belong to one field, such as section_limit_reached on the add button's field. */
  codes?: Partial<Record<ApiErrorCode, Path<T>>>;
}

/** The form field an API error key refers to, if any. */
export function matchField<T extends FieldValues>(key: string, fields: readonly Path<T>[]): Path<T> | null {
  const name = key.replace(/^\$\./, '').replace(/^request\./i, '').split('.')[0]?.split('[')[0] ?? '';
  return fields.find((field) => field.toLowerCase() === name.toLowerCase()) ?? null;
}

/**
 * Puts an API error on the form: 400 field errors on their fields, mapped
 * codes on their field, everything else in the returned summary text. The
 * API's own field messages are English, so the fields get Turkish text.
 */
export function applyProblem<T extends FieldValues>(
  error: unknown,
  mapping: ProblemMapping<T>,
  setError: UseFormSetError<T>,
): string {
  const problem = error instanceof ApiError ? error.problem : null;
  let focused = false;
  const mark = (field: Path<T>, message: string) => {
    setError(field, { type: 'server', message }, { shouldFocus: !focused });
    focused = true;
  };

  for (const key of Object.keys(problem?.errors ?? {})) {
    const field = matchField(key, mapping.fields);
    if (field) mark(field, messages.forms.fieldInvalid);
  }

  const code = problem?.code;
  const codeField = code ? mapping.codes?.[code] : undefined;
  if (code && codeField) mark(codeField, messages.errors.codes[code]);

  return describeError(error);
}
