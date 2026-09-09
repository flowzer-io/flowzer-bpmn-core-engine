import { render, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import type { BoundDirectorySubjectAdapter } from '@/components/bpmn/properties/DirectorySubjectPicker';

const createForm = vi.hoisted(() => vi.fn());

vi.mock('@formio/js', () => ({
  Formio: { createForm },
  Widgets: {},
}));
vi.mock('./dialogCalendarWidget', () => ({ registerDialogCalendarWidget: vi.fn() }));
vi.mock('./FlowzerSubjectComponent', () => ({ registerFlowzerSubjectComponent: vi.fn() }));

import { FormRenderer } from './FormRenderer';

describe('FormRenderer', () => {
  beforeEach(() => {
    createForm.mockReset();
    createForm.mockResolvedValue({
      submission: { data: {} },
      on: vi.fn(),
      checkValidity: vi.fn(() => true),
      destroy: vi.fn(),
    });
  });

  // Testzweck: Verschachtelte Form.io-Roots erhalten nur den vom Host gebundenen
  // Directory-Adapter; der Renderer reicht keine Task-ID oder freie API-Hook-Konfiguration
  // in das Drittanbieter-Formular durch.
  it('reicht den gebundenen Directory-Adapter in die Form.io-Optionen', async () => {
    const directoryAdapter: BoundDirectorySubjectAdapter = {
      cacheKey: ['task-1', 'field-1'],
      search: vi.fn(),
      resolve: vi.fn(),
    };

    render(<FormRenderer schema='{"components":[]}' directoryAdapter={directoryAdapter} />);

    await waitFor(() => expect(createForm).toHaveBeenCalledOnce());
    expect(createForm.mock.calls[0]?.[2]).toMatchObject({
      flowzerDirectoryAdapter: directoryAdapter,
      flowzerDirectoryContext: undefined,
    });
  });
});
