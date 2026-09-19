import { useEffect, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { Modal } from '@/components/ui/Modal';
import { suggestWorkflowName } from '@/lib/modeling/bpmnImport';
import {
  convertCamunda7,
  detectCamunda7,
  isEmptyReport,
  type Finding,
  type ImportReport,
} from '@/lib/modeling/camunda7Import';

/** Muss zur Grenze in <c>DefinitionController.MaxDefinitionNameLength</c> passen. */
const MAX_NAME_LENGTH = 200;

const FORM_ID = 'workflow-importieren-formular';

export interface ImportedWorkflow {
  name: string;
  xml: string;
  report: ImportReport;
  /** Ob die Datei aus Camunda 7 stammt — für die Meldung nach dem Anlegen. */
  fromCamunda7: boolean;
}

interface ImportWorkflowDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /**
   * Legt den Workflow an und liefert seine Kennung zurück. Die Zusage scheitert, wenn das
   * Anlegen scheitert — der Dialog bleibt dann mit der gewählten Datei stehen.
   */
  onCreate: (workflow: ImportedWorkflow) => Promise<string>;
  /** Öffnet den Modellierer für die angelegte Definition. */
  onOpenModeler: (definitionId: string) => void;
}

interface LoadedFile {
  fileName: string;
  xml: string;
  report: ImportReport;
  fromCamunda7: boolean;
}

/**
 * Importiert eine BPMN-Datei als neuen Workflow.
 *
 * Camunda-7-Dateien werden dabei nach `zeebe:*` übersetzt; eine gewöhnliche BPMN-Datei
 * läuft durch denselben Weg, nur ohne Übersetzung. Zwei getrennte Bedienwege für „Datei
 * öffnen“ wären derselbe Vorgang mit zwei Namen.
 *
 * Der Bericht steht **vor** dem Anlegen da — niemand soll erst danach erfahren, dass die
 * Hälfte des Prozesses nicht läuft. Hat der Bericht etwas zu sagen, bleibt er nach dem
 * Anlegen stehen, bis jemand den Modellierer öffnet oder den Dialog schließt.
 */
export function ImportWorkflowDialog({ open, onOpenChange, onCreate, onOpenModeler }: ImportWorkflowDialogProps) {
  const [loaded, setLoaded] = useState<LoadedFile | null>(null);
  const [name, setName] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [createdId, setCreatedId] = useState<string | null>(null);

  // Beim Öffnen wieder von vorn: Sonst stünde die Datei des letzten Versuchs da.
  useEffect(() => {
    if (!open) return;
    setLoaded(null);
    setName('');
    setProblem(null);
    setBusy(false);
    setCreatedId(null);
  }, [open]);

  async function chooseFile(file: File | undefined) {
    if (!file) return;

    setProblem(null);
    setCreatedId(null);

    let text: string;
    try {
      text = await readTextFile(file);
    } catch {
      setProblem('Die Datei konnte nicht gelesen werden.');
      return;
    }

    try {
      const fromCamunda7 = detectCamunda7(text);
      const { xml, report } = convertCamunda7(text);
      setLoaded({ fileName: file.name, xml, report, fromCamunda7 });
      setName(suggestWorkflowName(xml, file.name).slice(0, MAX_NAME_LENGTH));
    } catch {
      setLoaded(null);
      setProblem('Die Datei ist kein lesbares BPMN-XML.');
    }
  }

  const trimmed = name.trim();
  const tooLong = trimmed.length > MAX_NAME_LENGTH;
  const valid = loaded !== null && trimmed.length > 0 && !tooLong;

  async function submit() {
    if (!valid || busy || !loaded) return;

    setBusy(true);
    setProblem(null);
    try {
      const definitionId = await onCreate({
        name: trimmed,
        xml: loaded.xml,
        report: loaded.report,
        fromCamunda7: loaded.fromCamunda7,
      });

      // Gibt es nichts zu berichten, ist der Dialog fertig und der Modellierer dran.
      if (isEmptyReport(loaded.report)) {
        onOpenChange(false);
        onOpenModeler(definitionId);
        return;
      }

      setCreatedId(definitionId);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : 'Der Workflow konnte nicht angelegt werden.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="BPMN-Datei importieren"
      icon="upload"
      className="w-[min(680px,calc(100vw-32px))]"
      description={
        createdId
          ? 'Der Workflow ist angelegt. Der Bericht bleibt hier stehen, bis Sie den Dialog schließen.'
          : 'Eine BPMN-Datei wird als neuer Workflow angelegt. Camunda-7-Modelle werden dabei übersetzt.'
      }
      footer={
        createdId ? (
          <>
            <Button size="sm" onClick={() => onOpenChange(false)}>
              Schließen
            </Button>
            <Button
              size="sm"
              variant="primary"
              icon="schema"
              onClick={() => {
                onOpenChange(false);
                onOpenModeler(createdId);
              }}
            >
              Im Modellierer öffnen
            </Button>
          </>
        ) : (
          <>
            <Button size="sm" onClick={() => onOpenChange(false)} disabled={busy}>
              Abbrechen
            </Button>
            <Button
              type="submit"
              form={FORM_ID}
              size="sm"
              variant="primary"
              icon="add"
              loading={busy}
              disabled={!valid}
            >
              Als neuen Workflow anlegen
            </Button>
          </>
        )
      }
    >
      <form
        id={FORM_ID}
        className="pb-3"
        onSubmit={(event) => {
          event.preventDefault();
          void submit();
        }}
      >
        {!createdId && (
          <>
            <FieldLabel htmlFor="workflow-import-datei">BPMN-Datei</FieldLabel>
            <input
              id="workflow-import-datei"
              type="file"
              accept=".bpmn,.xml,application/xml,text/xml"
              className="text-muted w-full text-[13px]"
              onChange={(event) => void chooseFile(event.target.files?.[0])}
            />

            {loaded && (
              <div className="mt-4">
                <FieldLabel htmlFor="workflow-import-name">Name</FieldLabel>
                <TextInput
                  id="workflow-import-name"
                  value={name}
                  maxLength={MAX_NAME_LENGTH + 1}
                  placeholder="z. B. Bestellprozess"
                  onChange={(event) => setName(event.target.value)}
                />
                {tooLong && (
                  <div className="text-fail mt-1.5 text-[12.5px]">Höchstens {MAX_NAME_LENGTH} Zeichen.</div>
                )}
              </div>
            )}
          </>
        )}

        {problem && <div className="text-fail mt-3 text-[13px]">{problem}</div>}

        {loaded && (
          <div className="mt-5">
            <div className="text-muted mb-3 text-[13px]">
              {loaded.fromCamunda7
                ? `„${loaded.fileName}“ stammt aus Camunda 7 und wurde übersetzt.`
                : `„${loaded.fileName}“ enthält keine Camunda-7-Angaben und wird unverändert übernommen.`}
            </div>
            <ImportReportView report={loaded.report} />
          </div>
        )}
      </form>
    </Modal>
  );
}

/**
 * Liest die Datei als Text.
 *
 * Bewusst ueber `FileReader` und nicht ueber `Blob.text()`: Letzteres fehlt in jsdom, und
 * eine Funktion, die sich nur im Browser pruefen laesst, ist eine ungepruefte Funktion.
 */
function readTextFile(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error('Die Datei konnte nicht gelesen werden.'));
    reader.onload = () => resolve(typeof reader.result === 'string' ? reader.result : '');
    reader.readAsText(file);
  });
}

