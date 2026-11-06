using System;
using CarturMapPins;
using Xunit;

public class ZoomMathTests
{
    static float Run(float from, float to, float seconds, float dt, float tau)
    {
        float z = from;
        for (float t = 0; t < seconds; t += dt)
            z = ZoomMath.Step(z, to, dt, tau);
        return z;
    }

    [Theory]
    [InlineData(0.004f)] [InlineData(0.008f)] [InlineData(0.0167f)] [InlineData(0.033f)] [InlineData(0.05f)]
    public void ArrivesWithinTwiceTheDuration(float dt)
    {
        Assert.Equal(0.2f, Run(0.8f, 0.2f, 0.32f + dt, dt, ZoomMath.Tau(160)));
    }

    static float z(int steps, float dt, float tau)
    {
        float v = 1f;
        for (int i = 0; i < steps; i++) v = ZoomMath.Step(v, 0.1f, dt, tau);
        return v;
    }

    [Fact]
    public void SameRealTimeSameZoomAtAnyFrameRate()
    {
        float tau = ZoomMath.Tau(160);
        float a = z(60, 0.0016f, tau), b = z(6, 0.016f, tau);
        Assert.InRange(a / b, 0.998f, 1.002f);
    }

    [Fact]
    public void NeverOvershoots()
    {
        float z = 1f, tau = ZoomMath.Tau(160);
        for (int i = 0; i < 200; i++)
        {
            float next = ZoomMath.Step(z, 0.1f, 0.05f, tau);
            Assert.InRange(next, 0.1f, z);
            z = next;
        }
    }

    [Fact]
    public void RetargetMidFlightIsContinuousAndReverses()
    {
        float tau = ZoomMath.Tau(160), z = Run(1f, 0.1f, 0.05f, 0.005f, tau);
        float after = ZoomMath.Step(z, 0.5f, 0.005f, tau);
        Assert.True(after > z && after - z < 0.05f);
    }

    [Fact]
    public void ZeroDurationIsInstant() => Assert.Equal(0.3f, ZoomMath.Step(1f, 0.3f, 0.016f, ZoomMath.Tau(0)));

    [Fact]
    public void OneNotchInThenOutReturns()
    {
        float f = 1.06f, t = 0.37f;
        Assert.Equal(t, t / f * f, 5);
    }

    [Fact]
    public void AnchorKeepsWorldPointUnderCursor()
    {
        var r = new Random(7);
        for (int i = 0; i < 1000; i++)
        {
            float cx = (float)r.NextDouble() * 20000 - 10000, cz = (float)r.NextDouble() * 20000 - 10000;
            float nx = (float)r.NextDouble(), ny = (float)r.NextDouble(), aspect = 1.2f + (float)r.NextDouble();
            float zo = 0.015f + (float)r.NextDouble(), zn = 0.015f + (float)r.NextDouble(), span = 2048 * 8;
            ZoomMath.WorldUnder(cx, cz, nx, ny, aspect, zo, span, out float wx, out float wz);
            ZoomMath.CentreFor(wx, wz, nx, ny, aspect, zn, span, out float c2x, out float c2z);
            ZoomMath.WorldUnder(c2x, c2z, nx, ny, aspect, zn, span, out float vx, out float vz);
            Assert.InRange(Math.Abs(vx - wx), 0, 0.5);
            Assert.InRange(Math.Abs(vz - wz), 0, 0.5);
        }
    }

    [Fact]
    public void NoDriftOverAlternatingNotches()
    {
        float span = 2048 * 8, aspect = 1.78f, nx = 0.8f, ny = 0.2f, cx = 100, cz = -200, z = 0.3f;
        ZoomMath.WorldUnder(cx, cz, nx, ny, aspect, z, span, out float wx, out float wz);
        for (int i = 0; i < 200; i++)
        {
            z = i % 2 == 0 ? z / 1.06f : z * 1.06f;
            ZoomMath.CentreFor(wx, wz, nx, ny, aspect, z, span, out cx, out cz);
        }
        ZoomMath.WorldUnder(cx, cz, nx, ny, aspect, z, span, out float vx, out float vz);
        Assert.InRange(Math.Abs(vx - wx), 0, 0.5);
        Assert.InRange(Math.Abs(vz - wz), 0, 0.5);
    }
    const float Min = 0.015f, Max = 1f;

    [Fact]
    public void LogScaleRoundTrips()
    {
        foreach (float p in new[] { 0f, 2f, 20f, 63f, 99.5f, 100f })
            Assert.InRange(ZoomMath.LogPercent(ZoomMath.ZoomAtLogPercent(p, Min, Max), Min, Max), p - 0.01f, p + 0.01f);
    }

    [Fact]
    public void OldValuesKeepTheirZoom()
    {
        // totya's 2.0 and the old default 20, on the straight scale, and their zoom
        foreach (float old in new[] { 0f, 2f, 3f, 10f, 20f, 50f })
        {
            float zoom = Min + old / 100f * (Max - Min);
            float converted = ZoomMath.OldPercentToLog(old, Min, Max);
            Assert.InRange(ZoomMath.ZoomAtLogPercent(converted, Min, Max) / zoom, 0.999f, 1.001f);
        }
        Assert.Equal(100f, ZoomMath.OldPercentToLog(100f, Min, Max));
        Assert.InRange(ZoomMath.OldPercentToLog(2f, Min, Max), 19.9f, 20.1f);
        Assert.InRange(ZoomMath.OldPercentToLog(20f, Min, Max), 62.9f, 63.2f);
    }

    [Fact]
    public void OneNotchIsAboutOnePointFour()
    {
        float a = ZoomMath.LogPercent(0.0347f, Min, Max), b = ZoomMath.LogPercent(0.0347f * 1.06f, Min, Max);
        Assert.InRange(b - a, 1.3f, 1.5f);
    }

    [Fact]
    public void WorldClampLeavesCloseViewsAloneAndCentresAWorldSizedView()
    {
        ZoomMath.ClampToWorld(9000f, 4000f, 3000f, 10500f, out float x, out float z);
        Assert.Equal((9000f, 4000f), (x, z));
        ZoomMath.ClampToWorld(9000f, 4000f, 10500f, 10500f, out x, out z);
        Assert.Equal((0f, 0f), (x, z));
        ZoomMath.ClampToWorld(9000f, 0f, 7875f, 10500f, out x, out z);   // halfway through the fade: allowed 5250
        Assert.Equal(5250f, x, 1);
        Assert.Equal(0f, z);
    }
}
