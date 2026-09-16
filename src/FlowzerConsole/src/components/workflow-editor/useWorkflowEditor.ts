import { createContext, useContext } from 'react';
import type { WorkflowDraft } from './WorkflowDraft';

export type CaptureEditor = () => string | Promise<string>;
export interface WorkflowEditorContextValue {
  draft: WorkflowDraft;
  changed: () => void;
  saved: (xml: string, baseId: string, revision: number) => void;
  registerCapture: (capture: CaptureEditor) => () => void;
  allowDiscard: () => void;
  runSave: (action: () => Promise<void>) => Promise<void>;
}
export const WorkflowEditorContext = createContext<WorkflowEditorContextValue | null>(null);
export function useWorkflowEditor() {
  const editor = useContext(WorkflowEditorContext);
  if (!editor) throw new Error('Workflow editor requires a shared editing session.');
  return editor;
}
