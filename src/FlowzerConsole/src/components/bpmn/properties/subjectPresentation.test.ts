import { expect, it } from 'vitest';
import { presentSubject } from './subjectPresentation';

const user = { subject: { kind: 'user' as const, id: 'u1' }, displayName: 'Anna Beispiel', detail: 'opaque-subject',
  email: 'anna@example.test', username: 'anna', firstName: 'Anna', lastName: 'Beispiel' };
// Testzweck: Nur angeklickte Standardinformationen erscheinen; Identität/Wert bleiben unverändert.
it('zeigt Name und E-Mail statt technischer Subject-Kennung', () => {
  expect(presentSubject(user, { name: true, email: true })).toMatchObject({ displayName: 'Anna Beispiel', detail: 'anna@example.test', subject: user.subject });
  expect(presentSubject(user, { username: true, firstName: true })).toMatchObject({ displayName: 'anna', detail: 'Anna' });
});
// Testzweck: Fehlende Angaben und gleichnamige Gruppen bleiben verständlich darstellbar.
it('lässt fehlende Felder weg und erhält Gruppenpfade', () => {
  expect(presentSubject({ ...user, email: null }, { name: true, email: true }).detail).toBe('');
  expect(presentSubject({ ...user, subject: { kind: 'group', id: 'g1' }, detail: '/Nord/Team' }, { email: true }).detail).toBe('/Nord/Team');
  expect(presentSubject(user, {}).displayName).toBe('Anna Beispiel');
});
