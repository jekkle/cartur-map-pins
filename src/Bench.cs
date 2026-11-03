#if DIAGNOSTICS || BENCH
using System.Diagnostics;

namespace CarturMapPins
{
    /// Timing for the pilot's bench command; built into the Debug build only. One slot per
    /// stage of the map update, in Stopwatch ticks, with how often it ran and its worst single run.
    internal static class Bench
    {
        internal static readonly string[] Names =
            { "UpdatePins postfix", "Crowding.Measure", "Clusters.Measure", "LabelCrowding.Apply", "PinHover.Find", "PinFade.Step", "LabelCrowding.Step", "labels: ScaleLabels", "labels: before the loop (HideAll, HideOutOfSight)", "labels: grouping loop", "labels: sort" };

        internal static readonly long[] Ticks = new long[11], Worst = new long[11];
        internal static readonly int[] Runs = new int[11];

        internal static long Start() => Stopwatch.GetTimestamp();

        internal static void Stop(int slot, long t0)
        {
            long t = Stopwatch.GetTimestamp() - t0;
            Ticks[slot] += t;
            Runs[slot]++;
            if (t > Worst[slot])
                Worst[slot] = t;
        }

        internal static void Reset()
        {
            System.Array.Clear(Ticks, 0, Ticks.Length);
            System.Array.Clear(Worst, 0, Worst.Length);
            System.Array.Clear(Runs, 0, Runs.Length);
        }
    }
}
#endif
