import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { ServerWakeGate } from './ServerWakeGate';

const fast = { slowAfterMs: 30, giveUpAfterMs: 200, firstRetryDelayMs: 10 } as const;
const wakingText = 'Sunucu uyanıyor, en fazla bir dakika sürebilir.';

function ok() {
  return Promise.resolve(new Response('{}', { status: 200 }));
}

function renderGate(fetchFn: typeof fetch, baseUrl = '') {
  return render(
    <ServerWakeGate fetchFn={fetchFn} baseUrl={baseUrl} {...fast}>
      <p>content</p>
    </ServerWakeGate>,
  );
}

describe('ServerWakeGate', () => {
  it('renders the children without the waking message when the API answers at once', async () => {
    const fetchFn = vi.fn<typeof fetch>(ok);

    renderGate(fetchFn, 'https://api.example.test');

    expect(await screen.findByText('content')).toBeTruthy();
    expect(screen.queryByText(wakingText)).toBeNull();
    expect(fetchFn).toHaveBeenCalledWith(
      'https://api.example.test/health/live',
      expect.objectContaining({ cache: 'no-store' }),
    );
  });

  it('shows the waking message while the API is slow, then the children', async () => {
    let answer: (response: Response) => void = () => undefined;
    const fetchFn = vi.fn<typeof fetch>(
      () =>
        new Promise<Response>((resolve) => {
          answer = resolve;
        }),
    );

    renderGate(fetchFn);

    expect((await screen.findByRole('status')).textContent).toBe(wakingText);
    expect(screen.getByText(/Geçen süre: \d+ sn/)).toBeTruthy();
    expect(screen.queryByText('content')).toBeNull();

    answer(new Response('{}', { status: 200 }));

    expect(await screen.findByText('content')).toBeTruthy();
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('retries failed attempts and recovers', async () => {
    const fetchFn = vi
      .fn<typeof fetch>()
      .mockRejectedValueOnce(new TypeError('Failed to fetch'))
      .mockResolvedValueOnce(new Response(null, { status: 503 }))
      .mockImplementation(ok);

    renderGate(fetchFn);

    expect(await screen.findByText('content')).toBeTruthy();
    expect(fetchFn).toHaveBeenCalledTimes(3);
  });

  it('gives up after the limit and checks again on retry', async () => {
    const fetchFn = vi.fn<typeof fetch>().mockRejectedValue(new TypeError('Failed to fetch'));

    renderGate(fetchFn);

    expect(await screen.findByRole('alert')).toBeTruthy();
    expect(screen.queryByText('content')).toBeNull();

    fetchFn.mockImplementation(ok);
    fireEvent.click(screen.getByRole('button', { name: 'Tekrar dene' }));

    expect(await screen.findByText('content')).toBeTruthy();
    expect(screen.queryByRole('alert')).toBeNull();
  });
});
