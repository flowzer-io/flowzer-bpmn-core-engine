import { useEffect, useState } from 'react';

/**
 * Grenze zwischen Telefon und allem Größeren. Deckt sich mit Tailwinds `md`, damit
 * dieselbe Zahl nicht an zwei Stellen mit unterschiedlichem Wert steht.
 */
const COMPACT_QUERY = '(max-width: 767px)';

/**
 * Sagt, ob die Oberfläche gerade auf Telefonbreite läuft.
 *
 * Für reine Darstellung genügen die `md:`-Klassen; dieser Haken ist für die Fälle, in
 * denen sich das *Verhalten* unterscheidet. Die Aufgabenseite wählt am großen Schirm
 * automatisch die erste Aufgabe aus — dort ist die Liste ja daneben zu sehen. Auf dem
 * Telefon verdeckte dieselbe Vorauswahl die Liste, und man käme nie an die zweite Aufgabe.
 */
export function useCompactLayout(): boolean {
  const [compact, setCompact] = useState(
    () => typeof window !== 'undefined' && window.matchMedia(COMPACT_QUERY).matches,
  );

  useEffect(() => {
    const query = window.matchMedia(COMPACT_QUERY);
    const update = (event: MediaQueryListEvent) => setCompact(event.matches);
    setCompact(query.matches);
    query.addEventListener('change', update);
    return () => query.removeEventListener('change', update);
  }, []);

  return compact;
}
