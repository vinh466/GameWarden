import { render, screen } from '@test-utils';
import { userEvent } from '@testing-library/user-event';
import type { JobInfo } from '@/api/generated';
import { JobsMobileList } from './JobsMobileList';

const running: JobInfo = {
  id: '20261028-101500-ab12c3',
  type: 'install',
  instanceId: 'valheim-main',
  state: 'running',
  stage: 'steamcmd.install',
  progress: 38,
  message: null,
  createdAt: new Date().toISOString(),
  finishedAt: null,
};

describe('JobsMobileList', () => {
  it('keeps the stage and progress visible and opens the log from a keyboard-reachable button', async () => {
    const onOpen = vi.fn();
    render(<JobsMobileList jobs={[running]} showServer onOpen={onOpen} />);

    expect(screen.getByText('steamcmd.install · 38%')).toBeInTheDocument();
    expect(screen.getByText('valheim-main')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: /Install .*20261028-101500-ab12c3/ }));
    expect(onOpen).toHaveBeenCalledWith(running.id);
  });
});
