import { useEffect, useId, useRef, useState, type SubmitEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router';
import type { ApiProblem } from '../../api/problem';
import { useSession, useSessionState } from '../../auth/AuthContext';
import { safeReturnPath } from '../../auth/RequireAuth';
import { usePageHeading } from '../../components/usePageTitle';
import { messages } from '../../i18n';

type Field = 'email' | 'password';
type FieldErrors = Partial<Record<Field, string>>;

const t = messages.login;

export function LoginPage() {
  const session = useSession();
  const state = useSessionState();
  const navigate = useNavigate();
  const location = useLocation();
  const returnTo = safeReturnPath((location.state as { from?: unknown } | null)?.from);
  const heading = usePageHeading<HTMLHeadingElement>(t.title);
  const ids = { title: useId(), email: useId(), password: useId(), emailError: useId(), passwordError: useId() };

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [lockedUntil, setLockedUntil] = useState<number | null>(null);
  const [lockSeconds, setLockSeconds] = useState(0);
  const [now, setNow] = useState(() => Date.now());
  const submitting = useRef(false);
  const summary = useRef<HTMLDivElement>(null);

  // Ticks once a second while a 429 wait is running.
  useEffect(() => {
    if (lockedUntil === null) return;
    const timer = setInterval(() => {
      const current = Date.now();
      setNow(current);
      if (current >= lockedUntil) clearInterval(timer);
    }, 1000);
    return () => {
      clearInterval(timer);
    };
  }, [lockedUntil]);

  const secondsLeft = lockedUntil === null ? 0 : Math.max(0, Math.ceil((lockedUntil - now) / 1000));
  const locked = secondsLeft > 0;

  if (state.status === 'signed-in') return <Navigate to={returnTo} replace />;

  async function onSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault();
    // A ref, not state: a second click in the same tick must not send twice.
    if (submitting.current || locked) return;

    const errors = validate(email, password);
    setFieldErrors(errors);
    if (Object.keys(errors).length > 0) {
      setFormError(null);
      focusFirstInvalid(errors);
      return;
    }

    submitting.current = true;
    setPending(true);
    setFormError(null);
    setLockedUntil(null);
    try {
      const result = await session.login(email.trim(), password);
      if (result.ok) {
        await navigate(returnTo, { replace: true });
        return;
      }
      if (result.retryAfterSeconds !== null) {
        const until = Date.now() + result.retryAfterSeconds * 1000;
        setNow(Date.now());
        setLockedUntil(until);
        setLockSeconds(result.retryAfterSeconds);
      }
      setFieldErrors(fieldErrorsFrom(result.problem));
      // A 429 is shown as the live countdown below instead.
      setFormError(result.retryAfterSeconds !== null ? null : describe(result.problem));
      setPassword('');
      requestAnimationFrame(() => summary.current?.focus());
    } finally {
      submitting.current = false;
      setPending(false);
    }
  }

  function focusFirstInvalid(errors: FieldErrors) {
    const first = errors.email ? ids.email : errors.password ? ids.password : null;
    if (first) document.getElementById(first)?.focus();
  }

  // Announced once when the wait starts and once when it ends; the countdown itself is on the button.
  const lockMessage = locked ? t.rateLimited(lockSeconds) : lockedUntil !== null ? t.rateLimitOver : null;

  return (
    <section className="card" aria-labelledby={ids.title}>
      <h1 id={ids.title} ref={heading} tabIndex={-1}>
        {t.title}
      </h1>
      <p className="muted">{t.intro}</p>

      {(formError !== null || lockMessage !== null) && (
        <div ref={summary} className="alert" role="alert" tabIndex={-1}>
          <p className="alert-title">{t.errorSummary}</p>
          {formError !== null && <p>{formError}</p>}
          {lockMessage !== null && <p>{lockMessage}</p>}
        </div>
      )}

      <form noValidate onSubmit={(event) => void onSubmit(event)}>
        <div className="field">
          <label htmlFor={ids.email}>{t.email}</label>
          <input
            id={ids.email}
            name="email"
            type="email"
            inputMode="email"
            autoComplete="username"
            required
            value={email}
            onChange={(event) => {
              setEmail(event.target.value);
            }}
            aria-invalid={fieldErrors.email ? true : undefined}
            aria-describedby={fieldErrors.email ? ids.emailError : undefined}
          />
          {fieldErrors.email && (
            <p id={ids.emailError} className="field-error">
              {fieldErrors.email}
            </p>
          )}
        </div>

        <div className="field">
          <label htmlFor={ids.password}>{t.password}</label>
          <input
            id={ids.password}
            name="password"
            type="password"
            autoComplete="current-password"
            required
            value={password}
            onChange={(event) => {
              setPassword(event.target.value);
            }}
            aria-invalid={fieldErrors.password ? true : undefined}
            aria-describedby={fieldErrors.password ? ids.passwordError : undefined}
          />
          {fieldErrors.password && (
            <p id={ids.passwordError} className="field-error">
              {fieldErrors.password}
            </p>
          )}
        </div>

        <button type="submit" className="primary" disabled={pending || locked} aria-busy={pending || undefined}>
          {pending ? t.submitting : locked ? t.waitCountdown(secondsLeft) : t.submit}
        </button>
      </form>
    </section>
  );
}

function validate(email: string, password: string): FieldErrors {
  const errors: FieldErrors = {};
  const trimmed = email.trim();
  if (trimmed === '') errors.email = t.emailRequired;
  else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmed)) errors.email = t.emailInvalid;
  if (password === '') errors.password = t.passwordRequired;
  return errors;
}

/** The API's field messages are English and generic, so only the field names are used. */
function fieldErrorsFrom(problem: ApiProblem | null): FieldErrors {
  const errors: FieldErrors = {};
  for (const key of Object.keys(problem?.errors ?? {})) {
    const field = key.replace(/^\$\./, '').toLowerCase();
    if (field === 'email' || field === 'password') errors[field] = t.fieldInvalid;
  }
  return errors;
}

function describe(problem: ApiProblem | null): string {
  if (problem === null) return messages.errors.network;
  const text = problem.code === null ? messages.errors.unexpected : messages.errors.codes[problem.code];
  return problem.code === 'internal_error' && problem.traceId
    ? `${text} ${messages.errors.supportCode(problem.traceId)}`
    : text;
}