const GROUPS: Array<{ key: keyof ImportReport; title: string; icon: string; empty: string; tone: string }> = [
  {
    key: 'converted',
    title: 'Übernommen',
    icon: 'check_circle',
    empty: 'Nichts zu übersetzen.',
    tone: 'text-ok',
  },
  {
    key: 'attention',
    title: 'Nacharbeit nötig',
    icon: 'warning',
    empty: 'Nichts nachzuarbeiten.',
    tone: 'text-warn',
  },
  {
    key: 'dropped',
    title: 'Entfallen',
    icon: 'remove',
    empty: 'Nichts entfallen.',
    tone: 'text-muted',
  },
];

/** Der Bericht in drei Gruppen, je Element eine Zeile. */
export function ImportReportView({ report }: { report: ImportReport }) {
  if (isEmptyReport(report)) {
    return <div className="text-muted text-[13px]">Am Modell war nichts zu ändern.</div>;
  }

  return (
    <div className="flex flex-col gap-4">
      {GROUPS.map((group) => (
        <section key={group.key}>
          <div className="mb-1.5 flex items-center gap-1.5 text-[13px] font-semibold">
            <Icon name={group.icon} size={16} className={group.tone} />
            {group.title}
            <span className="text-faint font-normal">({report[group.key].length})</span>
          </div>
          {report[group.key].length === 0 ? (
            <div className="text-faint pl-[22px] text-[12.5px]">{group.empty}</div>
          ) : (
            <ul className="m-0 flex list-none flex-col gap-1.5 p-0 pl-[22px]">
              {report[group.key].map((finding, index) => (
                <FindingRow key={`${finding.elementId}-${index}`} finding={finding} />
              ))}
            </ul>
          )}
        </section>
      ))}
    </div>
  );
}

function FindingRow({ finding }: { finding: Finding }) {
  return (
    <li className="text-[12.5px] leading-normal">
      <span className="text-text font-medium">{finding.elementName ?? finding.elementId}</span>
      <span className="text-faint"> · {finding.elementId}</span>
      <div className="text-muted">
        {finding.what}
        {finding.detail && <span className="text-faint"> — {finding.detail}</span>}
      </div>
    </li>
  );
}
