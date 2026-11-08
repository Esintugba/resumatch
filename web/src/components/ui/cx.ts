/** Joins class names, skipping empty ones (CSS Module lookups can be undefined). */
export function cx(...names: (string | false | null | undefined)[]): string {
  return names.filter(Boolean).join(' ');
}
