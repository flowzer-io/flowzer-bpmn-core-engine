import { useMemo, useState } from 'react';

import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { FieldLabel } from '@/components/ui/Field';
import { InlineSpinner } from '@/components/ui/States';
import { useFormSectionVersions, useFormSections } from '@/lib/api/queries';
import type { FormSectionVersionSummaryDto } from '@/lib/api/types';

interface FormSectionPickerProps {
  disabled?: boolean;
  onInsert: (version: FormSectionVersionSummaryDto, name: string) => void;
}

/**
 * Servergebundener Picker für Form.io-Formularabschnitte. Es gibt absichtlich kein
 * Freitextfeld für IDs oder Versionen: Die API liefert die Auswahl, der Server
 * prüft dieselbe konkrete Referenz beim Veröffentlichen nochmals.
 */
export function FormSectionPicker({ disabled = false, onInsert }: FormSectionPickerProps) {
  const sectionsQuery = useFormSections();
  const [sectionId, setSectionId] = useState('');
  const [versionText, setVersionText] = useState('');
  const versionsQuery = useFormSectionVersions(sectionId || undefined);
  const section = sectionsQuery.data?.find((item) => item.sectionId === sectionId);
  const selected = useMemo(
    () => versionsQuery.data?.find((item) => `${item.version.major}.${item.version.minor}` === versionText),
    [versionText, versionsQuery.data],
  );

  function selectSection(next: string) {
    setSectionId(next);
    setVersionText('');
  }

  return (
    <Card className="mb-3 p-3.5">
      <div className="mb-2.5 flex flex-wrap items-end gap-2.5">
        <div className="min-w-[180px] flex-1">
          <FieldLabel htmlFor="form-section-library">Wiederverwendbaren Abschnitt einfügen</FieldLabel>
          <select
            id="form-section-library"
            value={sectionId}
            onChange={(event) => selectSection(event.target.value)}
            disabled={disabled || sectionsQuery.isPending}
            className="bg-surface-2 border-border text-text w-full rounded-[var(--r-sm)] border px-3 py-2.5 text-[13.5px] outline-none focus:border-accent"
          >
            <option value="">Abschnitt auswählen …</option>
            {(sectionsQuery.data ?? []).map((item) => (
              <option key={item.sectionId} value={item.sectionId}>{item.name}</option>
            ))}
          </select>
        </div>
        <div className="min-w-[150px] flex-1">
          <FieldLabel htmlFor="form-section-version">Konkrete Version</FieldLabel>
          <select
            id="form-section-version"
            value={versionText}
            onChange={(event) => setVersionText(event.target.value)}
            disabled={disabled || !sectionId || versionsQuery.isPending}
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
          disabled={disabled || !selected || !section}
          onClick={() => selected && section && onInsert(selected, section.name)}
        >
          Einfügen
        </Button>
      </div>
      {sectionsQuery.isPending && <InlineSpinner label="Abschnitte werden geladen …" />}
      {versionsQuery.isPending && sectionId && <InlineSpinner label="Versionen werden geladen …" />}
      {!sectionsQuery.isPending && sectionsQuery.data?.length === 0 && (
        <p className="text-muted m-0 text-xs">Noch keine veröffentlichten Abschnitte verfügbar.</p>
      )}
      {section && versionsQuery.data?.length === 0 && !versionsQuery.isPending && (
        <p className="text-muted m-0 text-xs">Dieser Abschnitt hat noch keine veröffentlichte Version.</p>
      )}
      {(sectionsQuery.error || versionsQuery.error) && (
        <p className="text-fail m-0 text-xs">Die serverseitige Abschnittsauswahl konnte nicht geladen werden.</p>
      )}
    </Card>
  );
}
