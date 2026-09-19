import { useEffect, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { FieldLabel, TextInput } from '@/components/ui/Field';
import { Chip } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { Modal } from '@/components/ui/Modal';
import { useImportPackage, usePreviewPackage } from '@/lib/api/queries';
import type {
  ProcessPackageImportResultDto,
  ProcessPackagePreviewDto,
  WorkflowFolderDto,
} from '@/lib/api/types';
import { pathLabel, sortByPath } from '@/lib/folderTree';
import {
  buildMapping,
  canImport,
  defaultTarget,
  formOutcomeLabel,
  informationalReferences,
  mappableReferences,
  referenceKindLabel,
  suggestedChoices,
  unresolvedReferences,
  type ImportTarget,
  type ReferenceChoices,
} from '@/lib/processPackage';

const SELECT_CLASS =
  'bg-surface-2 border-border text-text w-full cursor-pointer rounded-[var(--r-sm)] border px-2.5 py-2 text-[13px] outline-none focus:border-[var(--accent)]';

interface ImportPackageDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  folders: readonly WorkflowFolderDto[];
  /** Der Ordner, in dem der Katalog gerade steht; er ist der Vorschlag fürs Ziel. */
  selectedFolderId: string | null;
  mayUseRoot: boolean;
  /** Der Katalog soll sich neu laden; der Dialog bleibt für den Bericht stehen. */
  onImported: (result: ProcessPackageImportResultDto) => void;
  /** „Im Modellierer öffnen“ — der Weg, den der Bericht anbietet. */
  onOpenInModeler: (definitionId: string) => void;
}

/**
 * Trägt ein Prozesspaket in diese Installation ein.
 *
 * Bewusst in drei Schritten statt „Datei wählen und fertig“: Ein Paket bringt Bezüge mit,
 * die nur in der Installation gelten, aus der es stammt — Personen, Gruppen, Verbindungen.
 * Die werden hier ausdrücklich zugeordnet. Der Vorschlag ist der gleichnamige Eintrag, aber
 * bestätigen muss ihn ein Mensch.
 *
 * Es gibt noch keinen Dialog, der eine einzelne BPMN-Datei einliest; wenn einer dazukommt,
 * bleibt er getrennt: Eine BPMN-Datei ist ein Diagramm, ein Paket ist ein Workflow samt
 * seinen Formularen.
 */
