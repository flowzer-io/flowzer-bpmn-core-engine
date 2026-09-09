import { useCallback, useEffect, useState } from 'react';

import type { ProcessVariables } from '@flowzer/sdk';

export interface TaskFormDataState {
  data: ProcessVariables;
  isDirty: boolean;
  setData: (data: ProcessVariables) => void;
  resetToServer: () => void;
}

interface StoredFormData {
  userTaskId: string;
  initialized: boolean;
  data: ProcessVariables;
  isDirty: boolean;
}

function fromServer(userTaskId: string, data: ProcessVariables | undefined): StoredFormData {
  return {
    userTaskId,
    initialized: data !== undefined,
    data: data === undefined ? {} : { ...data },
    isDirty: false,
  };
}

/**
 * Bewahrt lokale Formulardaten über Query-Refetches. Neue Serverwerte werden erst nach
 * einem Taskwechsel oder einem ausdrücklichen Reset übernommen, nie über lokale Eingaben.
 */
export function useTaskFormData(
  userTaskId: string,
  serverData: ProcessVariables | undefined,
): TaskFormDataState {
  const [stored, setStored] = useState<StoredFormData>(() => fromServer(userTaskId, serverData));
  const current = stored.userTaskId === userTaskId ? stored : fromServer(userTaskId, serverData);

  useEffect(() => {
    setStored((previous) => {
      if (previous.userTaskId !== userTaskId) return fromServer(userTaskId, serverData);
      if (!previous.initialized && serverData !== undefined) return fromServer(userTaskId, serverData);
      return previous;
    });
  }, [serverData, userTaskId]);

  const setData = useCallback((data: ProcessVariables) => {
    setStored({ userTaskId, initialized: true, data: { ...data }, isDirty: true });
  }, [userTaskId]);

  const resetToServer = useCallback(() => {
    setStored(fromServer(userTaskId, serverData));
  }, [serverData, userTaskId]);

  return { data: current.data, isDirty: current.isDirty, setData, resetToServer };
}
