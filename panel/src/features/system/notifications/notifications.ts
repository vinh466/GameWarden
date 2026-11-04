import type { NotificationDestinationUpdate, NotificationDestinationView } from '@/api/generated';

export const notificationSeverities = ['info', 'success', 'warning', 'error', 'critical'];

/** A new destination with the first mode Core supports and the safe defaults. */
export function blankDestination(mode: string): NotificationDestinationUpdate {
  return {
    id: '',
    name: '',
    channel: 'discord-webhook',
    enabled: true,
    mode,
    language: 'vi',
    minimumSeverity: 'warning',
    topics: ['*'],
    instances: ['*'],
    mentionRoleId: null,
    panelUrl: null,
    intervalSec: 60,
    digestSec: 300,
    webhookUrl: null,
  };
}

/** The editable shape of a saved destination; the webhook stays write-only. */
export function toUpdate(destination: NotificationDestinationView): NotificationDestinationUpdate {
  return {
    id: destination.id,
    name: destination.name,
    channel: destination.channel,
    enabled: destination.enabled,
    mode: destination.mode,
    language: destination.language,
    minimumSeverity: destination.minimumSeverity,
    topics: destination.topics,
    instances: destination.instances,
    mentionRoleId: destination.mentionRoleId,
    panelUrl: destination.panelUrl,
    intervalSec: destination.intervalSec,
    digestSec: destination.digestSec,
    webhookUrl: null,
  };
}
