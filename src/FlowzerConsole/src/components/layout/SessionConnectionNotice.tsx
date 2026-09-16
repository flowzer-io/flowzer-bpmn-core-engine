import { Button } from '@/components/ui/Button';
import { useSession } from '@/stores/session';

/** Ein Verbindungsfehler darf das bearbeitete Formular nicht durch die Loginseite ersetzen. */
export function SessionConnectionNotice() {
  const error = useSession((state) => state.sessionError);
  const refresh = useSession((state) => state.refresh);
  if (!error) return null;
  return <div role="alert" className="bg-surface-2 flex items-center gap-3 px-6 py-3 text-sm">
    <span>{error}</span>
    <Button size="sm" variant="secondary" onClick={() => void refresh()}>Erneut prüfen</Button>
  </div>;
}
