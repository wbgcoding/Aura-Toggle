using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using Xunit;

namespace AuraToggle.Tests;

public class ColourTests
{
    private const float Tolerance = 0.75f;

    public static IEnumerable<object[]> RedChannelRows() =>
        Enumerable.Range(0, 16).Select(i => new object[] { i * 17 });

    private static void AssertClose(Color expected, Color actual, int allowed = 1)
    {
        Assert.True(Math.Abs(expected.R - actual.R) <= allowed, $"R {expected} vs {actual}");
        Assert.True(Math.Abs(expected.G - actual.G) <= allowed, $"G {expected} vs {actual}");
        Assert.True(Math.Abs(expected.B - actual.B) <= allowed, $"B {expected} vs {actual}");
    }

    private static void AssertSame(Color expected, Color actual) =>
        Assert.Equal(expected.ToArgb(), actual.ToArgb());

    [Theory]
    [MemberData(nameof(RedChannelRows))]
    public void HsvRoundTripStaysWithinOnePerChannel(int red)
    {
        for (int green = 0; green < 256; green += 17)
        {
            for (int blue = 0; blue < 256; blue += 17)
            {
                var colour = Color.FromArgb(red, green, blue);
                (double hue, double saturation, double value) = Theme.ToHsv(colour);
                Assert.InRange(hue, 0.0, 360.0);
                Assert.InRange(saturation, 0.0, 1.0);
                Assert.InRange(value, 0.0, 1.0);
                AssertClose(colour, Theme.FromHsv(hue, saturation, value));
            }
        }
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(60, 255, 255, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(180, 0, 255, 255)]
    [InlineData(240, 0, 0, 255)]
    [InlineData(300, 255, 0, 255)]
    public void HueGivesTheSixPrimaryAndSecondaryColours(double degrees, int r, int g, int b)
    {
        AssertSame(Color.FromArgb(r, g, b), Theme.Hue(degrees));
    }

    [Theory]
    [InlineData(360, 0)]
    [InlineData(720, 0)]
    [InlineData(-60, 300)]
    [InlineData(-360, 0)]
    [InlineData(-420, 300)]
    [InlineData(750, 30)]
    [InlineData(480, 120)]
    public void HueWrapsAroundTheCircle(double degrees, double equivalent)
    {
        AssertSame(Theme.Hue(equivalent), Theme.Hue(degrees));
    }

    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0, 0, 0)]
    [InlineData(0.0, 0.0, 1.0, 255, 255, 255)]
    [InlineData(200.0, 0.0, 1.0, 255, 255, 255)]
    [InlineData(0.0, 1.0, 1.0, 255, 0, 0)]
    [InlineData(0.0, 1.0, 0.0, 0, 0, 0)]
    [InlineData(90.0, 1.0, 0.0, 0, 0, 0)]
    public void FromHsvKnownValues(double hue, double saturation, double value, int r, int g, int b)
    {
        AssertSame(Color.FromArgb(r, g, b), Theme.FromHsv(hue, saturation, value));
    }

    [Theory]
    [InlineData(-1.0, 1.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(1.0, -1.0)]
    [InlineData(1.0, 3.0)]
    [InlineData(-5.0, -5.0)]
    [InlineData(50.0, 50.0)]
    [InlineData(double.MaxValue, 1.0)]
    public void FromHsvNeverThrowsForSaturationOrValueOutsideZeroToOne(double saturation, double value)
    {
        for (double hue = -400; hue <= 800; hue += 25)
        {
            Color colour = Theme.FromHsv(hue, saturation, value);
            Assert.InRange(colour.R, 0, 255);
            Assert.InRange(colour.G, 0, 255);
            Assert.InRange(colour.B, 0, 255);
        }
    }

    [Fact]
    public void FromHsvWithNegativeValueIsBlack()
    {
        AssertSame(Color.Black, Theme.FromHsv(120, 1.0, -1.0));
    }

    [Theory]
    [InlineData(255, 0, 0, 0.0)]
    [InlineData(0, 255, 0, 120.0)]
    [InlineData(0, 0, 255, 240.0)]
    [InlineData(255, 255, 0, 60.0)]
    [InlineData(0, 255, 255, 180.0)]
    [InlineData(255, 0, 255, 300.0)]
    [InlineData(255, 0, 128, 330.0)]
    public void ToHsvHueOfKnownColours(int r, int g, int b, double hue)
    {
        (double actual, double saturation, double value) = Theme.ToHsv(Color.FromArgb(r, g, b));
        Assert.Equal(hue, actual, 0.5);
        Assert.Equal(1.0, saturation, 0.001);
        Assert.Equal(1.0, value, 0.001);
    }

    [Theory]
    [InlineData(0, 0, 0, 0.0)]
    [InlineData(255, 255, 255, 1.0)]
    [InlineData(51, 51, 51, 0.2)]
    public void ToHsvOfGreysHasNoHueOrSaturation(int r, int g, int b, double value)
    {
        (double hue, double saturation, double actual) = Theme.ToHsv(Color.FromArgb(r, g, b));
        Assert.Equal(0.0, hue);
        Assert.Equal(0.0, saturation);
        Assert.Equal(value, actual, 0.001);
    }

    [Fact]
    public void ToHsvHueIsNeverNegative()
    {
        // Colours whose red is the largest and blue exceeds green give a negative angle before wrapping.
        for (int blue = 0; blue < 256; blue += 5)
        {
            (double hue, _, _) = Theme.ToHsv(Color.FromArgb(255, 0, blue));
            Assert.InRange(hue, 0.0, 360.0);
        }
    }

    [Theory]
    [InlineData("#fff", false)]
    [InlineData("fff", false)]
    [InlineData("#FFFFFF", true)]
    [InlineData("FFFFFF", true)]
    [InlineData("ffffff", true)]
    [InlineData("#ffffff", true)]
    [InlineData("#AbCdEf", true)]
    [InlineData("0000", false)]
    [InlineData("#0000", false)]
    [InlineData("12345", false)]
    [InlineData("#12345", false)]
    [InlineData("1234567", false)]
    [InlineData("#1234567", false)]
    [InlineData("12345678", false)]
    [InlineData("#12345678", false)]
    [InlineData("gggggg", false)]
    [InlineData("#12345g", false)]
    [InlineData("0x1234", false)]
    [InlineData("", false)]
    [InlineData("#", false)]
    [InlineData("   ", false)]
    [InlineData("  #ff00aa  ", true)]
    [InlineData("ff 00aa", false)]
    [InlineData("# ff00aa", false)]
    [InlineData(" 12345", false)]
    [InlineData("+12345", false)]
    [InlineData("-12345", false)]
    [InlineData("12345\t", false)]
    [InlineData("１２３４５６", false)]
    public void TryParseHexTable(string text, bool expected)
    {
        Assert.Equal(expected, Theme.TryParseHex(text, out _));
    }

    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("ff0000", 255, 0, 0)]
    [InlineData("#00FF00", 0, 255, 0)]
    [InlineData("0000ff", 0, 0, 255)]
    [InlineData("#12aB9c", 0x12, 0xAB, 0x9C)]
    [InlineData(" #010203 ", 1, 2, 3)]
    public void TryParseHexReadsRedGreenBlueInOrder(string text, int r, int g, int b)
    {
        Assert.True(Theme.TryParseHex(text, out Color colour));
        AssertSame(Color.FromArgb(r, g, b), colour);
        Assert.Equal(255, colour.A);
    }

    [Fact]
    public void TryParseHexFailureLeavesNoColour()
    {
        Assert.False(Theme.TryParseHex("nope", out Color colour));
        Assert.Equal(default, colour);
    }

    [Fact]
    public void TryParseHexIgnoresTheCurrentCulture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            Assert.True(Theme.TryParseHex("#ABCDEF", out Color colour));
            AssertSame(Color.FromArgb(0xAB, 0xCD, 0xEF), colour);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(0.0, 0, 0, 0)]
    [InlineData(1.0, 100, 150, 200)]
    [InlineData(0.5, 50, 75, 100)]
    [InlineData(2.0, 200, 255, 255)]
    [InlineData(10.0, 255, 255, 255)]
    [InlineData(-1.0, 0, 0, 0)]
    [InlineData(-100.0, 0, 0, 0)]
    public void ScaleMultipliesAndClamps(double factor, int r, int g, int b)
    {
        AssertSame(Color.FromArgb(r, g, b), Theme.Scale(Color.FromArgb(100, 150, 200), factor));
    }

    [Fact]
    public void ScaleDropsTheAlphaChannel()
    {
        Assert.Equal(255, Theme.Scale(Color.FromArgb(10, 100, 100, 100), 1.0).A);
    }

    [Theory]
    [InlineData(0.0, 100, 100, 100)]
    [InlineData(1.0, 120, 140, 160)]
    [InlineData(0.5, 110, 120, 130)]
    [InlineData(2.0, 140, 180, 220)]
    [InlineData(-1.0, 80, 60, 40)]
    public void BlendMovesTowardsTheOtherColour(double amount, int r, int g, int b)
    {
        // Amounts outside 0..1 extrapolate; these rows stay inside 0..255 per channel.
        AssertSame(Color.FromArgb(r, g, b), Theme.Blend(Color.FromArgb(100, 100, 100), Color.FromArgb(120, 140, 160), amount));
    }

    [Fact]
    public void BlendEndsAreTheTwoColoursExactly()
    {
        for (int v = 0; v < 256; v += 51)
        {
            var a = Color.FromArgb(v, 255 - v, 7);
            var b = Color.FromArgb(255 - v, v, 200);
            AssertSame(a, Theme.Blend(a, b, 0.0));
            AssertSame(b, Theme.Blend(a, b, 1.0));
        }
    }

    [Fact]
    public void BlendStaysBetweenTheTwoColoursForAmountsInsideZeroToOne()
    {
        var a = Color.FromArgb(0, 255, 40);
        var b = Color.FromArgb(255, 0, 90);
        for (double amount = 0.0; amount <= 1.0; amount += 0.05)
        {
            Color mixed = Theme.Blend(a, b, amount);
            Assert.InRange(mixed.R, 0, 255);
            Assert.InRange(mixed.G, 0, 255);
            Assert.InRange(mixed.B, 40 - 1, 90 + 1);
        }
    }

    [Fact]
    public void LuminanceOrdersBlackGreyWhite()
    {
        double black = EffectPainter.Luminance(Color.Black);
        double grey = EffectPainter.Luminance(Color.Gray);
        double white = EffectPainter.Luminance(Color.White);
        Assert.Equal(0.0, black);
        Assert.Equal(1.0, white, 6);
        Assert.True(black < grey && grey < white);
    }

    [Fact]
    public void LuminanceWeighsGreenOverRedOverBlue()
    {
        double red = EffectPainter.Luminance(Color.FromArgb(255, 0, 0));
        double green = EffectPainter.Luminance(Color.FromArgb(0, 255, 0));
        double blue = EffectPainter.Luminance(Color.FromArgb(0, 0, 255));
        Assert.True(green > red && red > blue);
    }

    [Fact]
    public void LuminanceNeverDecreasesAlongTheGreyRamp()
    {
        double previous = -1;
        for (int v = 0; v < 256; v++)
        {
            double now = EffectPainter.Luminance(Color.FromArgb(v, v, v));
            Assert.True(now > previous);
            Assert.InRange(now, 0.0, 1.0);
            previous = now;
        }
    }

    public static IEnumerable<object[]> BoxRows()
    {
        foreach (float radius in new[] { 0f, -3f, 1f, 6f, 10f, 50f, 1000f })
        {
            yield return new object[] { 0f, 0f, 100f, 40f, radius };
            yield return new object[] { 10.5f, -20f, 30f, 30f, radius };
            yield return new object[] { 5f, 5f, 12f, 80f, radius };
        }
    }

    [Theory]
    [MemberData(nameof(BoxRows))]
    public void RoundedRectangleStaysInsideItsBounds(float x, float y, float width, float height, float radius)
    {
        var bounds = new RectangleF(x, y, width, height);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, radius);
        Assert.True(path.PointCount > 0);

        RectangleF actual = path.GetBounds();
        Assert.True(actual.Width > 0 && actual.Height > 0);
        Assert.True(actual.Left >= bounds.Left - Tolerance, $"left {actual} in {bounds}");
        Assert.True(actual.Top >= bounds.Top - Tolerance, $"top {actual} in {bounds}");
        Assert.True(actual.Right <= bounds.Right + Tolerance, $"right {actual} in {bounds}");
        Assert.True(actual.Bottom <= bounds.Bottom + Tolerance, $"bottom {actual} in {bounds}");

        // The path reaches all four sides of the box.
        Assert.True(actual.Left <= bounds.Left + Tolerance);
        Assert.True(actual.Top <= bounds.Top + Tolerance);
        Assert.True(actual.Right >= bounds.Right - Tolerance);
        Assert.True(actual.Bottom >= bounds.Bottom - Tolerance);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-4f)]
    public void RoundedRectangleWithoutRadiusIsAPlainRectangle(float radius)
    {
        var bounds = new RectangleF(2, 3, 50, 20);
        using GraphicsPath path = Theme.RoundedRectangle(bounds, radius);
        Assert.Equal(bounds, path.GetBounds());
        Assert.True(path.IsVisible(4f, 5f));
        Assert.True(path.IsVisible(50f, 21f));
    }

    [Fact]
    public void RoundedRectangleRoundsTheCorners()
    {
        using GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0, 0, 100, 40), 10);
        Assert.False(path.IsVisible(0.3f, 0.3f));
        Assert.False(path.IsVisible(99.7f, 39.7f));
        Assert.True(path.IsVisible(50, 20));
        Assert.True(path.IsVisible(10, 1));
    }

    [Fact]
    public void RoundedRectangleWithOversizedRadiusBecomesACapsule()
    {
        // A radius beyond half the short side is capped, so the ends are half circles.
        using GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0, 0, 100, 40), 500);
        Assert.True(path.IsVisible(50, 20));
        Assert.True(path.IsVisible(20, 20));
        Assert.False(path.IsVisible(1, 1));
        Assert.False(path.IsVisible(1, 39));
        Assert.False(path.IsVisible(99, 1));
    }

    [Theory]
    [InlineData(50f, 50f, 40f)]
    [InlineData(0f, 0f, 10f)]
    [InlineData(100.5f, 20.25f, 7.5f)]
    [InlineData(10f, 10f, 1f)]
    public void GearPathIsAnEightToothRingInsideItsRadius(float cx, float cy, float radius)
    {
        var centre = new PointF(cx, cy);
        using GraphicsPath path = EffectPainter.GearPath(centre, radius);
        Assert.True(path.PointCount > 0);

        RectangleF bounds = path.GetBounds();
        float slack = 0.01f;
        Assert.True(bounds.Left >= cx - radius - slack);
        Assert.True(bounds.Right <= cx + radius + slack);
        Assert.True(bounds.Top >= cy - radius - slack);
        Assert.True(bounds.Bottom <= cy + radius + slack);

        // Tips reach the full radius; the shape is centred on the given point.
        Assert.True(bounds.Width > radius * 1.8f);
        Assert.True(bounds.Height > radius * 1.8f);
        Assert.Equal(cx, bounds.Left + (bounds.Width / 2), 0.05);
        Assert.Equal(cy, bounds.Top + (bounds.Height / 2), 0.05);
    }

    [Fact]
    public void GearPathHasAnOpenCentreAndSolidBody()
    {
        using GraphicsPath path = EffectPainter.GearPath(new PointF(100, 100), 80);
        Assert.False(path.IsVisible(100, 100));
        Assert.True(path.IsVisible(100 + (80 * 0.55f), 100));
        Assert.True(path.IsVisible(100, 100 + (80 * 0.55f)));
        Assert.False(path.IsVisible(100 + 85, 100));
        Assert.Equal(FillMode.Alternate, path.FillMode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void GearPathHasATipAtEachOfTheEightTeeth(int tooth)
    {
        // A point on the rim of each tooth is inside the shape, the gap between two teeth is not.
        const float radius = 100f;
        double step = Math.PI * 2 / 8;
        using GraphicsPath path = EffectPainter.GearPath(new PointF(0, 0), radius);
        double angle = tooth * step;
        Assert.True(path.IsVisible((float)(Math.Cos(angle) * radius * 0.9), (float)(Math.Sin(angle) * radius * 0.9)));
        double gap = angle + (step / 2);
        Assert.False(path.IsVisible((float)(Math.Cos(gap) * radius * 0.9), (float)(Math.Sin(gap) * radius * 0.9)));
    }

    [Fact]
    public void RetintLeavesAColourOutsideBothPalettesAlone()
    {
        var odd = Color.FromArgb(1, 2, 3);
        AssertSame(odd, Theme.Retint(odd));
        AssertSame(Color.Transparent, Theme.Retint(Color.Transparent));
    }

    [Fact]
    public void RetintLeavesTheCurrentPalettesColoursAlone()
    {
        // Only a colour from the other theme is translated; today's own colours stay.
        AssertSame(Theme.Background, Theme.Retint(Theme.Background));
        AssertSame(Theme.Surface, Theme.Retint(Theme.Surface));
        AssertSame(Theme.Text, Theme.Retint(Theme.Text));
        AssertSame(Theme.Accent, Theme.Retint(Theme.Accent));
        AssertSame(Theme.Danger, Theme.Retint(Theme.Danger));
    }

    [Fact]
    public void ThemeColoursAreOpaqueAndTextContrastsWithTheBackground()
    {
        foreach (Color colour in new[]
        {
            Theme.Background, Theme.Surface, Theme.SurfaceHover, Theme.Border, Theme.Text, Theme.TextMuted,
            Theme.Accent, Theme.AccentSoft, Theme.Neutral, Theme.NeutralSoft, Theme.Danger,
        })
        {
            Assert.Equal(255, colour.A);
        }

        Assert.True(Math.Abs(EffectPainter.Luminance(Theme.Text) - EffectPainter.Luminance(Theme.Background)) > 0.4);
    }
}
