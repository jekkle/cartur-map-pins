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
