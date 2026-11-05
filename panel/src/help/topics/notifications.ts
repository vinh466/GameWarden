import type { HelpTopic } from '@/components/help/types';
import { repoDoc } from './sources';

// Notification destination form (docs/18 §4.3 and §6). Sources checked 2026-11-04.
const checked = '2026-11-04';
const docs18 = repoDoc(
  'docs/18-discord-notifications.md',
  '18 Discord notifications, §4.3 Destinations and rules',
  checked
);
const docs18Adapter = repoDoc(
  'docs/18-discord-notifications.md',
  '18 Discord notifications, §5 The Discord adapter',
  checked
);
const panelDocs = repoDoc('docs/11-web-panel.md', '11 Web panel', checked);

export const notificationTopics = {
  'notifications.identity': {
    title: 'Destination id and name',
    summary:
      'The id is the stable key GameWarden uses for the destination and for its webhook secret; the name is only what the panel shows.',
    body: [
      {
        p: 'The id becomes the secret name in the notifications scope, so it is fixed after the destination exists. Use lowercase letters, digits and dashes, for example alerts or status.',
      },
      {
        p: 'The name can be changed at any time; it does not rename the secret or affect deliveries.',
      },
    ],
    verified: true,
    sources: [docs18],
    related: ['notifications.webhook', 'notifications.enabled'],
  },

  'notifications.channel': {
    title: 'Delivery channel',
    summary:
      'A webhook is a one-way message into one Discord channel. The GameWarden bot posts into the channel mapped to the affected server, creates a thread for an incident and can add approval buttons.',
    body: [
      {
        p: 'Use the webhook when the bot is off or when you only need a one-way alert or status message. Use the bot when you want the incident card, its thread and the Approve/Deny flow in the same channel as the conversation.',
      },
      {
        tip: 'A bot destination needs no webhook URL. The bot must be enabled and the server channel must be mapped in System → Operations.',
      },
    ],
    verified: true,
    sources: [docs18],
    related: ['notifications.webhook', 'notifications.mode'],
  },

  'notifications.webhook': {
    title: 'Discord webhook URL',
    summary:
      'A webhook is the address Discord gives one channel. GameWarden treats it as a password: it is stored in the secret store and is never shown again after saving.',
    body: [
      {
        p: 'In Discord open the channel settings, Integrations, Webhooks, create a webhook, copy its URL, and paste it here. The URL looks like https://discord.com/api/webhooks/... and belongs to that one channel.',
      },
      {
        p: 'Saving a destination writes the URL to the notifications secret scope; notifications.json only contains the destination name. The panel and API return only "webhook set".',
      },
      {
        warning:
          'Treat the URL like a password. Anyone who has it can post to that channel. Do not paste it into a ticket, chat or screenshot; create a new webhook if it leaks.',
      },
    ],
    tested:
      '0.3.0-dev.3 on the template VM, 2026-11-04: a real Discord webhook accepted the Test message and server events.',
    verified: true,
    sources: [docs18, docs18Adapter, panelDocs],
    related: ['notifications.mode', 'notifications.test', 'notifications.enabled'],
  },

  'notifications.mode': {
    title: 'Delivery mode',
    summary:
      'How the destination sends events. Instant is best for rare, important events; digest for chatty groups; status for one always-current server message.',
    body: [
      {
        p: 'The mode is a property of the destination, not of the event. An event can go to several destinations, each with its own mode and filters.',
      },
      {
        list: [
          'Instant: one Discord message per event, as soon as the outbox delivers it. Use it for crashes, faulted servers, failed jobs and update warnings.',
          'Digest: GameWarden waits digestSec, groups the matching events for this destination, and sends one message. Use it for chatty events such as player joins and leaves.',
          'Status: GameWarden keeps one message per destination and edits it in place every intervalSec when the rendered server state changes. A webhook cannot pin; pin the message once by hand in Discord.',
        ],
      },
      {
        tip: 'For a first destination use Instant with a minimum severity of warning. Add a second destination for Status only after the alerts work.',
      },
    ],
    tested:
      '0.3.0-dev.3 on the template VM, 2026-11-04: a real webhook accepted instant alerts. Digest and status were exercised by the channel and status-loop tests in 0.3.0-dev.4.',
    verified: true,
    sources: [docs18, docs18Adapter],
    related: ['notifications.webhook', 'notifications.minimumSeverity', 'notifications.timing'],
  },

  'notifications.language': {
    title: 'Language',
    summary:
      'The language the Discord adapter uses for its own text, such as the test message and the status heading. Event titles keep the language the game or feature used.',
    body: [
      {
        p: 'English is the fallback for text without a translation. Vietnamese is intended for the group in this VM; a custom topic may still supply its own title and body.',
      },
    ],
    values: [
      { value: 'vi', text: 'Vietnamese labels and status text where a translation exists.' },
      { value: 'en', text: 'English labels and status text; also the fallback.' },
    ],
    verified: true,
    sources: [docs18, panelDocs],
    related: ['notifications.mode'],
  },

  'notifications.minimumSeverity': {
    title: 'Minimum severity',
    summary:
      'The lowest severity this destination accepts. Lower events are dropped before a delivery row is created, so they never reach Discord.',
    body: [
      {
        p: 'Use warning for an alerts channel that should stay quiet. A status destination usually uses info because the status message itself carries the state.',
      },
      {
        tip: 'A critical event can mention the configured role; lower events never mention it.',
      },
    ],
    values: [
      { value: 'info', text: 'Everything, including starts and stops.' },
      { value: 'success', text: 'Successful jobs and updates, plus warning/error/critical.' },
      { value: 'warning', text: 'Warnings, errors and critical events; the usual alerts floor.' },
      { value: 'error', text: 'Errors and critical events only.' },
      {
        value: 'critical',
        text: 'Only events that need immediate attention, such as a faulted server.',
      },
    ],
    verified: true,
    sources: [docs18],
    related: ['notifications.mode', 'notifications.mentionRole'],
  },

  'notifications.topics': {
    title: 'Topics',
    summary:
      'Which event kinds this destination receives. Empty or * means every topic; exact names and one wildcard suffix such as instance.* are supported.',
    body: [
      {
        p: 'The catalog groups topics by instance, job, mods, update, system, player, admin and backup. The help drawer of this field lists the current catalog from Core.',
      },
      {
        list: [
          'A destination with several topics receives an event when any filter matches.',
          'A wildcard only works at the end, e.g. instance.* matches instance.crashed but not job.failed.',
          'Custom topics may be published without a catalog entry; use * to receive them.',
        ],
      },
      {
        warning:
          'Player and admin events can contain names and platform ids. Add them only to a channel the group is allowed to read.',
      },
    ],
    verified: true,
    sources: [docs18, docs18Adapter],
    related: ['notifications.instances', 'notifications.minimumSeverity'],
  },

  'notifications.instances': {
    title: 'Servers',
    summary:
      'Which game servers this destination covers. Empty or * means every server; pick ids to keep one channel for one server.',
    body: [
      {
        p: 'An event that has no server, such as a Core update or a low-disk warning, is not filtered out by this list: it reaches every destination whose topics and severity match.',
      },
      {
        tip: 'Use one destination for the whole group and a second one only for a specific server if its failures should go to a different channel.',
      },
    ],
    verified: true,
    sources: [docs18],
    related: ['notifications.topics', 'notifications.panelUrl'],
  },

  'notifications.mentionRole': {
    title: 'Mention role id',
    summary:
      'Optional Discord role to mention for critical events only. Leave it empty for no mentions, which is the safe default.',
    body: [
      {
        p: 'Enable Discord Developer Mode, right-click the role, and copy its id. GameWarden adds the literal <@&id> only when the event severity is critical and the destination has this field set.',
      },
      {
        p: 'The adapter sends allowed_mentions explicitly: roles are parsed only for that role. Game data cannot inject @everyone or another mention.',
      },
      {
        warning:
          'A role mention can notify many people at once. Use it only for a role that expects critical server alerts.',
      },
    ],
    verified: true,
    sources: [docs18, docs18Adapter],
    related: ['notifications.minimumSeverity'],
  },

  'notifications.panelUrl': {
    title: 'Panel URL',
    summary:
      'Optional base address of this panel, for links in Discord messages. The link opens the relevant server page; without it the message has no link.',
    body: [
      {
        p: 'Use an address that Discord and the people reading the channel can reach, for example the VM address on the VPN. Only http and https are accepted.',
      },
      {
        p: 'An event may carry its own URL, for example a release page. That URL wins over this base address.',
      },
    ],
    verified: true,
    sources: [docs18, panelDocs],
    related: ['notifications.instances'],
  },

  'notifications.timing': {
    title: 'Status interval and digest window',
    summary:
      'Status edits one message at most every intervalSec. Digest waits digestSec before grouping events. Shorter windows are noisier and use more Discord requests.',
    body: [
      {
        p: 'Status: the loop checks about every 5 seconds but skips a destination until its interval has passed. If the rendered state did not change, it skips the PATCH too. A deleted status message is posted again.',
      },
      {
        p: 'Digest: the first matching event starts the window. Events that arrive during the window join the same message; the message is sent after digestSec. A very large digest is truncated to Discord limits.',
      },
      {
        tip: 'For testing, set digestSec to 30 and intervalSec to 30. For normal use, 300 seconds for a player digest and 60 seconds for status are calmer.',
      },
    ],
    verified: true,
    sources: [docs18, docs18Adapter],
    related: ['notifications.mode'],
  },

  'notifications.enabled': {
    title: 'Enabled',
    summary:
      'A disabled destination keeps its settings and history but sends nothing. GameWarden also disables a destination automatically after a permanent webhook error.',
    body: [
      {
        p: 'A deleted webhook (404) or refused credentials (401/403) disables the destination and stores the reason in the delivery log. Fix or replace the webhook, then enable it again explicitly.',
      },
      {
        p: 'Transient failures such as a timeout, a 5xx or a 429 stay pending and retry with backoff; they do not disable the destination.',
      },
    ],
    verified: true,
    sources: [docs18, docs18Adapter],
    related: ['notifications.webhook', 'notifications.deliveries'],
  },

  'notifications.test': {
    title: 'Test message',
    summary:
      'Sends a synthetic info event through the real channel and shows the Discord result. Save the webhook first; the test uses the stored secret, not the text currently in the form.',
    body: [
      {
        p: 'A successful test returns the Discord message id. A failure explains whether the webhook is missing, Discord refused it, or Discord rejected the payload.',
      },
      {
        tip: 'Use Test after changing a webhook. It proves the channel works before a real crash or failed job needs it.',
      },
    ],
    tested:
      '0.3.0-dev.3 on the template VM, 2026-11-04: the real webhook accepted the test message.',
    verified: true,
    sources: [docs18, docs18Adapter],
    related: ['notifications.webhook', 'notifications.enabled'],
  },

  'notifications.deliveries': {
    title: 'Delivery log',
    summary:
      'The newest event deliveries with their destination, state, attempts and last redacted error. Retry puts a failed or dropped row back in the queue.',
    body: [
      {
        p: 'States: pending waits for a retry or the digest window; sending is in flight; sent has a Discord message id; failed reached the attempt limit or a permanent refusal; dropped was refused as invalid data or lost its destination.',
      },
      {
        p: 'The log stores the event topic and title, not the webhook URL or message body. A digest row shares one Discord message id across the events in the group.',
      },
    ],
    verified: true,
    sources: [docs18, docs18Adapter],
    related: ['notifications.enabled', 'notifications.mode'],
  },
} satisfies Record<string, HelpTopic>;
