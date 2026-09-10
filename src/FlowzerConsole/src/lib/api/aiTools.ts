import { AI_TOOL_SIDE_EFFECTS, type AiToolDto, type AiToolSideEffect } from './types';

type RawAiToolDto = Omit<AiToolDto, 'sideEffect'> & {
  sideEffect: AiToolSideEffect | number;
};

/** Hält numerische .NET-Enums am API-Rand und lehnt unbekannte Außenwirkungen fail-closed ab. */
export function normalizeAiTool(tool: RawAiToolDto): AiToolDto {
  const sideEffect = typeof tool.sideEffect === 'number'
    ? AI_TOOL_SIDE_EFFECTS[tool.sideEffect]
    : AI_TOOL_SIDE_EFFECTS.includes(tool.sideEffect) ? tool.sideEffect : undefined;
  if (!sideEffect) throw new Error('Außenwirkung des KI-Werkzeugs wird von dieser Console nicht unterstützt.');
  return { ...tool, sideEffect };
}
