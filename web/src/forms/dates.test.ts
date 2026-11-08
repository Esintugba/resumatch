import { describe, expect, it } from 'vitest';
import { messages } from '../i18n';
import { checkMonth, endsBeforeStart, formatRange, toApiDate, toMonthValue } from './dates';

describe('month fields', () => {
  it('reads and writes API dates as months', () => {
    expect(toMonthValue('2020-03-15')).toEqual({ year: '2020', month: '03' });
    expect(toMonthValue(null)).toEqual({ year: '', month: '' });
    expect(toApiDate({ year: '2020', month: '03' })).toBe('2020-03-01');
    expect(toApiDate({ year: '', month: '03' })).toBeNull();
  });

  it.each([
    [{ month: '', year: '' }, false, true],
    [{ month: '', year: '' }, true, messages.forms.required],
    [{ month: '03', year: '' }, false, messages.dates.incomplete],
    [{ month: '', year: '2020' }, false, messages.dates.incomplete],
    [{ month: '03', year: '20' }, false, messages.dates.yearInvalid],
    [{ month: '03', year: '1850' }, false, messages.dates.yearInvalid],
    [{ month: '03', year: '2020' }, true, true],
  ])('checks %j (required: %s)', (value, required, expected) => {
    expect(checkMonth(value, required)).toBe(expected);
  });

  it('finds an end before the start, ignoring empty or partial dates', () => {
    expect(endsBeforeStart({ year: '2020', month: '05' }, { year: '2020', month: '04' })).toBe(true);
    expect(endsBeforeStart({ year: '2020', month: '05' }, { year: '2020', month: '05' })).toBe(false);
    expect(endsBeforeStart({ year: '2020', month: '05' }, { year: '', month: '' })).toBe(false);
  });

  it('formats ranges in Turkish', () => {
    expect(formatRange('2020-03-01', '2022-11-01', false)).toBe('Mart 2020 – Kasım 2022');
    expect(formatRange('2020-03-01', null, true)).toBe(`Mart 2020 – ${messages.dates.present}`);
    expect(formatRange(null, null, false)).toBe('');
  });
});
