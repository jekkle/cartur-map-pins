using System;

namespace CarturMapPins
{
    /// The arithmetic of the smooth zoom, with no Unity types so it can be tested outside the game.
    internal static class ZoomMath
    {
        /// Within this much of the target (as a log ratio, 0.15%) the zoom lands on it. An exponential
        /// never arrives; without a stop the map would ask for a new pin pass on every frame forever.
        internal const float Arrive = 0.0015f;

        /// The time constant that makes the zoom cover 98% of the way in `durationMs`.
        internal static float Tau(float durationMs) => durationMs / 1000f / 3.9f;

        /// One step of an exponential approach, in log space, so zooming in and out are mirror
        /// images. Exact for any dt (the decay is analytic, not an Euler step), so the same
        /// real time gives the same zoom at 30 or 144 fps.
        internal static float Step(float current, float target, float dt, float tau)
        {
            if (tau <= 0f || current <= 0f || target <= 0f)
                return target;
            double left = Math.Log(target / current) * Math.Exp(-dt / tau);
            return Math.Abs(left) < Arrive ? target : (float)(target * Math.Exp(-left));
        }

        /// The zoom-limit settings are on a log scale: 0 is the closest zoom, 100 the furthest, and every
        /// point is the same ratio of zoom. Ten points is a factor of about 1.5 for the map's 0.015 to 1,
        /// and one 6% wheel notch is about 1.4 points, so the values that matter are not squeezed into
        /// the first two or three points of a straight percentage (totya, 2.1.3).
        internal static float LogPercent(float zoom, float min, float max) =>
            100f * (float)(Math.Log(zoom / min) / Math.Log(max / min));

        internal static float ZoomAtLogPercent(float percent, float min, float max) =>
            min * (float)Math.Pow(max / min, percent / 100f);

        /// A value saved on the old straight scale (0 to 100 of the way from min to max), as a log value.
        internal static float OldPercentToLog(float oldPercent, float min, float max)
        {
            if (oldPercent >= 100f)
                return 100f;
            if (oldPercent <= 0f)
                return 0f;
            return LogPercent(min + oldPercent / 100f * (max - min), min, max);
        }

        /// Keeps the world from sliding out of the view while zooming out about the pointer. Anchoring on the
        /// pointer moves the view centre away from where it started every notch (totya, 2.2.0: zooming out far,
        /// once the edge of the world shows, "we move farther and farther away from the map position where we
        /// started"; measured with the pointer in the upper right at 25% a notch: the centre ended 21 km from
        /// the start, outside the world, which is 21 km across). Once the view's half-height `half` passes half
        /// the world's radius the centre may only stray `radius * (1 - (half - radius/2) / (radius/2))` from the
        /// world's middle, which is the whole radius up to there and nothing once the view is as tall as the
        /// world. Where that line sits is a judgement of what feels natural, not a measured value.
        internal static void ClampToWorld(float cx, float cz, float half, float radius, out float ox, out float oz)
        {
            ox = cx;
            oz = cz;
            if (half <= 0.5f * radius)
                return;
            float allowed = radius * Math.Max(0f, 1f - (half - 0.5f * radius) / (0.5f * radius));
            float r = (float)Math.Sqrt(cx * cx + cz * cz);
            if (r <= allowed || r <= 0f)
                return;
            ox = cx * allowed / r;
            oz = cz * allowed / r;
        }

        /// A point outside the world's circle, brought to its edge. The pointer can be over the black beyond the
        /// world when zoomed out; anchoring a zoom-in on that point left the view centred outside the world.
        internal static void ClampPoint(float x, float z, float radius, out float ox, out float oz)
        {
            ox = x;
            oz = z;
            float r = (float)Math.Sqrt(x * x + z * z);
            if (r <= radius || r <= 0f)
                return;
            ox = x * radius / r;
            oz = z * radius / r;
        }

        /// The map image shows a window of the world, `aspect * zoom` by `zoom` texture-widths,
        /// centred on the view centre (Minimap.CenterMap sets uvRect that way), and the transform
        /// from texture to world is linear (Minimap.WorldToMapPoint). `span` is the world size of
        /// the whole texture, m_textureSize * m_pixelSize. (nx, ny) is a point in the image, 0..1.
        internal static void WorldUnder(float cx, float cz, float nx, float ny, float aspect, float zoom, float span,
                                        out float wx, out float wz)
        {
            wx = cx + (nx - 0.5f) * aspect * zoom * span;
            wz = cz + (ny - 0.5f) * zoom * span;
        }

        /// The view centre that puts world point (wx, wz) at (nx, ny) when the zoom is `zoom`.
        internal static void CentreFor(float wx, float wz, float nx, float ny, float aspect, float zoom, float span,
                                       out float cx, out float cz)
        {
            cx = wx - (nx - 0.5f) * aspect * zoom * span;
            cz = wz - (ny - 0.5f) * zoom * span;
        }
    }
}
