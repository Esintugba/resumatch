import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../api/http';
import { messages } from '../i18n';
import { applyProblem, matchField } from './problems';

interface Values {
  title: string;
  summary: string;
  items: string;
}

describe('matchField', () => {
  it.each([
    ['Title', 'title'],
    ['$.title', 'title'],
    ['request.Summary', 'summary'],
    ['Items[0].Name', 'items'],
    ['Unknown', null],
  ])('maps %s to %s', (key, expected) => {
    expect(matchField<Values>(key, ['title', 'summary', 'items'])).toBe(expected);
  });
});

describe('applyProblem', () => {
  it('puts field errors and mapped codes on their fields in Turkish and returns the summary', () => {
    const setError = vi.fn();
    const validation = new ApiError(400, {
      status: 400,
      code: 'validation_failed',
      errors: { Title: ['The supplied value is invalid.'], Other: ['x'] },
    });

    const summary = applyProblem<Values>(validation, { fields: ['title', 'summary', 'items'] }, setError);

    expect(summary).toBe(messages.errors.codes.validation_failed);
    expect(setError).toHaveBeenCalledTimes(1);
    expect(setError).toHaveBeenCalledWith(
      'title',
      { type: 'server', message: messages.forms.fieldInvalid },
      { shouldFocus: true },
    );

    const limit = new ApiError(409, { status: 409, code: 'section_limit_reached' });
    setError.mockClear();
    applyProblem<Values>(limit, { fields: ['items'], codes: { section_limit_reached: 'items' } }, setError);
    expect(setError).toHaveBeenCalledWith(
      'items',
      { type: 'server', message: messages.errors.codes.section_limit_reached },
      { shouldFocus: true },
    );
  });

  it('returns the connection message for a network error', () => {
    expect(applyProblem<Values>(new ApiError(null, null), { fields: [] }, vi.fn())).toBe(messages.errors.network);
  });
});
