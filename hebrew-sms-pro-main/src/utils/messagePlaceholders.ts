/**
 * Extracts placeholders wrapped in [brackets] from a message string.
 * Example: "שלום [שם], תעודה [תעודת זהות]" → ["שם", "תעודת זהות"]
 */
export function extractPlaceholders(message: string): string[] {
  const regex = /\[([^\]]+)\]/g;
  const matches: string[] = [];
  let m: RegExpExecArray | null;
  while ((m = regex.exec(message)) !== null) {
    const key = m[1].trim();
    if (key && !matches.includes(key)) {
      matches.push(key);
    }
  }
  return matches;
}

/**
 * Returns an array of placeholders that do NOT exist as columns in the CSV.
 */
export function findMissingPlaceholders(message: string, columns: string[]): string[] {
  const placeholders = extractPlaceholders(message);
  const normalizedColumns = columns.map((c) => c.trim());
  return placeholders.filter((p) => !normalizedColumns.includes(p));
}
