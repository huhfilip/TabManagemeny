namespace TabMan.Core;

public static class NavigationEngine
{
    public static Selection Initial(SwitcherLayout layout)
    {
        for (var row = 0; row < layout.Rows.Count; row++)
        {
            var items = layout.Rows[row].Items;
            for (var column = 0; column < items.Count; column++)
            {
                if (items[column].IsInitialTarget)
                {
                    return Selection.At(row, column);
                }
            }
        }

        return layout.TotalCount == 0 ? Selection.None : Selection.At(0, 0);
    }

    public static Selection Find(SwitcherLayout layout, long hwnd)
    {
        for (var row = 0; row < layout.Rows.Count; row++)
        {
            var items = layout.Rows[row].Items;
            for (var column = 0; column < items.Count; column++)
            {
                if (items[column].Hwnd == hwnd)
                {
                    return new Selection(row, column, column);
                }
            }
        }

        return Selection.None;
    }

    public static SwitcherItem? Get(SwitcherLayout layout, Selection selection)
    {
        if (selection.IsNone
            || selection.Row < 0
            || selection.Row >= layout.Rows.Count)
        {
            return null;
        }

        var items = layout.Rows[selection.Row].Items;
        if (selection.Column < 0 || selection.Column >= items.Count)
        {
            return null;
        }

        return items[selection.Column];
    }

    public static Selection Move(Selection current, NavigationDirection direction, SwitcherLayout layout)
    {
        if (layout.TotalCount == 0)
        {
            return Selection.None;
        }

        if (current.IsNone || Get(layout, current) is null)
        {
            current = Initial(layout);
        }

        var rowCount = layout.Rows.Count;
        var row = current.Row;
        var column = current.Column;
        var preferred = current.PreferredColumn;

        switch (direction)
        {
            case NavigationDirection.Left:
                if (column > 0)
                {
                    return Selection.At(row, column - 1);
                }

                var previousRow = row == 0 ? rowCount - 1 : row - 1;
                return Selection.At(previousRow, layout.Rows[previousRow].Items.Count - 1);

            case NavigationDirection.Right:
                if (column < layout.Rows[row].Items.Count - 1)
                {
                    return Selection.At(row, column + 1);
                }

                var nextRow = row == rowCount - 1 ? 0 : row + 1;
                return Selection.At(nextRow, 0);

            case NavigationDirection.Up:
                var up = row == 0 ? rowCount - 1 : row - 1;
                return new Selection(up, ClampColumn(layout, up, preferred), preferred);

            case NavigationDirection.Down:
                var down = row == rowCount - 1 ? 0 : row + 1;
                return new Selection(down, ClampColumn(layout, down, preferred), preferred);

            case NavigationDirection.Next:
                if (column < layout.Rows[row].Items.Count - 1)
                {
                    return Selection.At(row, column + 1);
                }

                if (row < rowCount - 1)
                {
                    return Selection.At(row + 1, 0);
                }

                return Selection.At(0, 0);

            case NavigationDirection.Previous:
                if (column > 0)
                {
                    return Selection.At(row, column - 1);
                }

                if (row > 0)
                {
                    var prior = row - 1;
                    return Selection.At(prior, layout.Rows[prior].Items.Count - 1);
                }

                var last = rowCount - 1;
                return Selection.At(last, layout.Rows[last].Items.Count - 1);

            case NavigationDirection.First:
                return Selection.At(0, 0);

            case NavigationDirection.Last:
                var lastRow = rowCount - 1;
                return Selection.At(lastRow, layout.Rows[lastRow].Items.Count - 1);

            default:
                return current;
        }
    }

    private static int ClampColumn(SwitcherLayout layout, int row, int preferred)
    {
        var count = layout.Rows[row].Items.Count;
        if (count <= 0)
        {
            return 0;
        }

        return Math.Clamp(preferred, 0, count - 1);
    }
}
