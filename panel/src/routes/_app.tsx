import { createFileRoute } from '@tanstack/react-router';
import { AppLayout } from '@/layout/AppLayout';

// Pathless layout: every page inside the application shell. Signing in (phase 2) adds the guard here.
export const Route = createFileRoute('/_app')({
  component: AppLayout,
});
