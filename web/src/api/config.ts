/** API origin without a trailing slash. Empty means same origin (the Vite proxy in development). */
export const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/+$/, '');
