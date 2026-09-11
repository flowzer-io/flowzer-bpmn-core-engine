/** Form.io hasEditTabs benötigt auch für kleine eigene Dialoge einen tabs-Knoten. */
export function componentEditForm(fields: Array<Record<string, unknown>>) {
  return {
    display: 'form',
    components: [{
      type: 'tabs', key: 'tabs',
      components: [{ label: 'Einstellungen', key: 'settings', components: fields }],
    }],
  };
}
