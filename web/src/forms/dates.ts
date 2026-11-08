import { messages } from '../i18n';

/** A month as the forms hold it: two strings, empty when not given. */
export interface MonthValue {
  month: string; // '01'..'12' or ''
  year: string; // 'YYYY' or ''
}

/** "2020-03-15" → { month: '03', year: '2020' }; null → empty. */
export function toMonthValue(date: string | null | undefined): MonthValue {
  const match = /^(\d{4})-(\d{2})/.exec(date ?? '');
  return match ? { year: match[1] ?? '', month: match[2] ?? '' } : { year: '', month: '' };
}

/** The API date for a month: its first day, or null when empty. */
export function toApiDate(value: MonthValue): string | null {
  return value.month && /^\d{4}$/.test(value.year) ? `${value.year}-${value.month}-01` : null;
}

/** Checks one month field: both parts or neither, and a four-digit year from 1900. */
export function checkMonth(value: MonthValue, required: boolean): string | true {
  const empty = value.month === '' && value.year.trim() === '';
  if (empty) return required ? messages.forms.required : true;
  if (value.month === '' || value.year.trim() === '') return messages.dates.incomplete;
  const year = Number(value.year);
  if (!/^\d{4}$/.test(value.year.trim()) || year < 1900 || year > 2100) return messages.dates.yearInvalid;
  return true;
}

/** True when the end month is before the start month. */
export function endsBeforeStart(start: MonthValue, end: MonthValue): boolean {
  const from = toApiDate(start);
  const to = toApiDate(end);
  return from !== null && to !== null && to < from;
}

/** "Mar 2020 – Halen" style text for lists. */
export function formatRange(start: string | null | undefined, end: string | null | undefined, current: boolean): string {
  const part = (date: string | null | undefined) => {
    const value = toMonthValue(date);
    const month = messages.dates.months[Number(value.month) - 1];
    return value.year ? `${month ?? ''} ${value.year}`.trim() : '';
  };
  const from = part(start);
  const to = current ? messages.dates.present : part(end);
  return [from, to].filter(Boolean).join(' – ');
}
