# Writing

> **Use when:** you write or review any text in the UI: labels, buttons, errors, empty states, confirmations, notifications, dates and numbers.
> **Sources:** Primer [Content](https://primer.style/product/getting-started/foundations/content/), [Forms](https://primer.style/product/ui-patterns/forms/), [Empty states](https://primer.style/product/ui-patterns/empty-states/), [Saving](https://primer.style/product/ui-patterns/saving/), [ConfirmationDialog guidelines](https://primer.style/product/components/confirmation-dialog/guidelines/), [Links and buttons](https://primer.style/accessibility/design-guidance/links-and-buttons/) · Mantine [TextInput](https://mantine.dev/llms/core-text-input.md), [Modals manager](https://mantine.dev/llms/x-modals.md), [Notifications](https://mantine.dev/llms/x-notifications.md)

Words are interface. Users scan, so every string must say exactly one thing, in the user's terms, in as few words
as it takes. Strings here are English examples; ship them through the product's i18n.

## Voice

- Plain, direct, calm. Aim for words a 12-year-old reads without effort.
- Active voice, present tense: "We couldn't save your changes", not "Changes could not be saved by the system".
- Talk to the user as "you" and "your". Use "I" only when the user states something ("I understand the risk").
- No jokes, "Oops", exclamation marks or emoji, above all in errors, waiting and failure.
- Do not apologise by reflex. One "sorry" is fine when the product is at fault and the user loses time.
- Never say "easy", "simple", "just" or "quick": if the user is stuck, these words blame them.
- Do not write "click here" or "here". The link text names the destination: "View billing history".

## Words and terms

- Use the user's words for things, not the database's: "server", not "instance_record"; "invite", not "membership".
- One name per thing across the product. If the button says "Archive", the result says "Archived", not "Moved".
- Do not show internal ids, enum values, stack traces or class names unless the audience works with them
  (an admin console may show a request id; put it last, in `c="dimmed"` text).
- Spell out an acronym unless users know it better than the long form (URL, CPU, PDF are fine).
- "and", not "&" or "+". No semicolons; split into two sentences.

## Case and punctuation

- Sentence case everywhere: titles, labels, buttons, tabs, menu items ("Create server", not "Create Server").
  Capitalise only proper nouns and product names.
- No full stop on labels, buttons, titles or single-line hints. Full sentences in body text and messages end with one.
- No colon after form labels.
- A label that starts with a number keeps the rest lowercase: "3 open alerts".

## Labels

- Short noun phrases, about 1 to 3 words: "Server name", "Billing email".
- Put extra context in the input's `description`, not in the label. Do not repeat the label in the description.
- A placeholder is an example value ("web-01"), never the label or an instruction you need later.
- Mark required or optional fields the same way across a form. See [forms](patterns/forms.md).

## Buttons and actions

- Verb + object: "Create server", "Save changes", "Invite member", "Delete project".
- Never "OK", "Yes", "Submit", "Confirm" or "Continue" alone. The label must make sense read out of context.
- The object may be dropped when it is obvious and unique on the screen ("Save" at the bottom of a one-section form).
- Keep it to 1 to 3 words, on one line.
- The same action has the same label everywhere: trigger, dialog and result use one verb.
- Cancel buttons say "Cancel" (or a specific keep-action like "Keep draft"). See [actions](decisions/actions.md).

## Errors

An error message answers two questions: what happened, and what the user can do now.

| Situation | Write | Not |
| --- | --- | --- |
| Field invalid | "Use 3 to 30 letters, numbers or dashes" | "Invalid input" |
| Field empty | "Enter a server name" | "Field required", "You forgot the name" |
| Save failed, retry may work | "Couldn't save your changes. Check your connection and try again." | "Error 500" |
| Not allowed | "You need the Admin role to delete servers. Ask an admin of this team." | "Forbidden" |
| Conflict | "Someone else changed this server. Reload to see their changes." | "Version mismatch" |

- Be specific: name the field or object. Say the rule ("at least 8 characters"), not only that it was broken.
- Never blame the user ("You entered a wrong date"). Describe the problem and the fix.
- Error codes may follow the message for support ("Couldn't load servers. Try again. (Code: SRV-503)"), never alone.
- Do not repeat the field's description inside its error. The error replaces it while shown.
- Keep what the user typed. The message says how to fix it, the form keeps the value.
- Where an error shows (inline, alert, notification) is decided in [feedback](decisions/feedback.md).

## Empty states

- Title: what this place is, or why it is empty. "No servers yet" / "No results for 'nginx'".
- Body (optional, one or two sentences): what the user gains or what to change. "Servers run your game worlds."
  / "Try a shorter search or clear the filters."
- Action: the next step as verb + object: "Create server", "Clear filters".
- An empty state caused by an error says so plainly ("Couldn't load servers") and offers a way out ("Try again").
- See [empty states](patterns/empty-states.md).

## Confirmations

- Title: the action and its object as a short question or phrase: "Delete server web-01?". Never "Are you sure?".
- Body: the consequence, specific: what will be lost, who is affected, whether it can be undone.
  "This deletes the server and its 12 backups. This can't be undone."
- Confirm button repeats the action: "Delete server". Cancel stays "Cancel".
- Bulk: state the count in the title and the button: "Delete 3 servers?", "Delete 3 servers".
- Type-to-confirm: tell the user exactly what to type: "Type web-01 to confirm".
- See [destructive actions](flows/destructive-actions.md).

## Success and progress

- Short, past tense, same verb as the action: "Server deleted", "Changes saved", "Invite sent".
- No "Successfully …"; the green icon already says success.
- Progress names the work: "Deleting server…", "Uploading 3 files…". Use the same verb as the trigger.
- Add a next step only when there is a likely one: "Invite sent. Add another".

## Numbers, dates and units

- Format with the user's locale (`Intl.NumberFormat`, `Intl.DateTimeFormat`, `Intl.RelativeTimeFormat`).
  Never build "1,234" or "12/03" by hand.
- Recent events: relative time ("5 minutes ago") with the exact date and time available on hover and focus.
- Deadlines, expiry, billing, audit logs: the exact date and time, with the time zone when it can differ.
- Running work: elapsed or remaining time ("Running for 3 min").
- Date ranges in one year: "Feb 17 – Feb 23, 2026". Across years, give both years.
- Always give units: "512 MB", "30 s", "€12.00". Put a space between number and unit.
- Show counts as digits ("3 servers"), not words.

## Length, truncation and translation

- Leave room for text to grow by 30 to 40 % in translation. Never size a button or column to fit English exactly.
- Never build a sentence from pieces (`"Delete " + name + "?"`). Use one message with placeholders:
  `t('confirm.deleteServer', { name })`. Word order differs between languages.
- Never pick plurals with `count === 1 ? 'server' : 'servers'`. Use the i18n library's plural rules (ICU messages).
- Truncate names and long values on purpose (`Text truncate` or `lineClamp`) and show the full text on hover and
  focus, or on the detail page. Never truncate actions, errors or numbers.
- Put the important word first so truncation keeps it.

## Checklist

- [ ] Every button is verb + object (or a clear verb on a single-purpose screen); no "OK", "Yes" or "Submit".
- [ ] Sentence case for titles, labels, buttons, tabs and menu items; no full stop on labels or buttons.
- [ ] Labels are short nouns in the user's words; no placeholder used as a label.
- [ ] Every error says what happened and what to do; codes only after the message; no blame.
- [ ] Empty states say what this is and offer the next step.
- [ ] Confirmations name the object and the consequence; the confirm button repeats the action.
- [ ] The same verb is used for trigger, progress and result ("Delete", "Deleting…", "Deleted").
- [ ] Success messages are short and past tense.
- [ ] Dates, numbers and units come from locale formatters; deadlines show the exact date.
- [ ] No string concatenation; plurals use the i18n library; layout survives 40 % longer text.
- [ ] No internal ids, enum values or jargon unless the audience needs them.
