using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moneta.Core.Presentation;

namespace Moneta.Tests;

[TestClass]
public sealed class WindowPlacementTests
{
    private static readonly PixelRect Work = new(0, 0, 1280, 680);

    [TestMethod]
    public void OversizedCenteredWindowIsMovedBelowTheScreenTop()
    {
        var fitted = WindowPlacement.Fit(new PixelRect(0, -60, 1280, 800), Work, 780, 450, 1, 1);

        Assert.AreEqual(new PixelRect(0, 0, 1280, 680), fitted);
    }

    [TestMethod]
    public void NegativePositionIsClampedToTheWorkArea()
    {
        var fitted = WindowPlacement.Fit(new PixelRect(-120, -80, 900, 600), Work, 780, 450, 1, 1);

        Assert.AreEqual(new PixelRect(0, 0, 900, 600), fitted);
    }

    [TestMethod]
    public void ValidPositionIsPreservedExactly()
    {
        var desired = new PixelRect(120, 80, 900, 560);
        var fitted = WindowPlacement.Fit(desired, Work, 780, 450, 1, 1);

        Assert.AreEqual(desired, fitted);
    }

    [TestMethod]
    public void RightAndBottomOverflowAreClamped()
    {
        var fitted = WindowPlacement.Fit(new PixelRect(1000, 500, 800, 600), Work, 780, 450, 1, 1);

        Assert.AreEqual(new PixelRect(480, 80, 800, 600), fitted);
    }

    [TestMethod]
    public void NegativeMonitorCoordinatesAndDpiAreHandled()
    {
        var work = new PixelRect(-1920, 40, 1920, 1000);
        var fitted = WindowPlacement.Fit(new PixelRect(-2100, -60, 1000, 700), work, 780, 450, 1.5, 1.5);

        Assert.AreEqual(new PixelRect(-1920, 40, 1170, 700), fitted);
    }

    [TestMethod]
    public void MinimumSizeWinsButNeverExceedsTheWorkArea()
    {
        var fitted = WindowPlacement.Fit(new PixelRect(10, 10, 100, 100), Work, 900, 500, 1, 1);
        var tooLargeForScreen = WindowPlacement.Fit(new PixelRect(0, 0, 100, 100),
            new PixelRect(0, 0, 640, 360), 900, 500, 1, 1);

        Assert.AreEqual(new PixelRect(10, 10, 900, 500), fitted);
        Assert.AreEqual(new PixelRect(0, 0, 640, 360), tooLargeForScreen);
    }

    [TestMethod]
    public void InvalidDesiredRectangleFallsBackToUsableMinimum()
    {
        var fitted = WindowPlacement.Fit(new PixelRect(double.NaN, double.NaN, double.NaN, double.NaN),
            Work, 780, 450, 1, 1);

        Assert.AreEqual(new PixelRect(0, 0, 780, 450), fitted);
    }

    [DataTestMethod]
    [DataRow(1280, 680, 1280, 800, 1.0, 1280, 680)]
    [DataRow(1280, 680, 1600, 1000, 1.25, 1280, 680)]
    [DataRow(1366, 728, 1366, 800, 1.0, 1366, 728)]
    [DataRow(1366, 728, 1707, 1000, 1.25, 1366, 728)]
    [DataRow(1366, 728, 2049, 1200, 1.5, 1366, 728)]
    [DataRow(1920, 1040, 1920, 800, 1.0, 1920, 800)]
    [DataRow(1920, 1000, 2400, 1000, 1.25, 1920, 1000)]
    [DataRow(1920, 1000, 2880, 1200, 1.5, 1920, 1000)]
    [DataRow(1920, 1000, 2560, 1600, 2.0, 1920, 1000)]
    [DataRow(2560, 1360, 3840, 2160, 1.5, 2560, 1360)]
    [DataRow(2560, 1360, 5120, 2880, 2.0, 2560, 1360)]
    public void CommonLaptopAndDesktopWorkAreasClampTheWindow(int workWidth, int workHeight,
        int desiredWidth, int desiredHeight, double scale, int expectedWidth, int expectedHeight)
    {
        var fitted = WindowPlacement.Fit(
            new PixelRect(0, -100, desiredWidth, desiredHeight),
            new PixelRect(0, 0, workWidth, workHeight),
            780, 450, scale, scale);

        Assert.AreEqual(0, fitted.Left);
        Assert.AreEqual(0, fitted.Top);
        Assert.AreEqual(expectedWidth, fitted.Width);
        Assert.AreEqual(expectedHeight, fitted.Height);
    }

    [TestMethod]
    public void UsableGeometryRejectsZeroAndNonFiniteValues()
    {
        Assert.IsFalse(WindowPlacement.IsUsable(0, 0, 0, 500));
        Assert.IsFalse(WindowPlacement.IsUsable(0, 0, 500, double.PositiveInfinity));
        Assert.IsTrue(WindowPlacement.IsUsable(-500, -20, 800, 600));
    }
}
