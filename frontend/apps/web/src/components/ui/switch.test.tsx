import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { Switch } from '@/components/ui/switch';

describe('a setting that is either on or off', () => {
  it('is announced as a switch, with what it governs and whether it is on', () => {
    render(<Switch label="Record what people click" checked onChange={() => {}} />);

    expect(screen.getByRole('switch', { name: 'Record what people click' })).toBeChecked();
  });

  it('hands back the state it is being moved to', async () => {
    const changed = vi.fn();

    render(<Switch label="Record what people click" checked onChange={changed} />);

    await userEvent.click(screen.getByRole('switch'));

    expect(changed).toHaveBeenCalledWith(false);
  });

  /**
   * The whole row is the control rather than the sliding part alone, so somebody on a phone does
   * not have to aim at a target the width of a fingertip.
   */
  it('is operated by pressing its wording as well as its switch', async () => {
    const changed = vi.fn();

    render(<Switch label="Record what people click" checked={false} onChange={changed} />);

    await userEvent.click(screen.getByText('Record what people click'));

    expect(changed).toHaveBeenCalledWith(true);
  });

  it('reads out the one thing somebody would otherwise get wrong', () => {
    render(
      <Switch
        label="Record what people click"
        hint="Your own wording is kept, never anything a visitor types."
        checked
        onChange={() => {}}
      />,
    );

    expect(screen.getByRole('switch')).toHaveAccessibleDescription(
      'Your own wording is kept, never anything a visitor types.',
    );
  });

  it('can be reached and worked without a pointer', async () => {
    const changed = vi.fn();

    render(<Switch label="Record what people click" checked onChange={changed} />);

    await userEvent.tab();
    await userEvent.keyboard(' ');

    expect(screen.getByRole('switch')).toHaveFocus();
    expect(changed).toHaveBeenCalledWith(false);
  });

  it('cannot be pressed again while the last change is still being saved', async () => {
    const changed = vi.fn();

    render(<Switch label="Record what people click" checked busy onChange={changed} />);

    await userEvent.click(screen.getByRole('switch'));

    expect(changed).not.toHaveBeenCalled();
    expect(screen.getByRole('switch')).toHaveAttribute('aria-busy', 'true');
  });
});
