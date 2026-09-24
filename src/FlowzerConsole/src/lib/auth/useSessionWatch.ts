import { useEffect, useRef } from 'react';

import { getRuntimeConfig } from '@/lib/config/runtime';
import { useSession } from '@/stores/session';

/**
 * Hält die Fähigkeiten der angemeldeten Person in der Konsole aktuell.
 *
 * Die Konsole lädt `/bff/session` sonst nur einmal beim Start. Der BFF ersetzt Rollen
 * und Gruppen aber bei jeder serverseitigen Token-Erneuerung (60 s vor Ablauf des
 * Access-Tokens) durch den aktuellen Stand des Identity Providers. Ein Rollenentzug
 * wirkte deshalb in der API längst, während die Oberfläche die entzogenen Aktionen bis
 * zum nächsten Reload, 401 oder 403 weiter anbot. Diese Überwachung fragt die Sitzung
 * deshalb regelmäßig und bei Rückkehr ins Fenster erneut ab; geänderte Fähigkeiten
 * (`access`, `modeler`, `operator`, `worker`) wirken so ohne Reload.
 *
 * Die Anzeige bleibt eine Anzeige: Maßgeblich ist weiterhin die API bei jedem Aufruf.
 */

/** Abstand der regelmäßigen Abfrage, solange das Fenster sichtbar ist. */
export const SESSION_WATCH_INTERVAL_MS = 5 * 60_000;

/**
 * Mindestabstand zwischen zwei Abfragen. Fokus und Sichtbarkeitswechsel kommen beim
 * Zurückkehren meist gemeinsam; sie sollen dann nur eine Abfrage auslösen.
 */
export const SESSION_WATCH_MIN_GAP_MS = 30_000;

/**
 * Fragt die BFF-Sitzung alle fünf Minuten sowie bei Fokus und Sichtbarkeit erneut ab.
 *
 * Aktiv nur mit BFF und angemeldeter Person. Ohne BFF (lokaler Entwicklungsmodus) gibt es
 * serverseitig nichts nachzuladen; anonym entscheidet ohnehin die Anmeldeseite.
 */
export function useSessionWatch(): void {
  const signedIn = useSession((state) => state.status === 'signed-in');
  const active = signedIn && getRuntimeConfig().bffEnabled;

  // Über Effekt-Neustarts hinweg: Eine noch laufende Abfrage soll auch nach einem
  // Statuswechsel keine zweite neben sich bekommen.
  const inFlight = useRef(false);
  const lastCheck = useRef(0);

  useEffect(() => {
    if (!active) return;

    // Angemeldet wird man nur durch eine erfolgreiche Sitzungsabfrage. Die liegt also
    // gerade erst zurück; ein Fokus direkt nach dem Start braucht keine zweite.
    lastCheck.current = Date.now();

    const runRefresh = async () => {
      try {
        await useSession.getState().refresh();
      } catch {
        // Der Store meldet Verbindungsfehler selbst über `sessionError`. Die Überwachung
        // darf daraus keinen unbehandelten Fehler machen und versucht es später erneut.
      } finally {
        inFlight.current = false;
      }
    };

    const check = () => {
      if (document.visibilityState !== 'visible') return;
      if (inFlight.current) return;

      const now = Date.now();
      if (now - lastCheck.current < SESSION_WATCH_MIN_GAP_MS) return;

      inFlight.current = true;
      lastCheck.current = now;
      void runRefresh();
    };

    const interval = window.setInterval(check, SESSION_WATCH_INTERVAL_MS);
    document.addEventListener('visibilitychange', check);
    window.addEventListener('focus', check);

    return () => {
      window.clearInterval(interval);
      document.removeEventListener('visibilitychange', check);
      window.removeEventListener('focus', check);
    };
  }, [active]);
}
