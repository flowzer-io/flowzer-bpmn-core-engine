/** Reservierter Worker-Typ der ersten versionierten Flowzer-KI-Aufgabe. */
export const AI_WORKER_TYPE = 'flowzer.ai.v1';

export type AiToolApprovalMode = 'automatic' | 'human' | 'preApproved';

export interface AiTaskToolConfiguration {
  toolId: string;
  toolVersion: string;
  approval: AiToolApprovalMode;
}

/** Werte des exportierbaren KI-Vertrags; Secrets sind ausdrücklich nicht Bestandteil davon. */
export interface AiTaskConfiguration {
  contractVersion: string;
  connectionId: string;
  model: string;
  instructionVersion: string;
  instruction: string;
  resultSchema: string;
  maxInputTokens: string;
  maxOutputTokens: string;
  timeoutSeconds: string;
  tools: AiTaskToolConfiguration[];
}

/** Sichere, begrenzte Ausgangswerte einer neu angelegten KI-Aufgabe. */
export const DEFAULT_AI_TASK: AiTaskConfiguration = {
  contractVersion: '1',
  connectionId: '',
  model: '',
  instructionVersion: '1',
  instruction: '',
  resultSchema: '{"type":"object","properties":{},"additionalProperties":false}',
  maxInputTokens: '4096',
  maxOutputTokens: '1024',
  timeoutSeconds: '60',
  tools: [],
};

export type ServiceTaskMode = 'worker' | 'ai';
