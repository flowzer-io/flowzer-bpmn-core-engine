import { FlowzerApiError } from '@flowzer/sdk';
import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ApiError } from '@/lib/api/client';
import { FormValidationErrors } from './FormValidationErrors';

const schema = JSON.stringify({ components: [{ type: 'textfield', key: 'reason', label: 'Begründung' }] });

describe('Serverseitige Formularfehler', () => {
  // Testzweck: Die serverseitige Ablehnung nennt das betroffene Feld verständlich und
  // ist per Screenreader/Fokus erreichbar; Eingaben werden nicht erneut gerendert.
  it('zeigt Feldlabel und übersetzten Fehlercode als fokussierte Meldung', () => {
    const error = new ApiError('Invalid input', { status: 422, url: '/usertask', body: { errors: { reason: ['required'] } } });
    render(<FormValidationErrors error={error} schema={schema} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Begründung');
    expect(screen.getByRole('alert')).toHaveTextContent('Dieses Feld ist erforderlich.');
    expect(screen.getByRole('alert')).toHaveFocus();
  });

  // Testzweck: Nach der Migration der Human Tasks auf das öffentliche SDK bleiben
  // dessen Problem-Details gleichwertig zur älteren Console-Fehlerklasse sichtbar.
  it('zeigt Feldfehler des öffentlichen SDKs an', () => {
    const error = new FlowzerApiError('Invalid input', {
      status: 422,
      url: '/usertask',
      body: { errors: { reason: ['required'] } },
    });

    render(<FormValidationErrors error={error} schema={schema} />);

    expect(screen.getByRole('alert')).toHaveTextContent('Begründung');
    expect(screen.getByRole('alert')).toHaveTextContent('Dieses Feld ist erforderlich.');
  });

  // Testzweck: Unbekannte/malforme Fehlerantworten und fehlende Schemata verursachen
  // keine Renderfehler, keine HTML-Ausführung und keine Darstellung von Rohwerten.
  it('verwendet für unbekannte Codes eine sichere allgemeine Meldung', () => {
    const error = new ApiError('Invalid input', { status: 422, url: '/usertask', body: { errors: { reason: ['DO_NOT_ECHO'] } } });
    render(<FormValidationErrors error={error} schema="{broken" />);
    expect(screen.getByRole('alert')).toHaveTextContent('reason');
    expect(screen.queryByText('DO_NOT_ECHO')).not.toBeInTheDocument();
  });

  // Testzweck: Directory-Auswahlen liefern eigene, verständliche Vertragsfehler, ohne
  // manipulierte IDs oder andere Eingabewerte in die Fehlermeldung zu spiegeln.
  it('übersetzt SubjectRef- und Directory-Fehler', () => {
    const error = new ApiError('Invalid input', {
      status: 422,
      url: '/start-form',
      body: { errors: { approvers: ['type.subject_ref', 'selection.duplicate', 'directory.unavailable'] } },
    });
    render(<FormValidationErrors error={error} />);
    expect(screen.getByRole('alert')).toHaveTextContent('gültige Benutzer- oder Gruppenreferenz');
    expect(screen.getByRole('alert')).toHaveTextContent('nur einmal');
    expect(screen.getByRole('alert')).toHaveTextContent('nicht mehr aktiv verfügbar');
    expect(screen.queryByText(/user-|group-/)).not.toBeInTheDocument();
  });

  // Testzweck: Generische Transportfehler werden weiterhin vom Aufrufer behandelt,
  // nicht als scheinbare Feldfehler ausgegeben.
  it('bleibt ohne feldbezogene Problem Details leer', () => {
    const { container } = render(<FormValidationErrors error={new Error('Offline')} schema={schema} />);
    expect(container).toBeEmptyDOMElement();
  });

  // Testzweck: Indexierte Serverpfade aus Wiederholgruppen werden als verstaendliche
  // Zeilen-/Feldbezeichnung dargestellt, waehrend der Fehlercode lokal uebersetzt bleibt.
  it('ordnet Wiederholgruppenfehler der sichtbaren Zeile zu', () => {
    const repeatSchema = JSON.stringify({ components: [{
      type: 'datagrid', key: 'positions', label: 'Positionen',
      components: [{ type: 'textfield', key: 'name', label: 'Bezeichnung' }],
    }] });
    const error = new ApiError('Invalid input', {
      status: 422,
      url: '/usertask',
      body: { errors: { 'positions[1].name': ['required'], positions: ['repeat.max'] } },
    });

    render(<FormValidationErrors error={error} schema={repeatSchema} />);

    expect(screen.getByRole('alert')).toHaveTextContent('Positionen 2 – Bezeichnung');
    expect(screen.getByRole('alert')).toHaveTextContent('Liste enthält zu viele');
  });
});
