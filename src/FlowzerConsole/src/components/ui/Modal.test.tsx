import { fireEvent, render, screen } from '@testing-library/react';
import { useEffect, useRef, useState } from 'react';
import { describe, expect, it, vi } from 'vitest';

import { Modal } from './Modal';

/**
 * Ein Datumsfeld, wie flatpickr es hinterlässt: die Instanz als `_flatpickr` am Eingabefeld,
 * der Kalender als `.flatpickr-calendar`, geöffnet mit der Klasse `open`.
 */
function FakeDateField({ onClose }: { onClose: () => void }) {
  const inputRef = useRef<HTMLInputElement>(null);
  const calendarRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const input = inputRef.current as HTMLInputElement & { _flatpickr?: unknown };
    const calendar = calendarRef.current as HTMLDivElement;
    input._flatpickr = {
      get isOpen() {
        return calendar.classList.contains('open');
      },
      close: () => {
        calendar.classList.remove('open');
        onClose();
      },
      _input: input,
      calendarContainer: calendar,
    };
  }, [onClose]);

  return (
    <>
      <input ref={inputRef} aria-label="Letzter Urlaubstag" />
      <div ref={calendarRef} className="flatpickr-calendar open" />
    </>
  );
}

function StartDialog({ onClose }: { onClose: () => void }) {
  const [open, setOpen] = useState(true);
  return (
    <Modal open={open} onOpenChange={setOpen} title="Urlaubsantrag starten">
      <FakeDateField onClose={onClose} />
    </Modal>
  );
}

describe('Modal', () => {
  // Testzweck: Das Dialogelement trägt das Kennzeichen, an dem Form.io-Datumsfelder erkennen,
  // dass sie ihren Kalender im Dialog statt am <body> brauchen.
  it('kennzeichnet das Dialogelement für Datumsfelder', () => {
    render(<Modal open onOpenChange={vi.fn()} title="Titel">Inhalt</Modal>);

    expect(screen.getByRole('dialog')).toHaveAttribute('data-flowzer-modal');
  });

  // Testzweck: Escape bei offenem Datumskalender schließt nur den Kalender; der Dialog bleibt
  // samt Eingaben offen. Erst das nächste Escape schließt den Dialog (Issue #370).
  it('schließt mit Escape zuerst den offenen Kalender, dann den Dialog', () => {
    const onCalendarClose = vi.fn();
    render(<StartDialog onClose={onCalendarClose} />);
    const field = screen.getByRole('textbox', { name: 'Letzter Urlaubstag' });
    field.focus();

    fireEvent.keyDown(field, { key: 'Escape' });

    expect(onCalendarClose).toHaveBeenCalledOnce();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(document.querySelector('.flatpickr-calendar.open')).toBeNull();

    fireEvent.keyDown(field, { key: 'Escape' });

    expect(screen.queryByRole('dialog')).toBeNull();
    expect(onCalendarClose).toHaveBeenCalledOnce();
  });
});
