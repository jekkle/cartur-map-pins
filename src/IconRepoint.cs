using BepInEx.Configuration;
using UnityEngine;

namespace CarturMapPins
{
    /// Carries an icon change onto the pins that are already on the map.
    ///
    /// Changing an ore icon used to change only what FUTURE pins looked like. The hundred copper
    /// pins already out there kept the old artwork until somebody found out that
    /// `carturpins_reicon` exists and typed it into a console that needs a launch argument to
    /// open - which is to say, never.
    ///
    /// Two rules, and the second is what keeps this from being a nuisance:
    ///
    ///  - A pin still carrying the icon the mod itself last gave it is a leftover, not a choice.
    ///    It follows the change silently. Nobody picks a new icon and then wants the old one kept
    ///    on half the map.
    ///  - A pin whose icon was chosen by hand through the map's own picker is a choice. Those are
    ///    counted, and only if there are any is anything asked.
    ///
    /// Almost always the count is zero and the change simply happens.
    internal static class IconRepoint
    {
        /// Set by any icon setting changing; acted on at the next tick rather than inside the
        /// callback.
        ///
        /// This is the whole answer to two hazards at once. Applying a preset writes dozens of
        /// settings in one go, and a config file rewritten by a mod manager raises the event for
        /// every entry it touches - doing the work per event would mean dozens of passes and, far
        /// worse, dozens of stacked dialogs. Coalescing to one pass makes a preset ask once.
        private static bool _pending;
        private static bool _primed;

        /// True while our own popup is up, so a second change cannot stack another one on it.
        private static bool _asking;

        /// Per world: the baseline is primed again against the next world's records.
        public static void Reset() => _primed = false;

        public static void Watch(ConfigEntry<PinIcon> entry)
        {
            if (entry != null)
                entry.SettingChanged += (_, __) => _pending = true;
        }

        /// Called from the placer's throttled tick, which is the first place each frame that the
        /// map is known to be up and the record loaded.
        public static void Tick()
        {
            if (Minimap.instance == null || !CustomIcons.Ready)
                return;

            // The baseline has to exist before the first change, or every pin looks hand-picked
            // and the player is asked about all of them.
            if (!_primed)
            {
                _primed = true;
                PinRecord.PrimeIconBaseline();
                _pending = false;
                return;
            }

            if (!_pending || _asking)
                return;
            _pending = false;

            int moved = PinRecord.RepointChanged(out int handPicked);

            if (moved > 0)
                Plugin.Log.LogInfo($"Icon change: moved {moved} pin(s) onto their new icon.");

            if (handPicked > 0)
                Ask(handPicked);
        }

        /// The game's own dialog, for the same reason the sort buttons elsewhere are clones of the
        /// game's own: it already handles gamepad focus, pausing and the escape key, and it looks
        /// like Valheim because it is Valheim. IsAvailable is false while another popup is up, and
        /// then the honest thing is to leave the hand-picked pins alone rather than queue a box
        /// the player did not expect.
        private static void Ask(int handPicked)
        {
            if (!UnifiedPopup.IsAvailable())
            {
                Plugin.Log.LogInfo($"{handPicked} pin(s) use an icon you picked yourself and were left as they are.");
                return;
            }

            _asking = true;
            UnifiedPopup.Push(new YesNoPopup(
                "Pin icons",
                $"{handPicked} pin(s) use an icon you picked yourself.\nChange those to the new icon as well?",
                () =>
                {
                    UnifiedPopup.Pop();
                    _asking = false;
                    int forced = PinRecord.RepointHandPicked();
                    Plugin.Log.LogInfo($"Icon change: moved {forced} hand-picked pin(s) as well.");
                },
                () =>
                {
                    UnifiedPopup.Pop();
                    _asking = false;
                },
                localizeText: false,
                coverBackground: false));
        }
    }
}
