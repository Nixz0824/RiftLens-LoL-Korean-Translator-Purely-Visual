using RiftLens.Models;

namespace RiftLens.UI;

public static class OverlayPlacementService
{
    private const int Gap = 10;

    public static Rectangle Calculate(
        AppSettings settings,
        Size desiredSize,
        OverlayPlacement? lockedAutoPlacement = null)
    {
        if (settings.OverlayPlacement == OverlayPlacement.Custom && settings.DisplayRegion.IsValid)
        {
            var custom = settings.DisplayRegion.ToRectangle();
            return new Rectangle(
                custom.X,
                custom.Y,
                Math.Min(desiredSize.Width, custom.Width),
                Math.Min(desiredSize.Height, custom.Height));
        }

        var capture = settings.CaptureRegion.ToRectangle();
        var screen = Screen.FromRectangle(capture).WorkingArea;
        var placements = BuildCandidates(capture, desiredSize);
        if (settings.OverlayPlacement == OverlayPlacement.Auto && lockedAutoPlacement is not null)
        {
            var locked = placements.First(candidate => candidate.Placement == lockedAutoPlacement.Value);
            return Fit(locked.Rectangle, screen);
        }

        var preferred = settings.OverlayPlacement == OverlayPlacement.Auto
            ? OverlayPlacement.Above
            : settings.OverlayPlacement;

        return placements
            .Select(candidate => new
            {
                Candidate = Fit(candidate.Rectangle, screen),
                Score = Score(candidate.Rectangle, capture, screen, candidate.Placement, preferred)
            })
            .OrderByDescending(item => item.Score)
            .First()
            .Candidate;
    }

    public static OverlayPlacement ResolveAutoPlacement(AppSettings settings, Size desiredSize)
    {
        var capture = settings.CaptureRegion.ToRectangle();
        var screen = Screen.FromRectangle(capture).WorkingArea;
        return BuildCandidates(capture, desiredSize)
            .Select(candidate => new
            {
                candidate.Placement,
                Score = Score(candidate.Rectangle, capture, screen, candidate.Placement, OverlayPlacement.Above)
            })
            .OrderByDescending(item => item.Score)
            .First()
            .Placement;
    }

    private static IEnumerable<(OverlayPlacement Placement, Rectangle Rectangle)> BuildCandidates(
        Rectangle capture,
        Size size)
    {
        yield return (OverlayPlacement.Above, new Rectangle(
            capture.Left,
            capture.Top - size.Height - Gap,
            size.Width,
            size.Height));
        yield return (OverlayPlacement.Below, new Rectangle(
            capture.Left,
            capture.Bottom + Gap,
            size.Width,
            size.Height));
        yield return (OverlayPlacement.Right, new Rectangle(
            capture.Right + Gap,
            capture.Top,
            size.Width,
            size.Height));
        yield return (OverlayPlacement.Left, new Rectangle(
            capture.Left - size.Width - Gap,
            capture.Top,
            size.Width,
            size.Height));
    }

    private static double Score(
        Rectangle candidate,
        Rectangle capture,
        Rectangle screen,
        OverlayPlacement placement,
        OverlayPlacement preferred)
    {
        var visible = Rectangle.Intersect(candidate, screen);
        var visibleRatio = visible.Width * visible.Height / (double)Math.Max(1, candidate.Width * candidate.Height);
        var overlap = Rectangle.Intersect(candidate, capture);
        var overlapRatio = overlap.Width * overlap.Height / (double)Math.Max(1, candidate.Width * candidate.Height);
        var screenCenter = new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2);
        var candidateCenter = new Point(candidate.Left + candidate.Width / 2, candidate.Top + candidate.Height / 2);
        var centerDistance = Math.Sqrt(
            Math.Pow(candidateCenter.X - screenCenter.X, 2)
            + Math.Pow(candidateCenter.Y - screenCenter.Y, 2));
        var centerSafety = Math.Min(1, centerDistance / Math.Max(1, screen.Width * 0.35));
        var preference = placement == preferred ? 0.4 : 0;
        return visibleRatio * 4 + centerSafety + preference - overlapRatio * 10;
    }

    private static Rectangle Fit(Rectangle rectangle, Rectangle bounds)
    {
        var width = Math.Min(rectangle.Width, bounds.Width);
        var height = Math.Min(rectangle.Height, bounds.Height);
        var x = Math.Clamp(rectangle.X, bounds.Left, Math.Max(bounds.Left, bounds.Right - width));
        var y = Math.Clamp(rectangle.Y, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - height));
        return new Rectangle(x, y, width, height);
    }
}
