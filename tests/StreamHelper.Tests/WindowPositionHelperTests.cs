using System.Drawing;
using System.Windows.Forms;
using StreamHelper.Shared.UI;

namespace StreamHelper.Tests;

[TestClass]
public class WindowPositionHelperTests
{
    private static readonly Rectangle PrimaryScreen = new(0, 0, 1920, 1080);
    private static readonly Rectangle SecondaryLeftScreen = new(-1920, 0, 1920, 1080);
    private static readonly Rectangle SecondaryRightScreen = new(1920, 0, 1920, 1080);

    [TestMethod]
    public void IsOnScreen_FullyInsidePrimaryScreen_ReturnsTrue()
    {
        var screens = new[] { PrimaryScreen };
        bool result = WindowPositionHelper.IsOnScreen(100, 100, 400, 300, screens);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsOnScreen_PartiallyOnScreenWithSufficientMargin_ReturnsTrue()
    {
        var screens = new[] { PrimaryScreen };
        // 100px visible on the right edge of primary screen
        bool result = WindowPositionHelper.IsOnScreen(1850, 500, 400, 300, screens, minVisibleWidth: 50, minVisibleHeight: 30);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsOnScreen_BarelyOnScreenBelowMinMargin_ReturnsFalse()
    {
        var screens = new[] { PrimaryScreen };
        // Only 20px visible on the right (1920 - 1900 = 20), requires 50px
        bool result = WindowPositionHelper.IsOnScreen(1900, 500, 400, 300, screens, minVisibleWidth: 50, minVisibleHeight: 30);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsOnScreen_MultiMonitor_SecondaryLeftScreen_ReturnsTrue()
    {
        var screens = new[] { PrimaryScreen, SecondaryLeftScreen };
        // Positioned on the left monitor with negative X coordinates
        bool result = WindowPositionHelper.IsOnScreen(-1000, 200, 400, 300, screens);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsOnScreen_MultiMonitor_SecondaryRightScreen_ReturnsTrue()
    {
        var screens = new[] { PrimaryScreen, SecondaryRightScreen };
        // Positioned on the right monitor
        bool result = WindowPositionHelper.IsOnScreen(2100, 200, 400, 300, screens);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsOnScreen_CompletelyOffScreen_ReturnsFalse()
    {
        var screens = new[] { PrimaryScreen };
        bool resultLeft = WindowPositionHelper.IsOnScreen(-2000, 100, 400, 300, screens);
        bool resultRight = WindowPositionHelper.IsOnScreen(5000, 100, 400, 300, screens);
        bool resultAbove = WindowPositionHelper.IsOnScreen(100, -2000, 400, 300, screens);
        bool resultBelow = WindowPositionHelper.IsOnScreen(100, 3000, 400, 300, screens);

        Assert.IsFalse(resultLeft);
        Assert.IsFalse(resultRight);
        Assert.IsFalse(resultAbove);
        Assert.IsFalse(resultBelow);
    }

    [TestMethod]
    public void IsOnScreen_InvalidDimensions_ReturnsFalse()
    {
        var screens = new[] { PrimaryScreen };
        Assert.IsFalse(WindowPositionHelper.IsOnScreen(100, 100, 0, 300, screens));
        Assert.IsFalse(WindowPositionHelper.IsOnScreen(100, 100, 400, -10, screens));
    }

    [TestMethod]
    public void RestorePosition_ValidCoordinates_SetsManualAndLocation()
    {
        var screens = new[] { PrimaryScreen };
        using var form = new Form { Size = new Size(400, 300) };

        bool restored = WindowPositionHelper.RestorePosition(form, 250, 150, screens);

        Assert.IsTrue(restored);
        Assert.AreEqual(FormStartPosition.Manual, form.StartPosition);
        Assert.AreEqual(new Point(250, 150), form.Location);
    }

    [TestMethod]
    public void RestorePosition_NullCoordinates_SetsCenterScreen()
    {
        var screens = new[] { PrimaryScreen };
        using var form = new Form { Size = new Size(400, 300) };

        bool restoredNullBoth = WindowPositionHelper.RestorePosition(form, null, null, screens);
        Assert.IsFalse(restoredNullBoth);
        Assert.AreEqual(FormStartPosition.CenterScreen, form.StartPosition);

        bool restoredNullX = WindowPositionHelper.RestorePosition(form, null, 150, screens);
        Assert.IsFalse(restoredNullX);
        Assert.AreEqual(FormStartPosition.CenterScreen, form.StartPosition);

        bool restoredNullY = WindowPositionHelper.RestorePosition(form, 250, null, screens);
        Assert.IsFalse(restoredNullY);
        Assert.AreEqual(FormStartPosition.CenterScreen, form.StartPosition);
    }

    [TestMethod]
    public void RestorePosition_OffScreenCoordinates_SetsCenterScreen()
    {
        var screens = new[] { PrimaryScreen };
        using var form = new Form { Size = new Size(400, 300) };

        bool restored = WindowPositionHelper.RestorePosition(form, -5000, -5000, screens);

        Assert.IsFalse(restored);
        Assert.AreEqual(FormStartPosition.CenterScreen, form.StartPosition);
    }
}
