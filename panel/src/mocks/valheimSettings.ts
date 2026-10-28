import type { AdapterSetting, AdapterSettingGroup } from '@/api/generated';
import schema from '../../../adapters/valheim/settings.schema.json';

// The real Valheim settings.schema.json, turned into what Core's GET /adapters answers (AdapterSummary.From), so
// `dev:mock` shows the form the VM will show.
type Text = string | Record<string, string>;

interface SchemaGroup {
  id: string;
  label?: Text;
  help?: Text;
  advanced?: boolean;
}

interface SchemaSetting {
  key: string;
  type: string;
  group: string;
  label?: Text;
  help?: Text;
  warning?: Text;
  default?: string | number | boolean;
  required?: boolean;
  wizard?: boolean;
  min?: number;
  max?: number;
  minLength?: number;
  maxLength?: number;
  pattern?: string;
  options?: { value: string; label?: Text }[];
  restartRequired?: boolean;
}

const { groups, settings } = schema as { groups: SchemaGroup[]; settings: SchemaSetting[] };
const text = (value?: Text) =>
  value === undefined ? null : typeof value === 'string' ? { en: value } : value;

export const valheimGroups: AdapterSettingGroup[] = groups.map((group) => ({
  id: group.id,
  label: text(group.label),
  help: text(group.help),
  advanced: group.advanced ?? false,
}));

export const valheimSettings: AdapterSetting[] = settings.map((setting) => {
  const required = setting.required === true && setting.default === undefined;
  return {
    key: setting.key,
    type: setting.type,
    group: setting.group,
    label: text(setting.label),
    help: text(setting.help),
    warning: text(setting.warning),
    default: setting.default === undefined ? null : String(setting.default),
    required,
    wizard: required || setting.wizard === true,
    min: setting.min ?? null,
    max: setting.max ?? null,
    minLength: setting.minLength ?? null,
    maxLength: setting.maxLength ?? null,
    pattern: setting.pattern ?? null,
    options: (setting.options ?? []).map((option) => ({
      value: option.value,
      label: text(option.label),
    })),
    restartRequired: setting.restartRequired ?? true,
  };
});
