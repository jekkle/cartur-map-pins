using System.Collections.Generic;
using UnityEngine;

namespace CarturMapPins
{
    /// The hide list: names you have asked not to see, kept out of the way rather than deleted.
    ///
    /// Turning a category or a group off only stops NEW pins; the ones already on the map stay,
    /// and the only cures were destructive (reset the world's pins, or delete each by hand). This
    /// hides instead: the pin stays in the save, stays searchable, and comes back the moment the
    /// name leaves the list. It also reaches pins this mod did not place (hand-placed, another
    /// mod's, or ones whose record was lost).
    ///
    /// Matching is on what a pin IS, not on what it says. Every pin this mod placed is recorded
    /// with two language-independent strings: the key ("Ore:Copper", category and subtype) and
    /// the source, the label before Localize ran ("Eber" says nothing about which token produced
    /// it). So "copper" and "copper ore" both find copper, and nothing breaks when the game
    /// language changes. A pin with no record has only its drawn, translated name.
    ///
    /// Every word you type must appear in the pin's identity. "ore" takes all ore and "core"
    /// takes all three cores, which is sometimes the point; the count shown beside each
    /// suggestion is what stops that being a surprise.
    internal static class PinHiding
    {
        /// Each entry is one typed term, already lower-cased and split into words. A pin is
        /// hidden when every word of any one term appears in its identity.
        private static readonly List<string[]> Terms = new List<string[]>();

        /// Position key -> the identity of the pin recorded there. Rebuilt only when the record
        /// changes size or the list is edited; the draw pass must not pay for building it.
        private static readonly Dictionary<long, string> Identities = new Dictionary<long, string>();
        private static int _indexedRecords = -1;

        private static readonly HashSet<Minimap.PinData> HiddenPins = new HashSet<Minimap.PinData>();

        /// Same position key PinStyles uses, for the same reason: a pin and its record are written
        /// from the same coordinates and a metre of rounding is plenty to pair them.
        private static long KeyFor(Vector3 pos) =>
            ((long)Mathf.RoundToInt(pos.x) << 32) ^ (uint)Mathf.RoundToInt(pos.z);

        public static bool Any => Terms.Count > 0;

        /// How many names are being hidden, for the map to say so somewhere visible: a hidden pin
        /// with nothing announcing it reads as a broken mod six months later.
        public static int TermCount => Terms.Count;

        /// Re-reads the setting. Called once when it changes, never per frame.
        public static void Reparse(string raw)
        {
            Terms.Clear();
            HiddenPins.Clear();
            _indexedRecords = -1;

            if (string.IsNullOrEmpty(raw))
                return;

            foreach (string piece in raw.Split(','))
            {
                string term = piece.Trim().ToLowerInvariant();
                if (term.Length == 0)
                    continue;

                string[] words = term.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0)
                    Terms.Add(words);
            }
        }

        /// Decides which pins are hidden, once per pass rather than once per frame. Called from
        /// the same place as Crowding.Measure: UpdatePins runs when the map moves, which is also
        /// when pins appear, so it is the cheapest moment that is never stale.
        public static void Apply(List<Minimap.PinData> pins)
        {
            HiddenPins.Clear();
            if (Terms.Count == 0 || pins == null)
                return;   // costs one int compare when nobody is hiding anything

            EnsureIndex();

            foreach (Minimap.PinData pin in pins)
            {
                if (pin == null)
                    continue;

                // Also the drawn name: a pin renamed by hand has an identity that no longer says
                // what the player calls it, and the hide list is typed by name.
                if (Matches(IdentityOf(pin)) ||
                    (!string.IsNullOrEmpty(pin.m_name) && Matches(pin.m_name.ToLowerInvariant())))
                    HiddenPins.Add(pin);
            }
        }

        public static bool IsHidden(Minimap.PinData pin) =>
            HiddenPins.Count > 0 && HiddenPins.Contains(pin);

        /// What this pin is, as one lower-case string to search.
        ///
        /// The record's key and source first, because they are English whatever the game is set
        /// to. The drawn name is the fallback for a pin with no record, and the one part a
        /// language change breaks.
        private static string IdentityOf(Minimap.PinData pin)
        {
            if (Identities.TryGetValue(KeyFor(pin.m_pos), out string identity))
                return identity;

            return pin.m_name == null ? string.Empty : pin.m_name.ToLowerInvariant();
        }

        private static bool Matches(string identity)
        {
            if (identity.Length == 0)
                return false;

            foreach (string[] words in Terms)
            {
                bool all = true;
                foreach (string word in words)
                {
                    if (identity.IndexOf(word, System.StringComparison.Ordinal) >= 0)
                        continue;
                    all = false;
                    break;
                }

                if (all)
                    return true;
            }

            return false;
        }

        /// Rebuilds the position -> identity table when the record has changed size.
        ///
        /// Count rather than a revision counter: every operation that adds or forgets a pin moves
        /// it, and the ones that do not (relabelling, repointing an icon) leave the key alone,
        /// which is what the match is mostly made of. Editing the list rebuilds it outright.
        private static void EnsureIndex()
        {
            int records = PinRecord.Count;
            if (records == _indexedRecords)
                return;

            _indexedRecords = records;
            Identities.Clear();

            foreach (KeyValuePair<Vector3, string> entry in PinRecord.AllIdentities())
                Identities[KeyFor(entry.Key)] = entry.Value;
        }

        /// What the player could hide, for the settings drawer to offer: every distinct pin name
        /// they have, with how many pins carry it.
        ///
        /// The count is the point: seeing that a name covers fifty pins tells you whether hiding
        /// it is worth doing before you commit.
        public static List<KeyValuePair<string, int>> Suggestions()
        {
            var counts = new Dictionary<string, int>(System.StringComparer.Ordinal);
            var result = new List<KeyValuePair<string, int>>();

            // Rebuilt unconditionally: the drawer runs on a menu the player opened, not per frame,
            // and an index that lagged a pin would offer a name that hides nothing.
            _indexedRecords = -1;
            EnsureIndex();

            // The record first, because it is the complete list: a file on disk naming every pin
            // this mod placed, where the live map list holds only what is loaded (and is reached
            // by reflection). Map-only suggestions left names off the menu.
            foreach (string label in PinRecord.AllLabels())
            {
                if (string.IsNullOrEmpty(label))
                    continue;
                counts.TryGetValue(label, out int n);
                counts[label] = n + 1;
            }

            // Then the map, for what the record does not know about (hand-placed, another mod's,
            // or a lost record). Same table, so a name in both is not listed twice.
            Minimap map = Minimap.instance;
            List<Minimap.PinData> pins = map == null ? null : MinimapAccess.GetPins(map);

            if (pins != null)
            {
                foreach (Minimap.PinData pin in pins)
                {
                    if (pin == null || string.IsNullOrEmpty(pin.m_name))
                        continue;
                    if (counts.ContainsKey(pin.m_name))
                        continue;   // already counted from the record

                    counts.TryGetValue(pin.m_name, out int n);
                    counts[pin.m_name] = n + 1;
                }
            }

            foreach (KeyValuePair<string, int> kv in counts)
                result.Add(kv);

            result.Sort((a, b) => b.Value != a.Value
                ? b.Value.CompareTo(a.Value)
                : string.Compare(a.Key, b.Key, System.StringComparison.Ordinal));
            return result;
        }
    }
}
