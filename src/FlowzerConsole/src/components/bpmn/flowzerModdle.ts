/**
 * Versionierter Flowzer-Moddle-Vertrag.
 *
 * Die Namen muessen exakt mit `ModelParser.ParseUserTaskAssignment` uebereinstimmen. Der
 * Descriptor liegt im eigenen Produkt-Namespace, damit bpmn-js die typisierten Referenzen
 * nicht als unbekanntes XML behandelt oder beim Speichern verliert.
 */
export const FLOWZER_MODDLE = {
  name: 'Flowzer',
  prefix: 'flowzer',
  uri: 'https://flowzer.io/schema/bpmn/1.0',
  xml: { tagAlias: 'lowerCase' },
  associations: [],
  types: [
    {
      name: 'TaskAssignment',
      superClass: ['Element'],
      properties: [
        { name: 'mode', isAttr: true, type: 'String' },
        { name: 'assigneeId', isAttr: true, type: 'String' },
        { name: 'candidateUserIds', isAttr: true, type: 'String' },
        { name: 'candidateGroupIds', isAttr: true, type: 'String' },
      ],
    },
    {
      name: 'AiTask',
      superClass: ['Element'],
      properties: [
        { name: 'contractVersion', isAttr: true, type: 'String' },
        { name: 'connectionId', isAttr: true, type: 'String' },
        { name: 'model', isAttr: true, type: 'String' },
        { name: 'instructionVersion', isAttr: true, type: 'String' },
        { name: 'maxInputTokens', isAttr: true, type: 'String' },
        { name: 'maxOutputTokens', isAttr: true, type: 'String' },
        { name: 'timeoutSeconds', isAttr: true, type: 'String' },
        { name: 'instruction', type: 'Instruction' },
        { name: 'resultSchema', type: 'ResultSchema' },
      ],
    },
    {
      name: 'Instruction',
      superClass: ['Element'],
      properties: [{ name: 'body', isBody: true, type: 'String' }],
    },
    {
      name: 'ResultSchema',
      superClass: ['Element'],
      properties: [{ name: 'body', isBody: true, type: 'String' }],
    },
  ],
} as const;
