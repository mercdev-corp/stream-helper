using System.Drawing;
using System.Windows.Forms;

namespace StreamHelper.Shared.UI;

/// <summary>
/// Helper utilities for validating and restoring on-screen window coordinates across single and multi-monitor setups.
/// </summary>
public static class WindowPositionHelper
{
    public const int DefaultMinVisibleWidth = 50;
    public const int DefaultMinVisibleHeight = 30;

    /// <summary>
    /// Checks whether a window of the given position and size intersects any active screen working area
    /// by at least the specified minimum visible width and height.
    /// </summary>
    public static bool IsOnScreen(
        int x,
        int y,
        int width,
        int height,
        IEnumerable<Rectangle>? screenAreas = null,
        int minVisibleWidth = DefaultMinVisibleWidth,
        int minVisibleHeight = DefaultMinVisibleHeight)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var windowRect = new Rectangle(x, y, width, height);
        var screens = screenAreas ?? Screen.AllScreens.Select(s => s.WorkingArea);

        int requiredWidth = Math.Min(width, minVisibleWidth);
        int requiredHeight = Math.Min(height, minVisibleHeight);

        foreach (var screen in screens)
        {
            var intersection = Rectangle.Intersect(windowRect, screen);
            if (intersection.Width >= requiredWidth && intersection.Height >= requiredHeight)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether a window of the given location and size is visible on screen.
    /// </summary>
    public static bool IsOnScreen(
        Point location,
        Size size,
        IEnumerable<Rectangle>? screenAreas = null,
        int minVisibleWidth = DefaultMinVisibleWidth,
        int minVisibleHeight = DefaultMinVisibleHeight)
    {
        return IsOnScreen(location.X, location.Y, size.Width, size.Height, screenAreas, minVisibleWidth, minVisibleHeight);
    }

    /// <summary>
    /// Restores a form's position using the provided nullable coordinates if valid and visible on screen.
    /// If coordinates are null or off-screen, sets StartPosition to CenterScreen.
    /// </summary>
    /// <returns>True if the saved coordinates were valid and applied; false if falling back to CenterScreen.</returns>
    public static bool RestorePosition(
        Form form,
        int? x,
        int? y,
        IEnumerable<Rectangle>? screenAreas = null,
        int minVisibleWidth = DefaultMinVisibleWidth,
        int minVisibleHeight = DefaultMinVisibleHeight)
    {
        ArgumentNullException.ThrowIfNull(form);

        int width = form.Width > 0 ? form.Width : form.ClientSize.Width;
        int height = form.Height > 0 ? form.Height : form.ClientSize.Height;

        if (x.HasValue && y.HasValue && IsOnScreen(x.Value, y.Value, width, height, screenAreas, minVisibleWidth, minVisibleHeight))
        {
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(x.Value, y.Value);
            return true;
        }

        form.StartPosition = FormStartPosition.CenterScreen;
        return false;
    }
}
