import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/Card';

interface MigrationRetryNoticeProps {
  icon: string;
  title: string;
  description: string;
  onRetry: () => void;
}

/**
 * Fehlermeldung samt neuem Anlauf im Migrationsassistenten.
 *
 * Der Trockenlauf ist folgenlos und darf beliebig wiederholt werden — auch nach einem
 * Versionskonflikt. Deshalb endet jeder Fehlweg hier und nicht in einem toten Dialog.
 */
export function MigrationRetryNotice({
  icon,
  title,
  description,
  onRetry,
}: MigrationRetryNoticeProps) {
  return (
    <EmptyState
      icon={icon}
      title={title}
      description={description}
      action={
        <Button icon="refresh" onClick={onRetry}>
          Erneut prüfen
        </Button>
      }
    />
  );
}
