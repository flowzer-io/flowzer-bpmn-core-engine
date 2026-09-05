import * as Popover from '@radix-ui/react-popover';
import { useNavigate } from '@tanstack/react-router';
import { useEffect, useState } from 'react';

import { Dot } from '@/components/ui/Chip';
import { Icon } from '@/components/ui/Icon';
import { useActivityFeed } from '@/lib/activity';

export function NotificationsMenu() {
  const activity = useActivityFeed();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [seenCount, setSeenCount] = useState(0);

  // Solange das Menü offen ist, gelten alle sichtbaren Meldungen als gelesen.
  useEffect(() => {
    if (open) setSeenCount(activity.length);
  }, [open, activity.length]);

  const unread = Math.max(0, activity.length - seenCount);

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger asChild>
        <button
          type="button"
          title="Benachrichtigungen"
          aria-label={`Benachrichtigungen${unread > 0 ? ` (${unread} neu)` : ''}`}
          className="text-muted hover:bg-surface-2 relative grid h-[38px] w-[38px] cursor-pointer place-items-center rounded-[var(--r-sm)] border-none bg-transparent"
        >
          <Icon name="notifications" size={21} />
          {unread > 0 && (
            <span className="bg-fail border-surface absolute top-2 right-[9px] h-[7px] w-[7px] rounded-full border-2" />
          )}
        </button>
      </Popover.Trigger>

      <Popover.Portal>
        <Popover.Content
          align="end"
          sideOffset={8}
          className="bg-surface border-border shadow-pop animate-fade-in-fast z-[61] w-[344px] overflow-hidden rounded-[var(--r-lg)] border"
        >
          <div className="border-border flex items-center justify-between border-b px-4 py-3">
            <span className="text-sm font-semibold">Benachrichtigungen</span>
            <span className="text-faint font-mono text-[11px]">
              {activity.length === 0 ? 'nichts Neues' : `${activity.length} Ereignisse`}
            </span>
          </div>

          <div className="max-h-[330px] overflow-auto px-2 py-1.5">
            {activity.length === 0 && (
              <div className="text-muted px-3 py-8 text-center text-[13.5px]">
                Zurzeit gibt es nichts zu berichten.
              </div>
            )}

            {activity.map((entry) => (
              <button
                key={entry.id}
                type="button"
                onClick={() => {
                  if (entry.href) void navigate({ to: entry.href });
                  setOpen(false);
                }}
                className="hover:bg-surface-2 flex w-full cursor-pointer gap-2.5 rounded-[var(--r-sm)] border-none bg-transparent px-2 py-2.5 text-left"
              >
                <Dot tone={entry.tone} size={9} halo className="mt-1.5" />
                <span className="min-w-0">
                  <span className="block text-[13px] leading-snug">{entry.text}</span>
                  <span className="text-faint mt-0.5 block font-mono text-[11.5px]">{entry.time}</span>
                </span>
              </button>
            ))}
          </div>
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  );
}
