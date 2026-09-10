import { createContext, useContext, type PropsWithChildren } from 'react';

import type { FlowzerClient } from '@flowzer/sdk';

export interface FlowzerProviderProps extends PropsWithChildren {
  client: FlowzerClient;
  /** Stabile, nicht geheime Kennung der Flowzer-Installation für Query-Keys. */
  cacheNamespace: string;
  /** Pro Anmeldung wechselnder, nicht geheimer Scope; niemals Token oder E-Mail verwenden. */
  sessionScope: string;
}

export interface FlowzerReactContext {
  client: FlowzerClient;
  cacheNamespace: string;
  sessionScope: string;
}

const Context = createContext<FlowzerReactContext | null>(null);

/** Bindet einen expliziten Client an genau eine Installations-/Sitzungskombination. */
export function FlowzerProvider({
  client,
  cacheNamespace,
  sessionScope,
  children,
}: FlowzerProviderProps) {
  if (cacheNamespace.trim().length === 0 || sessionScope.trim().length === 0) {
    throw new TypeError('FlowzerProvider requires non-empty cacheNamespace and sessionScope values.');
  }

  return (
    // Die Scope-Grenze gilt auch für lokalen React-State (Eingaben, Mutationen), nicht
    // nur für Query-Keys. Ein Scopewechsel verwirft deshalb den gesamten alten Teilbaum.
    <Context.Provider key={JSON.stringify([cacheNamespace, sessionScope])}
      value={{ client, cacheNamespace, sessionScope }}>
      {children}
    </Context.Provider>
  );
}

export function useFlowzer(): FlowzerReactContext {
  const value = useContext(Context);
  if (!value) throw new Error('Flowzer hooks must be used inside a FlowzerProvider.');
  return value;
}
