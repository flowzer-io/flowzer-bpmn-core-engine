// @vitest-environment jsdom
import DOMPurify from 'dompurify'
import moment from 'moment'
import { expect, it, vi } from 'vitest'
import lock from '../../../package-lock.json'

// Testzweck: Der reproduzierbare Lockbaum darf die konkret bestätigten
// Advisory-Versionen nicht wieder einführen, auch nicht als verschachteltes Paket.
it.each([['dompurify', [3, 4, 16]], ['moment', [2, 31, 0]]] as const)('hält %s mindestens auf dem geprüften Patchstand', (name, minimum) => {
  const packages = Object.entries(lock.packages).filter(([path]) => path.endsWith(`/node_modules/${name}`) || path === `node_modules/${name}`)
  expect(packages.length).toBeGreaterThan(0)
  for (const [, entry] of packages) {
    const version = 'version' in entry ? entry.version : ''
    expect(version).toMatch(/^\d+\.\d+\.\d+$/)
    const parts = version.split('.').map(Number)
    const firstDifference = parts.findIndex((part, index) => part !== minimum[index])
    expect(firstDifference < 0 || parts[firstDifference]! > minimum[firstDifference]!).toBe(true)
  }
})

// Testzweck: GHSA-p98j-92pf-mc4p – ein entfernender After-Hook darf im
// IN_PLACE-Pfad keine Eventattribute am abgetrennten lebenden Teilbaum erhalten.
// Nur DOM-Attribute prüfen: kein Event ausführen und kein Netzwerk/HTML-Script laden.
it('neutralisiert Ereignisattribute auch nach Entfernung durch After-Hook', () => {
  const root = document.createElement('div')
  root.innerHTML = '<section id="dependency-hook"><img onerror="void 0"></section>'
  const image = root.querySelector('img')!
  DOMPurify.addHook('afterSanitizeElements', node => { if (node instanceof Element && node.id === 'dependency-hook') node.remove() })
  try { DOMPurify.sanitize(root, { IN_PLACE: true }); expect(image.hasAttribute('onerror')).toBe(false) }
  finally { DOMPurify.removeAllHooks() }
})

// Testzweck: GHSA-4p3w-j4w9-5jqw – die Modulnamensprüfung darf keine vom
// Objekt gelieferte match-Funktion verwenden. Der Patch normalisiert zuvor zu
// einem sicheren String; Normalisierung selbst ist weiterhin erlaubte API.
// match liefert null, "en" ist bereits geladen: kein fremdes Modul laden.
it('validiert Modulnamen nicht durch eine vom Aufrufer gelieferte match-Funktion', () => {
  const match = vi.fn(() => null)
  const previous = moment.locale()
  try { moment.locale({ match, toLowerCase: () => 'en' } as unknown as string); expect(match).not.toHaveBeenCalled() }
  finally { moment.locale(previous) }
})
