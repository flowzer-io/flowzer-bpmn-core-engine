import { Button } from '@/components/ui/Button';
import { Card, CardHeader } from '@/components/ui/Card';
import { Chip } from '@/components/ui/Chip';
import { instanceBucket } from '@/lib/api/normalize';
import type { ProcessInstanceInfoDto } from '@/lib/api/types';
import { formatTimestamp, shortId } from '@/lib/format';
import { BUCKET_TONE, STATE_LABEL } from '@/lib/instanceView';

/** Bewusst keine Tokens/Variablen: Die API hat ausschließlich eine Übersicht freigegeben. */
export function InstanceOverview({ instance, onBack, onTasks }: {
  instance: ProcessInstanceInfoDto;
  onBack: () => void;
  onTasks: () => void;
}) {
  return (
    <div className="min-h-0 flex-1 overflow-auto p-4 sm:p-8">
      <div className="mx-auto max-w-3xl space-y-5">
        <Button variant="ghost" icon="arrow_back" onClick={onBack}>Zur Instanzliste</Button>
        <Card>
          <CardHeader title="Vorgangsübersicht" icon="shield_person" />
          <div className="space-y-5 p-5 sm:p-7">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="font-display min-w-0 break-words text-xl font-semibold">{instance.relatedDefinitionName}</h1>
              <Chip tone={BUCKET_TONE[instanceBucket(instance.state)]}>{STATE_LABEL[instance.state]}</Chip>
            </div>
            <p className="text-muted text-sm">
              Hier sehen Sie den Status Ihres Vorgangs. Prozessvariablen, interne Abläufe und
              technische Warteobjekte sind nur mit Diagnoseberechtigung sichtbar.
            </p>
            <dl className="grid gap-4 text-sm sm:grid-cols-2">
              <div><dt className="text-muted">Vorgang</dt><dd className="mt-1 font-mono">#{shortId(instance.instanceId)}</dd></div>
              <div><dt className="text-muted">Gestartet</dt><dd className="mt-1">{formatTimestamp(instance.startedAt)}</dd></div>
              {instance.finishedAt && <div><dt className="text-muted">Beendet</dt><dd className="mt-1">{formatTimestamp(instance.finishedAt)}</dd></div>}
              <div><dt className="text-muted">Offene Aufgaben im Vorgang</dt><dd className="mt-1">{instance.userTaskSubscriptionCount}</dd></div>
            </dl>
            {instance.userTaskSubscriptionCount > 0 && (
              <Button variant="primary" icon="assignment" onClick={onTasks}>Zu meinen Aufgaben</Button>
            )}
          </div>
        </Card>
      </div>
    </div>
  );
}
