/**
 * Extracts `{placeholder}` tokens from a message string (same convention as the Web API).
 * Example: "שלום {שם}, תעודה {תעודת זהות}" → ["שם", "תעודת זהות"]
 *
 * Inner text is compared to CSV headers exactly (no lowercasing or bidi normalization),
 * so keys match `custom_fields` and server-side `ReplaceMessageFields`.
 */
export function extractPlaceholders(message: string): string[] {
  const regex = /\{([^{}]+)\}/g;
  const matches: string[] = [];
  let m: RegExpExecArray | null;
  while ((m = regex.exec(message)) !== null) {
    const key = m[1];
    if (key && !matches.includes(key)) {
      matches.push(key);
    }
  }
  return matches;
}

/**
 * Placeholders whose inner name is not present as an exact CSV column header.
 */
export function findMissingPlaceholders(message: string, columns: string[]): string[] {
  const placeholders = extractPlaceholders(message);
  const columnSet = new Set(columns);
  return placeholders.filter((p) => !columnSet.has(p));
}

/**
 * CSV columns that are not referenced as `{column}` in the message (exact header match).
 * The phone column is excluded since it is never a message placeholder.
 */
export function findUnusedColumns(
  message: string,
  columns: string[],
  phoneColumnName?: string | null
): string[] {
  const used = new Set(extractPlaceholders(message));
  return columns.filter((col) => {
    if (!col) return false;
    if (col === phoneColumnName) return false;
    return !used.has(col);
  });
}
