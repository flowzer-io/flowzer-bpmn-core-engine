import { useEffect, useId, useState } from 'react';

import {
  useDirectorySubjectResolutions,
  useDirectorySubjectSearch,
  useFolderDirectorySubjectResolutions,
  useFolderDirectorySubjectSearch,
  useFormDirectorySubjectResolutions,
  useFormDirectorySubjectSearch,
} from '@/lib/api/queries';
import type {
  DirectorySubjectDto,
  DirectorySubjectSearchResultDto,
  FormDirectorySearchContext,
  SubjectRefDto,
} from '@/lib/api/types';

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

/**
 * Hostgebundene Directory-Suche für ein konkretes Formularfeld oder eine konkrete
 * Lifecycle-Aktion. Der Picker erhält weder Task-ID noch Feld/Aktion und kann den
 * serverseitig gebundenen Kontext daher nicht erweitern.
 */
export interface BoundDirectorySubjectAdapter {
  /** Stabile, nicht fachliche Cache-/Instanzdomäne des Hosts. */
  cacheKey: readonly unknown[];
  /**
   * Der Host bindet hier bereits Task plus Feld beziehungsweise Aktion. Der
   * verschachtelte React-Root erhält deshalb nur Suchparameter und kann den
   * serverseitig erlaubten Kontext nicht erweitern.
   */
  search: (
    fieldKey: string,
    options: { query: string; kind: SubjectRefDto['kind'] | 'all'; signal?: AbortSignal },
  ) => Promise<DirectorySubjectSearchResultDto>;
  resolve: (
    fieldKey: string,
    subjects: readonly SubjectRefDto[],
    signal?: AbortSignal,
  ) => Promise<DirectorySubjectDto[]>;
}

export interface DirectorySubjectPickerProps {
  definitionId: string;
  /** Für Ordnerdelegationen wird statt des Workflowpfads dieser Kontext verwendet. */
  folderId?: string;
  kind: SubjectRefDto['kind'] | 'all';
  /** Bei Formularen ersetzt dieser Kontext die workflowgebundene Modeler-Suche. */
  directoryContext?: FormDirectorySearchContext;
  /** Technischer Form.io-Key, der serverseitig in die Policy-Prüfung einfließt. */
  fieldKey?: string;
  /** Laufzeitgebundene Auswahl eines tatsächlichen Bearbeiters. */
  directoryAdapter?: BoundDirectorySubjectAdapter;
  disabledReason?: string;
  selected: DirectorySubjectSelection[];
  multiple: boolean;
  disabled?: boolean;
  label: string;
  onChange: (selected: DirectorySubjectSelection[]) => void;
}

interface SearchState {
  data?: DirectorySubjectSearchResultDto;
  isPending: boolean;
  isFetching: boolean;
  error: unknown;
}

interface ResolutionState {
  data: DirectorySubjectDto[];
  isPending: boolean;
  isFetching: boolean;
  error: unknown;
}

/**
 * Workflowgebundene Auswahl für eine stabile Benutzer- oder Gruppenreferenz.
 *
 * Die Komponente kennt bewusst weder BPMN-Moddle noch den Editor. Sie erhält die bereits
 * gespeicherten Referenzen und liefert bei Auswahl nur die vom API-Endpunkt erhaltene
 * Anzeigeprojektion zurück. Dadurch bleiben auch unbekannte historische IDs als Warn-Chips
 * sichtbar, statt bei einer neuen Suche still verloren zu gehen.
 */
