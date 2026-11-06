import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Figures } from '@/components/charts/figures';

/** The smallest table the figures can hold. */
const ROWS = (
  <tbody>
    <tr>
      <td>A figure</td>
    </tr>
  </tbody>
);

describe('figures folded away beneath a drawing', () => {
  /** A long table in its own box does not push the rest of the screen away. */
  it('keeps a long table to a box of its own height', () => {
    render(<Figures label="The figures">{ROWS}</Figures>);

    expect(screen.getByRole('table', { hidden: true }).parentElement).toHaveClass('max-h-72');
  });

  it('shows every row where the screen says the table is to be read through whole', () => {
    render(
      <Figures label="The figures" uncapped>
        {ROWS}
      </Figures>,
    );

    expect(screen.getByRole('table', { hidden: true }).parentElement).not.toHaveClass('max-h-72');
  });

  /** The line that unfolds the figures can stand, and work, before the figures are in. */
  it('keeps what stands in for figures on their way from anybody reading', () => {
    const { rerender } = render(
      <Figures label="The figures" open waiting>
        {ROWS}
      </Figures>,
    );

    expect(screen.getByText('The figures').closest('details')).toHaveAttribute('open');
    expect(screen.queryByRole('table')).not.toBeInTheDocument();

    rerender(
      <Figures label="The figures" open>
        {ROWS}
      </Figures>,
    );

    expect(screen.getByRole('table')).toBeInTheDocument();
  });

  it('leaves its folding to the browser unless the screen keeps it', () => {
    render(<Figures label="The figures">{ROWS}</Figures>);

    expect(screen.getByText('The figures').closest('details')).not.toHaveAttribute('open');
  });

  it('opens as the screen says, and tells the screen when the reader folds it', () => {
    const told = vi.fn();

    render(
      <Figures label="The figures" open onToggle={told}>
        {ROWS}
      </Figures>,
    );

    const details = screen.getByText('The figures').closest('details') as HTMLDetailsElement;

    expect(details).toHaveAttribute('open');

    details.open = false;
    fireEvent(details, new Event('toggle'));

    expect(told).toHaveBeenLastCalledWith(false);
  });
});
