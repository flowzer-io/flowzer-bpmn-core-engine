import { Icon } from '@/components/ui/Icon';
import { toneColor, toneSurface } from '@/components/ui/Chip';
import { useSession } from '@/stores/session';

/**
 * Angemeldet zu sein und zugelassen zu sein sind zwei verschiedene Dinge: Der
 * Identity Provider stellt jeder Person im Realm ein Token aus, die Anwendung
 * verlangt zusätzlich eine Rolle. Ohne diesen Hinweis sieht eine solche Person
 * nur rohe Fehlermeldungen auf jeder Seite.
 */
export function AccessDeniedNotice() {
  const accessDenied = useSession((state) => state.accessDenied);
  if (!accessDenied) return null;

  return (
    <div
      role="alert"
      className="mx-6 mt-6 flex items-start gap-3 rounded-[var(--r-md)] border px-4 py-3.5"
      style={{ borderColor: toneColor('wait'), background: toneSurface('wait', 10) }}
    >
      <Icon name="lock" size={20} className="mt-0.5 shrink-0" style={{ color: toneColor('wait') }} />
      <div>
        <div className="mb-1 text-[14.5px] font-semibold">Kein Zugang zu Flowzer</div>
        <p className="text-muted max-w-[60ch] text-[13.5px] leading-relaxed">
          Die Anmeldung hat funktioniert, aber dieses Konto ist für Flowzer nicht freigeschaltet.
          Wenden Sie sich an die IT und bitten Sie um die Freigabe.
        </p>
      </div>
    </div>
  );
}