function DirectorySubjectPickerView({
  kind,
  disabledReason,
  selected,
  multiple,
  disabled = false,
  label,
  onChange,
  search,
  resolution,
  query,
  setQuery,
  debouncedQuery,
}: DirectorySubjectPickerProps & {
  search: SearchState;
  resolution: ResolutionState;
  query: string;
  setQuery: (value: string) => void;
  debouncedQuery: string;
}) {
  const fieldId = useId();

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
          placeholder={kind === 'user' ? 'Benutzer suchen …' : kind === 'group' ? 'Gruppe suchen …' : 'Benutzer oder Gruppe suchen …'}
          aria-label={`${label} suchen`}
          onChange={(event) => setQuery(event.target.value)}
        />
      )}

      {disabled && selected.length === 0 && (
        <p className="text-faint m-0 text-[12px]">Keine Referenz ausgewählt.</p>
      )}

      {disabledReason && (
        <p className="text-fail bg-fail/10 m-0 rounded-[var(--r-sm)] px-2.5 py-2 text-[12px]" role="alert">
          {disabledReason}
        </p>
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

      {!disabled && hasSearch && !searching && Boolean(search.error) && (
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

/** Gemeinsame Ansicht für Workflow-, Ordner- und Formularsuche; nur der Hook-Kontext unterscheidet sich. */
export function DirectorySubjectPicker(props: DirectorySubjectPickerProps) {
  if (props.folderId) {
    return <FolderDirectorySubjectPicker {...props} />;
  }
  if (props.directoryAdapter) {
    return <BoundDirectorySubjectPicker {...props} />;
  }
  if (props.directoryContext && props.fieldKey) {
    return <FormDirectorySubjectPicker {...props} />;
  }
  return <WorkflowDirectorySubjectPicker {...props} />;
}

/**
 * Adapterpfad für eingebettete Hosts wie Form.io und den Lifecycle-Dialog.
 *
 * Der Host bindet Task plus Feld beziehungsweise Aktion in die Callbacks. Damit
 * kann der verschachtelte React-Root nur noch Suchtext und Auswahlart liefern.
 */
function BoundDirectorySubjectPicker(props: DirectorySubjectPickerProps) {
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  useEffect(() => {
    const timeout = window.setTimeout(() => setDebouncedQuery(query.trim()), 250);
    return () => window.clearTimeout(timeout);
  }, [query]);
  const adapter = props.directoryAdapter!;
  const fieldKey = props.fieldKey ?? '';
  const search = useBoundSearch(adapter, fieldKey, props.kind, debouncedQuery, !props.disabled);
  const subjects = uniqueSubjects(props.selected.map((entry) => entry.subject));
  const resolution = useBoundResolution(adapter, fieldKey, subjects, !props.disabled);
  return (
    <DirectorySubjectPickerView
      {...props}
      search={search}
      resolution={{
        data: resolution.data,
        isPending: resolution.isPending,
        isFetching: resolution.isFetching,
        error: resolution.error,
      }}
      query={query}
      setQuery={setQuery}
      debouncedQuery={debouncedQuery}
    />
  );
}

/**
 * Form.io erzeugt für das Feld einen eigenen React-Root. Deshalb darf dieser
 * Adapterpfad keine Console-Hooks mit React-Query-Kontext voraussetzen, sondern
 * arbeitet ausschließlich mit den vom Host injizierten, bereits gebundenen
 * Promises. Abgebrochene oder überholte Antworten werden verworfen.
 */
function useBoundSearch(
  adapter: BoundDirectorySubjectAdapter,
  fieldKey: string,
  kind: DirectorySubjectPickerProps['kind'],
  query: string,
  enabled: boolean,
): SearchState {
  const [state, setState] = useState<SearchState>({
    data: undefined,
    isPending: false,
    isFetching: false,
    error: null,
  });

  useEffect(() => {
    if (!enabled || query.length < 2) {
      setState({ data: undefined, isPending: false, isFetching: false, error: null });
      return;
    }

    const controller = new AbortController();
    let current = true;
    setState((previous) => ({ ...previous, isPending: previous.data === undefined, isFetching: true, error: null }));
    void adapter.search(fieldKey, { query, kind, signal: controller.signal }).then(
      (data) => {
        if (current) setState({ data, isPending: false, isFetching: false, error: null });
      },
      (error: unknown) => {
        if (current && !controller.signal.aborted) {
          setState((previous) => ({ ...previous, isPending: false, isFetching: false, error }));
        }
      },
    );

    return () => {
      current = false;
      controller.abort();
    };
  }, [adapter, enabled, fieldKey, kind, query]);

  return state;
}

function useBoundResolution(
  adapter: BoundDirectorySubjectAdapter,
  fieldKey: string,
  subjects: SubjectRefDto[],
  enabled: boolean,
): ResolutionState {
  const [state, setState] = useState<ResolutionState>({
    data: [],
    isPending: false,
    isFetching: false,
    error: null,
  });
  const subjectKey = subjects.map((subject) => `${subject.kind}:${subject.id}`).join('|');

  useEffect(() => {
    if (!enabled || subjects.length === 0) {
      setState({ data: [], isPending: false, isFetching: false, error: null });
      return;
    }

    const controller = new AbortController();
    let current = true;
    setState((previous) => ({ ...previous, isPending: previous.data.length === 0, isFetching: true, error: null }));
    void adapter.resolve(fieldKey, subjects, controller.signal).then(
      (data) => {
        if (current) setState({ data, isPending: false, isFetching: false, error: null });
      },
      (error: unknown) => {
        if (current && !controller.signal.aborted) {
          setState((previous) => ({ ...previous, isPending: false, isFetching: false, error }));
        }
      },
    );

    return () => {
      current = false;
      controller.abort();
    };
    // The serialized key tracks the immutable subject references without making
    // the host recreate callbacks solely because Form.io recreated an array.
  // subjectKey is the intentional structural dependency; Form.io frequently
  // recreates the array while keeping the selected references unchanged.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [adapter, enabled, fieldKey, subjectKey]);

  return state;
}

function FolderDirectorySubjectPicker(props: DirectorySubjectPickerProps) {
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  useEffect(() => {
    const timeout = window.setTimeout(() => setDebouncedQuery(query.trim()), 250);
    return () => window.clearTimeout(timeout);
  }, [query]);
  const search = useFolderDirectorySubjectSearch(
    props.folderId!,
    debouncedQuery,
    props.kind,
    !props.disabled && debouncedQuery.length >= 2,
  );
  const resolution = useFolderDirectorySubjectResolutions(
    props.folderId!,
    props.selected.map((entry) => entry.subject),
    props.folderId!.length > 0,
  );
  return (
    <DirectorySubjectPickerView
      {...props}
      search={search}
      resolution={resolution}
      query={query}
      setQuery={setQuery}
      debouncedQuery={debouncedQuery}
    />
  );
}

function WorkflowDirectorySubjectPicker(props: DirectorySubjectPickerProps) {
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  useEffect(() => {
    const timeout = window.setTimeout(() => setDebouncedQuery(query.trim()), 250);
    return () => window.clearTimeout(timeout);
  }, [query]);
  const workflowKind = props.kind === 'all' ? 'user' : props.kind;
  const search = useDirectorySubjectSearch(props.definitionId, debouncedQuery, workflowKind, !props.disabled && debouncedQuery.length >= 2);
  const resolution = useDirectorySubjectResolutions(
    props.definitionId,
    props.selected.map((entry) => entry.subject),
    props.definitionId.length > 0,
  );
  return <DirectorySubjectPickerView {...props} search={search} resolution={resolution} query={query} setQuery={setQuery} debouncedQuery={debouncedQuery} />;
}

function FormDirectorySubjectPicker(props: DirectorySubjectPickerProps) {
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  useEffect(() => {
    const timeout = window.setTimeout(() => setDebouncedQuery(query.trim()), 250);
    return () => window.clearTimeout(timeout);
  }, [query]);
  const context = props.directoryContext!;
  const fieldKey = props.fieldKey!;
  const search = useFormDirectorySubjectSearch(
    context,
    fieldKey,
    debouncedQuery,
    props.kind,
    !props.disabled && debouncedQuery.length >= 2,
  );
  const resolution = useFormDirectorySubjectResolutions(
    context,
    fieldKey,
    props.selected.map((entry) => entry.subject),
    Boolean(context),
  );
  return <DirectorySubjectPickerView {...props} search={search} resolution={resolution} query={query} setQuery={setQuery} debouncedQuery={debouncedQuery} />;
}

function uniqueSubjects(subjects: SubjectRefDto[]): SubjectRefDto[] {
  return subjects.filter(
    (subject, index) => subjects.findIndex(
      (candidate) => candidate.kind === subject.kind && candidate.id === subject.id,
    ) === index,
  );
}
