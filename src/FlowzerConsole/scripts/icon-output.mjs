/**
 * Maskiert fremde SVG-Daten fuer ein einfach quotiertes TypeScript-Stringliteral.
 * Backslashes muessen vor Quotes und Steuerzeichen behandelt werden, damit keine
 * bereits eingefuehrte Escape-Sequenz erneut interpretiert wird.
 */
export function escapeSingleQuotedLiteral(value) {
  return value
    .replaceAll('\\', '\\\\')
    .replaceAll("'", "\\'")
    .replaceAll('\r', '\\r')
    .replaceAll('\n', '\\n')
    .replaceAll('\u2028', '\\u2028')
    .replaceAll('\u2029', '\\u2029');
}
