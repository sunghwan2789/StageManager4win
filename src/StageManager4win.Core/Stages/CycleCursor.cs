using StageManager4win.Core.Windows;

namespace StageManager4win.Core.Stages;

public sealed class CycleCursor
{
    private readonly WindowId[] _order;
    private int _index;
    private bool _hasCurrent;

    public CycleCursor(IReadOnlyList<WindowId> order) => _order = order.ToArray();

    public IReadOnlyList<WindowId> Order => _order;

    public WindowId? Current => _hasCurrent ? _order[_index] : null;

    public WindowId? Advance(int delta, Func<WindowId, bool> isEligible)
    {
        if (_order.Length == 0)
        {
            return null;
        }

        var step = Math.Sign(delta);
        if (step == 0)
        {
            step = 1;
        }

        var index = _hasCurrent ? _index : (step > 0 ? -1 : 0);

        for (var attempt = 0; attempt < _order.Length; attempt++)
        {
            index = (index + step + _order.Length) % _order.Length;

            if (isEligible(_order[index]))
            {
                _index = index;
                _hasCurrent = true;
                return _order[index];
            }
        }

        return null;
    }
}
