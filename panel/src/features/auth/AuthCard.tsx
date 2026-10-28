import type { ReactNode } from 'react';
import { IconShieldCheckFilled } from '@tabler/icons-react';
import { Container, Paper, Stack, Text, ThemeIcon, Title } from '@mantine/core';

/** Sign-in and setup (docs/11 §4): one centred card on a plain page, Mantine UI's AuthenticationTitle layout. */
export function AuthCard({
  title,
  text,
  children,
}: {
  title: string;
  text: string;
  children: ReactNode;
}) {
  return (
    <Container size={420} my={40}>
      <Stack align="center" gap="xs">
        <ThemeIcon size={48} radius="md" variant="light">
          <IconShieldCheckFilled size={30} aria-hidden />
        </ThemeIcon>
        <Title order={2} ta="center" fw={600}>
          {title}
        </Title>
        <Text c="dimmed" size="sm" ta="center">
          {text}
        </Text>
      </Stack>
      <Paper withBorder shadow="sm" p={22} mt={30} radius="md">
        {children}
      </Paper>
    </Container>
  );
}
