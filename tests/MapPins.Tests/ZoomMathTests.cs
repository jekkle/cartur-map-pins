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
}
