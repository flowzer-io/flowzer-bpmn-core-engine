import * as Dialog from '@radix-ui/react-dialog';
import { useNavigate } from '@tanstack/react-router';
import { Command } from 'cmdk';
import { useMemo } from 'react';

import { Icon } from '@/components/ui/Icon';
import { useDefinitions, useInstances, useUserTasks } from '@/lib/api/queries';
import { shortId } from '@/lib/format';
import { useAppearance } from '@/stores/appearance';

import { NAV_ITEMS } from './navigation';

interface CommandPaletteProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

interface Entry {
  id: string;
  group: string;
  icon: string;
  label: string;
  hint: string;
  run: () => void;
}

const MAX_PER_GROUP = 6;

/**
 * Befehlspalette (⌘K). Sie durchsucht Navigation, Workflows, Instanzen und
 * offene Aufgaben in einem Feld — die Suchleiste in der Kopfzeile öffnet sie.
 */
export function CommandPalette({ open, onOpenChange }: CommandPaletteProps) {
  const navigate = useNavigate();
  const toggleTheme = useAppearance((state) => state.toggleTheme);

  // Nur laden, während die Palette offen ist — sonst hält sie unnötig Abfragen wach.
  const definitionsQuery = useDefinitions({ enabled: open });
  const instancesQuery = useInstances({ enabled: open });
  const userTasksQuery = useUserTasks({ enabled: open });

  const definitions = definitionsQuery.data;
  const instances = instancesQuery.data;
  const userTasks = userTasksQuery.data;

  const entries = useMemo<Entry[]>(() => {
    const go = (to: string) => () => {
      void navigate({ to });
      onOpenChange(false);
    };

    return [
      ...NAV_ITEMS.map((item) => ({
        id: `nav-${item.key}`,
        group: 'Navigation',
        icon: item.icon,
        label: item.label,
        hint: 'Seite',
        run: go(item.path),
      })),
      {
        id: 'action-theme',
        group: 'Aktionen',
        icon: 'contrast',
        label: 'Hell/Dunkel umschalten',
        hint: 'Ansicht',
        run: () => {
          toggleTheme();
          onOpenChange(false);
        },
      },
      {
        id: 'action-tasks',
        group: 'Aktionen',
        icon: 'inbox',
        label: 'Meine Aufgaben öffnen',
        hint: 'Aufgaben',
        run: go('/tasks'),
      },
      ...(definitions ?? []).slice(0, MAX_PER_GROUP).map((definition) => ({
        id: `wf-${definition.definitionId}`,
        group: 'Workflows',
        icon: 'schema',
        label: definition.name,
        hint: definition.deployedVersion
          ? `v${definition.deployedVersion.major}.${definition.deployedVersion.minor}`
          : 'Entwurf',
        run: go(`/workflows/${encodeURIComponent(definition.definitionId)}`),
      })),
      ...(instances ?? []).slice(0, MAX_PER_GROUP).map((instance) => ({
        id: `inst-${instance.instanceId}`,
        group: 'Instanzen',
        icon: 'play_circle',
        label: instance.relatedDefinitionName,
        hint: `#${shortId(instance.instanceId)}`,
        run: go(`/instances/${instance.instanceId}`),
      })),
      ...(userTasks ?? []).slice(0, MAX_PER_GROUP).map((task) => ({
        id: `task-${task.id}`,
        group: 'Aufgaben',
        icon: 'assignment',
        label: task.name,
        hint: task.definitionMetaName,
        run: go(`/tasks?task=${task.id}`),
      })),
    ];
  }, [definitions, instances, userTasks, navigate, onOpenChange, toggleTheme]);

  const groups = useMemo(() => {
    const byGroup = new Map<string, Entry[]>();
    for (const entry of entries) {
      const existing = byGroup.get(entry.group);
      if (existing) existing.push(entry);
      else byGroup.set(entry.group, [entry]);
    }
    return [...byGroup.entries()];
  }, [entries]);

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay
          className="fixed inset-0 z-[70]"
          style={{ background: 'color-mix(in oklab, #0b0b12 42%, transparent)' }}
        />
        <Dialog.Content
          aria-describedby={undefined}
          className="bg-surface border-border shadow-pop animate-fade-in-fast fixed top-[13vh] left-1/2 z-[71] w-[min(580px,92vw)] -translate-x-1/2 overflow-hidden rounded-[var(--r-lg)] border"
        >
          <Dialog.Title className="sr-only">Befehlspalette</Dialog.Title>

          <Command loop label="Befehlspalette">
            <div className="border-border flex items-center gap-2.5 border-b px-4 py-3.5">
              <Icon name="search" size={20} className="text-muted" />
              <Command.Input
                autoFocus
                placeholder="Springe zu Seite, Workflow, Instanz oder Aufgabe …"
                className="text-text min-w-0 flex-1 border-none bg-transparent text-[15px] outline-none"
              />
              <span className="border-border text-faint shrink-0 rounded-[5px] border px-1.5 font-mono text-[11px]">
                ESC
              </span>
            </div>

            <Command.List className="max-h-[min(430px,54vh)] overflow-auto px-2 py-1.5">
              <Command.Empty className="text-muted px-3.5 py-8 text-center text-[13.5px]">
                Nichts gefunden.
              </Command.Empty>

              {groups.map(([group, groupEntries]) => (
                <Command.Group
                  key={group}
                  heading={group}
                  className="[&_[cmdk-group-heading]]:text-faint [&_[cmdk-group-heading]]:px-3 [&_[cmdk-group-heading]]:pt-2.5 [&_[cmdk-group-heading]]:pb-1 [&_[cmdk-group-heading]]:font-mono [&_[cmdk-group-heading]]:text-[10px] [&_[cmdk-group-heading]]:tracking-[0.11em] [&_[cmdk-group-heading]]:uppercase"
                >
                  {groupEntries.map((entry) => (
                    <Command.Item
                      key={entry.id}
                      value={`${entry.label} ${entry.hint} ${entry.group}`}
                      onSelect={entry.run}
                      className="text-text data-[selected=true]:bg-surface-2 flex cursor-pointer items-center gap-3 rounded-[var(--r-sm)] px-3 py-2.5"
                    >
                      <Icon name={entry.icon} size={19} className="text-muted" />
                      <span className="min-w-0 flex-1 truncate text-[13.5px] font-medium">{entry.label}</span>
                      <span className="text-faint shrink-0 font-mono text-[11px]">{entry.hint}</span>
                    </Command.Item>
                  ))}
                </Command.Group>
              ))}
            </Command.List>
          </Command>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
