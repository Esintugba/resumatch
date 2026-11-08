import { QueryClient } from '@tanstack/react-query';
import { shouldRetry } from './http';

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: shouldRetry,
        retryDelay: (attempt) => Math.min(1000 * 2 ** attempt, 8000),
        // Forms keep their own copy, so a background refetch never overwrites what the user is typing.
        refetchOnWindowFocus: true,
        staleTime: 30_000,
      },
      mutations: {
        retry: false,
      },
    },
  });
}