export function ImportPackageDialog({
  open,
  onOpenChange,
  folders,
  selectedFolderId,
  mayUseRoot,
  onImported,
  onOpenInModeler,
}: ImportPackageDialogProps) {
  const [file, setFile] = useState<File | null>(null);
  const [preview, setPreview] = useState<ProcessPackagePreviewDto | null>(null);
  const [choices, setChoices] = useState<ReferenceChoices>({});
  const [target, setTarget] = useState<ImportTarget>({ mode: 'new', name: '', folderId: null });
  const [result, setResult] = useState<ProcessPackageImportResultDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const previewPackage = usePreviewPackage();
  const importPackage = useImportPackage();

  // Ein neu geöffneter Dialog beginnt leer; sonst stünde das Paket des letzten Versuchs da.
  useEffect(() => {
    if (!open) return;
    setFile(null);
    setPreview(null);
    setChoices({});
    setResult(null);
    setError(null);
  }, [open]);

  function choose(selected: File | null) {
    setError(null);
    setResult(null);
    setPreview(null);
    setFile(selected);
    if (!selected) return;

    previewPackage.mutate(selected, {
      onSuccess: (loaded) => {
        setPreview(loaded);
        setChoices(suggestedChoices(loaded));
        setTarget(defaultTarget(loaded, selectedFolderId));
      },
      onError: (cause) => setError(cause instanceof Error ? cause.message : 'Das Paket ist nicht lesbar.'),
    });
  }

  function submit() {
    if (!file || !preview) return;
    setError(null);
    importPackage.mutate(
      { file, mapping: buildMapping(preview, choices, target) },
      {
        onSuccess: (imported) => {
          setResult(imported);
          onImported(imported);
        },
        onError: (cause) =>
          setError(cause instanceof Error ? cause.message : 'Der Import ist fehlgeschlagen.'),
      },
    );
  }

  const busy = previewPackage.isPending || importPackage.isPending;
  const ready = preview !== null && canImport(preview, choices, target);
  const editableFolders = sortByPath(folders.filter((folder) => folder.mayEdit));

  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title="Paket importieren"
      icon="inventory_2"
      description="Ein Prozesspaket enthält einen Workflow samt seiner Formulare. Veröffentlicht wird es nicht — das bleibt eine eigene Entscheidung."
      className="w-[min(640px,calc(100vw-32px))]"
      footer={
        result ? (
          <>
            <Button size="sm" onClick={() => onOpenChange(false)}>
              Schließen
            </Button>
            <Button
              size="sm"
              variant="primary"
              icon="edit"
              onClick={() => onOpenInModeler(result.definitionId)}
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
              size="sm"
              variant="primary"
              icon="download"
              loading={importPackage.isPending}
              disabled={!ready || busy}
              onClick={submit}
            >
              Importieren
            </Button>
          </>
        )
      }
    >
      <div className="flex flex-col gap-4 pb-3">
        {!result && (
          <div>
            <FieldLabel>Paketdatei</FieldLabel>
            <input
              type="file"
              accept=".zip,application/zip"
              aria-label="Paketdatei"
              className="text-muted file:bg-surface-2 file:border-border file:text-text w-full text-[13px] file:mr-3 file:cursor-pointer file:rounded-[var(--r-sm)] file:border file:px-3 file:py-1.5"
              onChange={(event) => choose(event.target.files?.[0] ?? null)}
            />
            {previewPackage.isPending && (
              <div className="text-muted mt-2 text-[12.5px]">Das Paket wird gelesen …</div>
            )}
          </div>
        )}

        {error && (
          <div className="text-fail bg-fail/10 rounded-[var(--r-sm)] px-3 py-2 text-[13px]" role="alert">
            {error}
          </div>
        )}

        {preview && !result && <PreviewBody preview={preview} />}

        {preview && !result && mappableReferences(preview).length > 0 && (
          <div>
            <FieldLabel>Zuordnungen</FieldLabel>
            <p className="text-muted mt-0 mb-2 text-[12.5px]">
              Diese Bezüge gelten nur dort, wo das Paket herkommt. Ordnen Sie jedem einen Eintrag
              dieser Installation zu.
            </p>
            <div className="flex flex-col gap-2.5">
              {mappableReferences(preview).map((option) => (
                <div key={option.reference.id} className="border-border rounded-[var(--r-sm)] border p-2.5">
                  <div className="mb-1.5 flex items-center gap-2 text-[13px]">
                    <Chip tone="accent">{referenceKindLabel(option.reference.kind)}</Chip>
                    <span className="min-w-0 truncate font-semibold">{option.reference.label}</span>
                    <span className="text-faint min-w-0 truncate text-[12px]">
                      {option.reference.elementName || option.reference.elementId}
                    </span>
                  </div>
                  <select
                    className={SELECT_CLASS}
                    aria-label={`Zuordnung für ${option.reference.label}`}
                    value={choices[option.reference.id] ?? ''}
                    onChange={(event) =>
                      setChoices((previous) => ({ ...previous, [option.reference.id]: event.target.value }))
                    }
                  >
                    <option value="">Bitte zuordnen …</option>
                    {option.candidates.map((candidate) => (
                      <option key={candidate.id} value={candidate.id}>
                        {candidate.label}
                        {candidate.hint ? ` · ${candidate.hint}` : ''}
                      </option>
                    ))}
                  </select>
                  {option.candidates.length === 0 && (
                    <div className="text-muted mt-1.5 text-[12px]">
                      Hier gibt es dafür noch keinen Eintrag. Legen Sie ihn zuerst an.
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}

        {preview && !result && (
          <div>
            <FieldLabel>Ziel</FieldLabel>
            {preview.conflict && (
              <p className="text-muted mt-0 mb-2 text-[12.5px]">
                Die Kennung <code>{preview.conflict.definitionId}</code> gibt es hier schon
                {preview.conflict.latestVersion ? ` (${preview.conflict.latestVersion})` : ''}:{' '}
                „{preview.conflict.name}“.
              </p>
            )}
            <div className="flex flex-col gap-2">
              <label className="flex items-center gap-2 text-[13.5px]">
                <input
                  type="radio"
                  name="paket-ziel"
                  checked={target.mode === 'new'}
                  onChange={() => setTarget((previous) => ({ ...previous, mode: 'new' }))}
                />
                Als neuen Workflow anlegen
              </label>
              <label className="flex items-center gap-2 text-[13.5px]">
                <input
                  type="radio"
                  name="paket-ziel"
                  disabled={!preview.conflict?.mayCreateNewVersion}
                  checked={target.mode === 'newVersionOf'}
                  onChange={() => setTarget((previous) => ({ ...previous, mode: 'newVersionOf' }))}
                />
                Als neuen Stand von „{preview.conflict?.name ?? preview.manifest.workflow.name}“
                {preview.conflict ? '' : ' (nicht vorhanden)'}
              </label>
            </div>

            {target.mode === 'new' && (
              <div className="mt-2.5 flex flex-col gap-2.5">
                <div>
                  <FieldLabel>Name im Katalog</FieldLabel>
                  <TextInput
                    value={target.name}
                    aria-label="Name im Katalog"
                    onChange={(event) =>
                      setTarget((previous) => ({ ...previous, name: event.target.value }))
                    }
                  />
                </div>
                <div>
                  <FieldLabel>Ordner</FieldLabel>
                  <select
                    className={SELECT_CLASS}
                    aria-label="Ordner"
                    value={target.folderId ?? ''}
                    onChange={(event) =>
                      setTarget((previous) => ({ ...previous, folderId: event.target.value || null }))
                    }
                  >
                    <option value="" disabled={!mayUseRoot}>
                      Oberste Ebene{mayUseRoot ? '' : ' (nicht erlaubt)'}
                    </option>
                    {editableFolders.map((folder) => (
                      <option key={folder.id} value={folder.id}>
                        {pathLabel(folder.id, folders)}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
            )}

            {unresolvedReferences(preview, choices).length > 0 && (
              <div className="text-muted mt-2.5 text-[12.5px]">
                Noch offen: {unresolvedReferences(preview, choices).length} Zuordnung(en).
              </div>
            )}
          </div>
        )}

        {result && <ImportReport result={result} />}
      </div>
    </Modal>
  );
}

/** Was im Paket steht und ob diese Installation es annehmen würde. */
function PreviewBody({ preview }: { preview: ProcessPackagePreviewDto }) {
  const workflow = preview.manifest.workflow;
  const hints = informationalReferences(preview);

  return (
    <div className="border-border rounded-[var(--r-sm)] border p-3">
      <div className="flex items-center gap-2">
        <span className="font-display min-w-0 truncate text-[15px] font-semibold">{workflow.name}</span>
        <Chip tone="muted">v{workflow.version}</Chip>
        <Chip tone={workflow.source === 'deployed' ? 'done' : 'wait'}>
          {workflow.source === 'deployed' ? 'Veröffentlicht' : 'Entwurf'}
        </Chip>
      </div>

      <div className="text-muted mt-2 text-[12.5px]">
        {preview.manifest.forms.length} Formular(e) · {workflow.processIds.length} Prozess(e) ·
        Fähigkeitsvertrag v{preview.manifest.bpmnCapabilitiesContract} · {preview.manifest.formsContract}
      </div>

      <div className="mt-2.5 flex items-center gap-1.5 text-[13px]">
        <Icon
          name={preview.deployableHere ? 'check_circle' : 'error'}
          size={16}
          className={preview.deployableHere ? 'text-done' : 'text-fail'}
        />
        {preview.deployableHere
          ? 'Dieses Modell ließe sich hier veröffentlichen.'
          : 'Dieses Modell ließe sich hier noch nicht veröffentlichen — es kommt als Entwurf an.'}
      </div>

      {preview.problems.length > 0 && (
        <ul className="text-muted mt-1.5 mb-0 pl-5 text-[12.5px]">
          {preview.problems.map((problem) => (
            <li key={`${problem.code}-${problem.elementId ?? ''}`}>
              {problem.message}
              {problem.elementId ? ` (${problem.elementId})` : ''}
            </li>
          ))}
        </ul>
      )}

      {preview.notices.map((notice) => (
        <div key={notice.code} className="text-muted mt-1.5 text-[12.5px]">
          {notice.message}
        </div>
      ))}

      {hints.length > 0 && (
        <div className="text-muted mt-2.5 text-[12.5px]">
          Diese Installation muss bereitstellen:{' '}
          {hints
            .map((option) => `${referenceKindLabel(option.reference.kind)} „${option.reference.label}“`)
            .join(', ')}
          .
        </div>
      )}
    </div>
  );
}

/** Der Bericht nach dem Import — was entstanden ist und was noch aussteht. */
function ImportReport({ result }: { result: ProcessPackageImportResultDto }) {
  return (
    <div className="border-border rounded-[var(--r-sm)] border p-3">
      <div className="flex items-center gap-2 text-[14px] font-semibold">
        <Icon name="check_circle" size={17} className="text-done" />
        „{result.name}“ wurde angelegt
      </div>
      <div className="text-muted mt-1.5 text-[12.5px]">
        Kennung <code>{result.definitionId}</code> · Fassung {result.version.major}.{result.version.minor}
      </div>

      {result.forms.length > 0 && (
        <ul className="text-muted mt-2 mb-0 pl-5 text-[12.5px]">
          {result.forms.map((form) => (
            <li key={form.formKey}>
              {form.name}
              {form.revision ? ` ${form.revision}` : ''} — {formOutcomeLabel(form.outcome)}
            </li>
          ))}
        </ul>
      )}

      {result.notices.map((notice) => (
        <div key={`${notice.code}-${notice.message}`} className="text-muted mt-2 text-[12.5px]">
          {notice.message}
        </div>
      ))}
    </div>
  );
}
