import {
  AI_PROCESSING_LOCATIONS,
  AI_PROVIDER_KINDS,
  type AiConnectionDto,
  type AiProcessingLocation,
  type AiProviderKind,
  type CreateAiConnectionInput,
  type UpdateAiConnectionInput,
} from './types';

type RawAiConnectionDto = Omit<AiConnectionDto, 'provider' | 'location' | 'allowedTools'> & {
  provider: AiProviderKind | number;
  location: AiProcessingLocation | number;
  allowedTools?: AiConnectionDto['allowedTools'];
};

/** Normalisiert die numerischen .NET-Enums einmalig am API-Rand. */
export function normalizeAiConnection(connection: RawAiConnectionDto): AiConnectionDto {
  return {
    ...connection,
    provider: enumName(connection.provider, AI_PROVIDER_KINDS, 'Provider'),
    location: enumName(connection.location, AI_PROCESSING_LOCATIONS, 'Verarbeitungsort'),
    allowedTools: connection.allowedTools ?? [],
  };
}

/** Der Server erwartet fuer Enums weiterhin deren stabilen numerischen .NET-Wert. */
export function createAiConnectionBody(input: CreateAiConnectionInput): Record<string, unknown> {
  return {
    ...input,
    provider: AI_PROVIDER_KINDS.indexOf(input.provider),
    location: AI_PROCESSING_LOCATIONS.indexOf(input.location),
  };
}

/** Eine leere Secret-Eingabe wird ausgelassen und kann dadurch nichts offenlegen/loeschen. */
export function updateAiConnectionBody(input: UpdateAiConnectionInput): Record<string, unknown> {
  const secretReference = input.secretReference?.trim();
  return {
    ...input,
    secretReference: secretReference || undefined,
    provider: AI_PROVIDER_KINDS.indexOf(input.provider),
    location: AI_PROCESSING_LOCATIONS.indexOf(input.location),
  };
}

function enumName<T extends string>(value: T | number, values: readonly T[], label: string): T {
  if (typeof value === 'string' && values.includes(value)) return value;
  if (typeof value === 'number' && values[value]) return values[value]!;
  throw new Error(`${label} der KI-Verbindung wird von dieser Console nicht unterstuetzt.`);
}
