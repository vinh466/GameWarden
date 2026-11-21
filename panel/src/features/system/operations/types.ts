import type { AgentDiscordTestResult, AgentModelTestResult } from '@/api/generated';

/** The last model or Discord test, shown under the card that ran it. */
export type TestState =
  | { kind: 'model'; result: AgentModelTestResult }
  | { kind: 'discord'; result: AgentDiscordTestResult }
  | { kind: 'model' | 'discord'; error: string };
