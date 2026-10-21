using ONEVO.Application.Features.Dashboard.Layout.Registry;

namespace ONEVO.Application.Features.Dashboard.Layout.Grid;

/// <summary>Pure grid rules: validation, auto-placement and gravity compaction. No I/O.</summary>
public static class DashboardGrid
{
    public const string OutOfBounds = "OUT_OF_BOUNDS";
    public const string SizeNotAllowed = "SIZE_NOT_ALLOWED";
    public const string Overlap = "OVERLAP";
    public const string UnknownWidget = "UNKNOWN_WIDGET";
    public const string DuplicateWidget = "DUPLICATE_WIDGET";

    public const int MaxRows = 200;

    public static bool Overlaps(GridRect a, GridRect b) =>
        (long)a.X < (long)b.X + b.W && (long)b.X < (long)a.X + a.W && (long)a.Y < (long)b.Y + b.H && (long)b.Y < (long)a.Y + a.H;

    public static IReadOnlyList<string> Validate(IReadOnlyList<(string Key, GridRect Rect)> items, string breakpoint)
    {
        var columns = Breakpoints.Columns(breakpoint);
        var errors = new List<string>();
        var seen = new HashSet<string>();
        var valid = new List<GridRect>();

        foreach (var (key, rect) in items)
        {
            if (!seen.Add(key))
            {
                errors.Add(DuplicateWidget);
                continue;
            }

            var def = DashboardWidgetRegistry.Get(key);
            if (def is null)
            {
                errors.Add(UnknownWidget);
                continue;
            }

            if (rect.X < 0 || rect.Y < 0 || rect.W < 1 || rect.H < 1 || rect.X >= columns || rect.W > columns - rect.X || rect.H > MaxRows - rect.Y)
            {
                errors.Add(OutOfBounds);
                continue;
            }

            var minW = Math.Min(def.MinW, columns);
            var maxW = Math.Min(def.MaxW, columns);
            if (rect.W < minW || rect.W > maxW || rect.H < def.MinH || rect.H > def.MaxH)
            {
                errors.Add(SizeNotAllowed);
            }

            valid.Add(rect);
        }

        for (var i = 0; i < valid.Count; i++)
        {
            for (var j = i + 1; j < valid.Count; j++)
            {
                if (Overlaps(valid[i], valid[j]))
                {
                    errors.Add(Overlap);
                }
            }
        }

        return errors.Distinct().ToList();
    }


    public static GridRect AutoPlace(IEnumerable<GridRect> occupied, int w, int h, int columns)
    {
        if (w < 1 || h < 1 || w > columns)
        {
            throw new ArgumentOutOfRangeException(nameof(w), "Widget size must be at least 1x1 and fit within the column count.");
        }

        if (h > MaxRows)
        {
            throw new ArgumentOutOfRangeException(nameof(h), "Widget height exceeds the maximum row count.");
        }

        var taken = occupied.ToList();
        for (var y = 0; y + h <= MaxRows; y++)
        {
            for (var x = 0; x <= columns - w; x++)
            {
                var candidate = new GridRect(x, y, w, h);
                if (!taken.Any(t => Overlaps(t, candidate)))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException($"No free slot within {MaxRows} rows.");
    }

    public static IReadOnlyList<(string Key, GridRect Rect)> Compact(IReadOnlyList<(string Key, GridRect Rect)> items)
    {
        var placed = new List<(string Key, GridRect Rect)>();
        foreach (var (key, rect) in items.OrderBy(i => i.Rect.Y).ThenBy(i => i.Rect.X))
        {
            // Lowest free Y: the highest bottom edge among already-placed items that share columns and sit above.
            long floor = 0;
            foreach (var (_, p) in placed)
            {
                var shareColumns = (long)p.X < (long)rect.X + rect.W && (long)rect.X < (long)p.X + p.W;
                var bottom = (long)p.Y + p.H;
                if (shareColumns && bottom <= rect.Y && bottom > floor)
                {
                    floor = bottom;
                }
            }

            placed.Add((key, rect with { Y = (int)Math.Min(floor, rect.Y) }));
        }

        return placed;
    }
}
