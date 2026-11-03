import { createFileRoute } from '@tanstack/react-router';
import { LoaderLog } from '@/features/servers/mods/LoaderLog';

export const Route = createFileRoute('/_app/servers/$id/mods/log')({
  component: ModsLog,
});

/** BepInEx's log gets the whole screen when it is open (docs/13 §4.3), instead of a card at the bottom of the set. */
function ModsLog() {
  const { id } = Route.useParams();
  return <LoaderLog id={id} defaultOpen height="calc(100vh - 320px)" />;
}
