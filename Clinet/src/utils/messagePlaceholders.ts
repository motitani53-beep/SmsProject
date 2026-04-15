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
 * Normalizes a field name for comparison: trims, strips bidi/zero-width marks
 * and BOM, collapses whitespace, and lowercases. This makes the placeholder
 * vs. CSV-column comparison forgiving of common Excel/Hebrew export quirks
 * (RLM/LRM/NBSP/BOM) and English casing differences.
 */
function normalizeFieldName(name: string): string {
  return name
    .replace(/[\u200E\u200F\u200B\u200C\u200D\uFEFF\u00A0]/g, '')
    .trim()
    .replace(/\s+/g, ' ')
    .toLowerCase();
}

/**
 * Returns an array of placeholders that do NOT exist as columns in the CSV.
 * Comparison is case-insensitive and tolerant of bidi/zero-width whitespace.
 */
export function findMissingPlaceholders(message: string, columns: string[]): string[] {
  const placeholders = extractPlaceholders(message);
  const normalizedColumns = new Set(columns.map(normalizeFieldName));
  return placeholders.filter((p) => !normalizedColumns.has(normalizeFieldName(p)));
}

/**
 * Returns CSV columns that are NOT referenced as [placeholders] in the message.
 * The phone column (and any falsy entries) are excluded since they are never
 * expected to appear inside the message body. Returned values use the original
 * column casing/spelling so they can be displayed back to the user.
 */
export function findUnusedColumns(
  message: string,
  columns: string[],
  phoneColumnName?: string | null
): string[] {
  const normalizedPlaceholders = new Set(
    extractPlaceholders(message).map(normalizeFieldName)
  );
  const normalizedPhone = phoneColumnName ? normalizeFieldName(phoneColumnName) : '';
  return columns.filter((col) => {
    const normalized = normalizeFieldName(col);
    if (!normalized) return false;
    if (normalized === normalizedPhone) return false;
    return !normalizedPlaceholders.has(normalized);
  });
}
