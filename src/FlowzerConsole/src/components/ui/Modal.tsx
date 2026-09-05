import * as Dialog from '@radix-ui/react-dialog';
import type { ReactNode } from 'react';

import { cn } from '@/lib/cn';

import { Button } from './Button';
import { Icon } from './Icon';

interface ModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: ReactNode;
  icon?: string;
  /** Der eigentliche Inhalt — bei Formularen ein `<form>` mit `id`, siehe `formId`. */
  children?: ReactNode;
  footer?: ReactNode;
  className?: string;
}

/**
 * Mittiger Dialog im Stil der Konsole.
 *
 * Bewusst schmal und mit eigener Bildlaufflaeche: Ein Dialog, der mit seinem Inhalt
 * waechst, schiebt seine Schaltflaechen aus dem Bild — dann ist er nicht mehr zu
 * bedienen, ohne die Seite dahinter zu scrollen.
 */
export function Modal({
  open,
  onOpenChange,
  title,
  description,
  icon,
  children,
  footer,
  className,
}: ModalProps) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 z-[70] bg-black/45 backdrop-blur-[2px]" />
        <Dialog.Content
          // Radix verknuepft Titel und Beschreibung selbst und warnt, wenn eine Beschreibung
          // fehlt. Gibt es keine, wird die Verknuepfung hier ausdruecklich entfernt — ein
          // leerer Wert waere eine Verknuepfung ins Nichts.
          {...(description ? {} : { 'aria-describedby': undefined })}
          className={cn(
            'bg-surface border-border shadow-pop animate-fade-in-fast fixed top-1/2 left-1/2 z-[71]',
            'flex max-h-[85vh] w-[min(520px,calc(100vw-32px))] -translate-x-1/2 -translate-y-1/2',
            'flex-col rounded-[var(--r-lg)] border',
            className,
          )}
        >
          <div className="flex items-start gap-3 px-[22px] pt-[20px] pb-3.5">
            {icon && (
              <span className="bg-surface-2 text-accent mt-0.5 grid h-9 w-9 flex-none place-items-center rounded-[10px]">
                <Icon name={icon} size={20} />
              </span>
            )}
            <div className="min-w-0 flex-1">
              <Dialog.Title className="font-display m-0 text-[17px] font-semibold">{title}</Dialog.Title>
              {description && (
                <Dialog.Description className="text-muted mt-1.5 text-[13.5px] leading-normal">
                  {description}
                </Dialog.Description>
              )}
            </div>
            <Dialog.Close asChild>
              <button
                type="button"
                aria-label="Schließen"
                className="text-faint hover:text-text hover:bg-surface-2 -mt-1 -mr-1.5 grid h-8 w-8 flex-none cursor-pointer place-items-center rounded-md border-none bg-transparent"
              >
                <Icon name="close" size={18} />
              </button>
            </Dialog.Close>
          </div>

          {children && <div className="min-h-0 flex-1 overflow-auto px-[22px] pb-1">{children}</div>}

          {footer && (
            <div className="border-border flex items-center justify-end gap-2 border-t px-[22px] py-3.5">
              {footer}
            </div>
          )}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}

interface ConfirmModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description?: ReactNode;
  /** Beschriftung der bestaetigenden Schaltflaeche, z. B. „Löschen“. */
  confirmLabel: string;
  confirmIcon?: string;
  /** Zerstoererische Aktionen bekommen die Warnfarbe. */
  destructive?: boolean;
  busy?: boolean;
  onConfirm: () => void;
  children?: ReactNode;
}

/** Rueckfrage vor einer Aktion, die sich nicht zuruecknehmen laesst. */
export function ConfirmModal({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  confirmIcon,
  destructive = false,
  busy = false,
  onConfirm,
  children,
}: ConfirmModalProps) {
  return (
    <Modal
      open={open}
      onOpenChange={onOpenChange}
      title={title}
      description={description}
      icon={destructive ? 'warning' : 'help'}
      footer={
        <>
          <Button size="sm" onClick={() => onOpenChange(false)} disabled={busy}>
            Abbrechen
          </Button>
          <Button
            size="sm"
            variant={destructive ? 'danger' : 'primary'}
            icon={confirmIcon}
            loading={busy}
            className={destructive ? 'border-fail text-fail hover:bg-fail/10' : undefined}
            onClick={onConfirm}
          >
            {confirmLabel}
          </Button>
        </>
      }
    >
      {children}
    </Modal>
  );
}
