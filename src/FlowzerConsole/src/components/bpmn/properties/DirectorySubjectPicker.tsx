import { useEffect, useId, useState } from 'react';

import {
  useDirectorySubjectResolutions,
  useDirectorySubjectSearch,
} from '@/lib/api/queries';
import type { DirectorySubjectDto, SubjectRefDto } from '@/lib/api/types';

import { FieldLabel, SearchInput } from '@/components/ui/Field';
import { Icon } from '@/components/ui/Icon';
import { cn } from '@/lib/cn';

/** Ein vom Modeler gespeicherter Directory-Wert samt seiner Anzeigeprojektion. */
export interface DirectorySubjectSelection {
  subject: SubjectRefDto;
  displayName: string;
  detail: string;
  /** False bei einer historischen oder inzwischen deaktivierten Referenz. */
  available: boolean;
}

export interface DirectorySubjectPickerProps {
  definitionId: string;
  kind: SubjectRefDto['kind'];
  selected: DirectorySubjectSelection[];
  multiple: boolean;
  disabled?: boolean;
  label: string;
  onChange: (selected: DirectorySubjectSelection[]) => void;
}

/**
 * Workflowgebundene Auswahl für eine stabile Benutzer- oder Gruppenreferenz.
 *
 * Die Komponente kennt bewusst weder BPMN-Moddle noch den Editor. Sie erhält die bereits
 * gespeicherten Referenzen und liefert bei Auswahl nur die vom API-Endpunkt erhaltene
 * Anzeigeprojektion zurück. Dadurch bleiben auch unbekannte historische IDs als Warn-Chips
 * sichtbar, statt bei einer neuen Suche still verloren zu gehen.
 */
export function DirectorySubjectPicker({
  definitionId,
  kind,
  selected,
  multiple,
  disabled = false,
  label,
  onChange,
}: DirectorySubjectPickerProps) {
  const fieldId = useId();
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');

  useEffect(() => {
    const timeout = window.setTimeout(() => setDebouncedQuery(query.trim()), 250);
    return () => window.clearTimeout(timeout);
  }, [query]);

  const canSearch = !disabled && debouncedQuery.length >= 2;
  const search = useDirectorySubjectSearch(definitionId, debouncedQuery, kind, canSearch);
  const resolution = useDirectorySubjectResolutions(
    definitionId,
    selected.map((entry) => entry.subject),
    !disabled,
  );
  const items = search.data?.items ?? [];
  const displayedSelected = selected.map((entry) => {
    const resolved = resolution.data.find(
      (candidate) =>
        candidate.subject.kind === entry.subject.kind && candidate.subject.id === entry.subject.id,
    );
    return resolved ? { ...resolved, available: true } : entry;
  });
  const searching = search.isPending || search.isFetching;
  const hasSearch = debouncedQuery.length >= 2;
  const duplicate = (subject: SubjectRefDto) =>
    selected.some((entry) => entry.subject.kind === subject.kind && entry.subject.id === subject.id);

  function select(item: DirectorySubjectDto) {
    const next: DirectorySubjectSelection = {
      subject: item.subject,
      displayName: item.displayName,
      detail: item.detail,
      available: true,
    };

    if (duplicate(item.subject)) {
      setQuery('');
      return;
    }

    onChange(multiple ? [...selected, next] : [next]);
    setQuery('');
  }

  function remove(subject: SubjectRefDto) {
    onChange(
      selected.filter(
        (entry) => entry.subject.kind !== subject.kind || entry.subject.id !== subject.id,
      ),
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <FieldLabel htmlFor={fieldId}>{label}</FieldLabel>

      <div className="flex flex-wrap gap-1.5" aria-label={`${label} ausgewählt`}>
        {displayedSelected.map((entry) => (
          <span
            key={`${entry.subject.kind}:${entry.subject.id}`}
            className={cn(
              'inline-flex max-w-full items-center gap-1.5 rounded-full px-2.5 py-1 text-[11.5px] font-semibold',
              entry.available ? 'bg-accent/15 text-accent' : 'bg-fail/10 text-fail',
            )}
            title={entry.available ? entry.detail : 'Diese Referenz ist im aktiven Verzeichnis nicht verfügbar.'}
          >
            {!entry.available && <Icon name="warning" size={14} />}
            <span className="min-w-0 truncate">
              {entry.displayName || entry.subject.id}
              {entry.detail && <span className="text-[10.5px] font-normal opacity-75"> · {entry.detail}</span>}
            </span>
            <button
              type="button"
              disabled={disabled}
              aria-label={`${entry.displayName || entry.subject.id} entfernen`}
              onClick={() => remove(entry.subject)}
              className="hover:text-text grid h-4 w-4 flex-none cursor-pointer place-items-center rounded-full border-none bg-transparent p-0 disabled:cursor-not-allowed"
            >
              <Icon name="close" size={13} />
            </button>
          </span>
        ))}
      </div>

      {!disabled && (multiple || selected.length === 0) && (
        <SearchInput
          id={fieldId}
          value={query}
          disabled={disabled}
          placeholder={kind === 'user' ? 'Benutzer suchen …' : 'Gruppe suchen …'}
          aria-label={`${label} suchen`}
          onChange={(event) => setQuery(event.target.value)}
        />
      )}

      {disabled && selected.length === 0 && (
        <p className="text-faint m-0 text-[12px]">Keine Referenz ausgewählt.</p>
      )}

      {!disabled && query.trim().length > 0 && query.trim().length < 2 && (
        <p className="text-faint m-0 text-[11.5px]">Mindestens zwei Zeichen eingeben.</p>
      )}

      {!disabled && hasSearch && searching && (
        <p className="text-muted m-0 flex items-center gap-1.5 text-[12px]" role="status">
          <Icon name="progress_activity" size={15} className="animate-spin" />
          Suche läuft …
        </p>
      )}

      {!disabled && hasSearch && !searching && search.error && (
        <p className="text-fail bg-fail/10 m-0 rounded-[var(--r-sm)] px-2.5 py-2 text-[12px]" role="alert">
          Die Directory-Suche ist derzeit nicht verfügbar.
        </p>
      )}

      {!disabled && hasSearch && !searching && !search.error && search.data !== undefined && items.length === 0 && (
        <p className="text-faint m-0 text-[12px]">Keine passenden aktiven Einträge gefunden.</p>
      )}

      {!disabled && hasSearch && !searching && items.length > 0 && (
        <div
          role="listbox"
          aria-label={`${label} Suchergebnisse`}
          className="border-border bg-surface-2 max-h-48 overflow-y-auto rounded-[var(--r-sm)] border p-1"
        >
          {items.map((item) => {
            const isSelected = duplicate(item.subject);
            return (
              <button
                key={`${item.subject.kind}:${item.subject.id}`}
                type="button"
                role="option"
                aria-selected={isSelected}
                disabled={isSelected}
                onClick={() => select(item)}
                className={cn(
                  'text-text hover:bg-surface flex w-full cursor-pointer flex-col items-start rounded-md border-none bg-transparent px-2.5 py-2 text-left',
                  'disabled:cursor-default disabled:opacity-50',
                )}
              >
                <span className="text-[12.5px] font-semibold">{item.displayName}</span>
                {item.detail && <span className="text-faint text-[11px]">{item.detail}</span>}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
