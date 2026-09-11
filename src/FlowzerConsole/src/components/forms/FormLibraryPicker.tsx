import { useMemo, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { FieldLabel } from '@/components/ui/Field';
import { InlineSpinner } from '@/components/ui/States';
import { useFormFolders, useForms, useFormVersions } from '@/lib/api/queries';
import type { FormVersionSummaryDto } from '@/lib/api/types';

interface FormLibraryPickerProps {
  currentFormId: string;
  disabled?: boolean;
  onInsert: (version: FormVersionSummaryDto, name: string) => void;
}

/**
 * Ein Formular ist zugleich die wiederverwendbare Komponente. Die Auswahl liefert nur eine
 * stabile ID und konkrete Version; Schemaauflösung und Berechtigungsprüfung bleiben beim Server.
 */
export function FormLibraryPicker({ currentFormId, disabled = false, onInsert }: FormLibraryPickerProps) {
  const formsQuery = useForms();
  const foldersQuery = useFormFolders();
  const [formId, setFormId] = useState('');
  const [versionText, setVersionText] = useState('');
  const versionsQuery = useFormVersions(formId || undefined);
  const form = formsQuery.data?.find((item) => item.formId === formId);
  const selected = useMemo(
    () => versionsQuery.data?.find((item) => `${item.version.major}.${item.version.minor}` === versionText),
    [versionText, versionsQuery.data],
  );
  const choices = useMemo(() => (formsQuery.data ?? [])
    .filter((item) => item.formId !== currentFormId)
    .map((item) => ({ ...item, catalogLabel: labelFor(item.folderId, foldersQuery.data ?? [], item.name) }))
    .sort((a, b) => a.catalogLabel.localeCompare(b.catalogLabel, 'de')),
  [currentFormId, foldersQuery.data, formsQuery.data]);

  function selectForm(next: string) {
    setFormId(next);
    setVersionText('');
  }

  return (
    <Card className="mb-3 p-3.5">
      <div className="mb-1 text-sm font-semibold">Formular-Komponente einfügen</div>
      <p className="text-muted mb-2.5 mt-0 text-xs">
        Jedes veröffentlichte Formular kann als fest gebundene Komponente verwendet werden.
      </p>
      <div className="flex flex-wrap items-end gap-2.5">
        <div className="min-w-[210px] flex-1">
          <FieldLabel htmlFor="form-library-component">Formular</FieldLabel>
          <select
            id="form-library-component"
            value={formId}
            onChange={(event) => selectForm(event.target.value)}
            disabled={disabled || formsQuery.isPending}
            className="bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] outline-none focus:border-accent"
          >
            <option value="">Formular auswählen …</option>
            {choices.map((item) => (
              <option key={item.formId} value={item.formId}>{item.catalogLabel}</option>
            ))}
          </select>
        </div>
        <div className="min-w-[150px] flex-1">
          <FieldLabel htmlFor="form-library-version">Konkrete Version</FieldLabel>
          <select
            id="form-library-version"
            value={versionText}
            onChange={(event) => setVersionText(event.target.value)}
            disabled={disabled || !formId || versionsQuery.isPending}
            className="bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] outline-none focus:border-accent"
          >
            <option value="">Version auswählen …</option>
            {(versionsQuery.data ?? []).map((item) => {
              const value = `${item.version.major}.${item.version.minor}`;
              return <option key={item.id} value={value}>v{value}</option>;
            })}
          </select>
        </div>
        <Button
          size="sm"
          variant="secondary"
          icon="add"
          disabled={disabled || !selected || !form}
          onClick={() => selected && form && onInsert(selected, form.name)}
        >
          Einfügen
        </Button>
      </div>
      {(formsQuery.isPending || foldersQuery.isPending) && <InlineSpinner label="Formularbibliothek wird geladen …" />}
      {versionsQuery.isPending && formId && <InlineSpinner label="Versionen werden geladen …" />}
      {!formsQuery.isPending && choices.length === 0 && (
        <p className="text-muted mb-0 mt-2 text-xs">Noch kein anderes Formular verfügbar.</p>
      )}
      {form && versionsQuery.data?.length === 0 && !versionsQuery.isPending && (
        <p className="text-muted mb-0 mt-2 text-xs">Dieses Formular ist noch nicht veröffentlicht.</p>
      )}
      {(formsQuery.error || foldersQuery.error || versionsQuery.error) && (
        <p className="text-fail mb-0 mt-2 text-xs">Die serverseitige Komponentenauswahl konnte nicht geladen werden.</p>
      )}
    </Card>
  );
}

function labelFor(
  folderId: string | null | undefined,
  folders: ReadonlyArray<{ id: string; parentId?: string | null; name: string }>,
  formName = '',
): string {
  const byId = new Map(folders.map((folder) => [folder.id, folder]));
  const names: string[] = [];
  const seen = new Set<string>();
  let current = folderId ?? null;
  while (current && !seen.has(current)) {
    seen.add(current);
    const folder = byId.get(current);
    if (!folder) break;
    names.unshift(folder.name);
    current = folder.parentId ?? null;
  }
  return [...names, formName].filter(Boolean).join(' / ');
}
