using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;

namespace CarturMapPins
{
    /// Makes the names this mod invents translatable. Requested by three users.
    ///
    /// Most pin labels are already language-correct: traders, chests, wisps, beehives, runestones,
    /// pickables and spawned creatures are read off the component's own m_name, a "$token" the
    /// game resolves into the player's language on the way to the map.
    ///
    /// What did not translate is the text this mod writes itself - the Subtypes tables. "Sunken
    /// Crypt", "Frost Cave", "Infested Citadel", "Copper" are names the mod chose, and the game
    /// has no token for most of them.
    ///
    /// So those names are passed around as tokens the mod owns, with English registered as the
    /// default. Everything downstream already runs labels through Labels.Localize, so once a token
    /// is registered the whole chain works unchanged - including PinRecord.Relabel, which
    /// re-words existing pins when the language changes.
    ///
    /// Read off assembly_guiutils.dll, because three details decide the shape of this file:
    ///  1. Localization.AddWord(key, value) is private, and is exactly
    ///     "m_translations.Remove(key); m_translations.Add(key, value)". The key carries no "$" -
    ///     the dollar is stripped by FindNextWord before Translate sees the word.
    ///  2. Localization.Translate returns "[" + word + "]" for a word it does not know. So a token
    ///     that was never registered renders as "[carturpins_frost_cave]" on the map. Every token
    ///     this mod emits must therefore be registered with an English default, and Token()
    ///     refuses to hand out one that was not.
    ///  3. Localize caches its results in an LRUCache, so words added after a string was first
    ///     localized would not show. LRUCache.EvictAll() is public and clears it.
    internal static class Translations
    {
        /// Namespaced so a mod word can never collide with one of the game's own.
        public const string Prefix = "carturpins_";

        private static readonly MethodInfo AddWordMethod =
            AccessTools.Method(typeof(Localization), "AddWord", new[] { typeof(string), typeof(string) });

        private static readonly FieldInfo CacheField =
            AccessTools.Field(typeof(Localization), "m_cache");

        /// key -> the English text, which is also the default when no translation is supplied.
        private static readonly Dictionary<string, string> English =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// Names that reached Token() before Register ran, or that are in no table. Kept so the
        /// warning is logged once per name rather than once per pin.
        private static readonly HashSet<string> Unregistered = new HashSet<string>(StringComparer.Ordinal);

        private static bool _built;

        /// "Sunken Crypt" -> "sunken_crypt". Derived rather than written down: the tables hold 161
        /// entries and a hand-maintained second column would drift the first time a name was edited.
        public static string Key(string name)
        {
            if (string.IsNullOrEmpty(name))
                return string.Empty;

            var sb = new StringBuilder(name.Length);
            bool lastUnderscore = false;
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                    lastUnderscore = false;
                }
                else if (!lastUnderscore && sb.Length > 0)
                {
                    sb.Append('_');
                    lastUnderscore = true;
                }
            }

            return sb.ToString().TrimEnd('_');
        }

        /// The token for a mod-authored name, or the name itself when no word is registered for it.
        /// An unregistered token would be drawn as "[carturpins_something]" on the map, which is
        /// worse than plain English.
        public static string Token(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            string key = Prefix + Key(name);
            if (English.ContainsKey(key))
                return "$" + key;

            if (Unregistered.Add(name))
                Plugin.Log.LogWarning($"No translation key for pin name \"{name}\" - it will stay English. Add it to a Subtypes table.");
            return name;
        }

        /// Collects every name the mod can put on a pin.
        private static void Build()
        {
            if (_built)
                return;
            _built = true;

            foreach (Subtypes.Entry[] table in Subtypes.AllTables)
            {
                foreach (Subtypes.Entry e in table)
                {
                    string key = Prefix + Key(e.Name);
                    if (!string.IsNullOrEmpty(key) && !English.ContainsKey(key))
                        English[key] = e.Name;
                }
            }

            // Written straight into ResolveLabel, not from a table, so it would otherwise be the one
            // auto-pin name with no way to translate it.
            English[Prefix + Key(Leviathan)] = Leviathan;
        }

        public const string Leviathan = "Leviathan";

        /// Registers English, then lets a translation file for the active language override it.
        ///
        /// Safe to call repeatedly - AddWord replaces rather than appends. It runs again on every
        /// language change because Localization reloads its table then, dropping anything a mod
        /// added.
        public static void Register()
        {
            if (Plugin.Log == null || AddWordMethod == null)
                return;

            Build();

            Dictionary<string, string> overrides = LoadOverrides(Localization.instance.GetSelectedLanguage());

            var args = new object[2];
            int translated = 0;
            foreach (KeyValuePair<string, string> pair in English)
            {
                string text = pair.Value;
                if (overrides != null && overrides.TryGetValue(pair.Key, out string custom) && !string.IsNullOrEmpty(custom))
                {
                    text = custom;
                    translated++;
                }

                args[0] = pair.Key;
                args[1] = text;
                AddWordMethod.Invoke(Localization.instance, args);
            }

            // Anything localized before this point (a label written on the main menu, or the previous
            // language's version of the same token) would be handed back unchanged from the cache.
            // EvictAll is public on LRUCache.
            (CacheField?.GetValue(Localization.instance) as LRUCache<string>)?.EvictAll();

            Plugin.Log.LogInfo(translated > 0
                ? $"Pin names: {English.Count} registered, {translated} translated from {FileName(Localization.instance.GetSelectedLanguage())}."
                : $"Pin names: {English.Count} registered in English.");
        }

        private static string FileName(string language) => $"carturpins_names_{language}.txt";

        /// A translation file sits next to the config: one "key=text" per line, "#" for comments,
        /// UTF-8. Deliberately not JSON - a translator can edit it in Notepad, and a missing comma
        /// does not cost them the whole file.
        ///
        /// A key that is absent, blank, or unknown keeps its English, so a partial translation
        /// still works.
        private static Dictionary<string, string> LoadOverrides(string language)
        {
            string directory = Paths.ConfigPath;
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(language))
                return null;

            WriteTemplate(directory);

            string path = Path.Combine(directory, FileName(language));
            if (!File.Exists(path))
                return null;

            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not read {path}: {e.Message}. Pin names stay English.");
                return null;
            }

            return map;
        }

        /// The English list, written once so a translator has the keys to work from - copy it to
        /// carturpins_names_<language>.txt and translate the right-hand side.
        ///
        /// Never overwritten: somebody might have edited it in place before realising it is the
        /// template.
        private static void WriteTemplate(string directory)
        {
            string path = Path.Combine(directory, "carturpins_names_english_template.txt");
            if (File.Exists(path))
                return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Cartur's Map Pins - pin name translations.");
                sb.AppendLine("# Copy this file to carturpins_names_<language>.txt, using the same");
                sb.AppendLine("# language name Valheim uses (swedish, german, french, russian...),");
                sb.AppendLine("# and translate the text to the right of each '='.");
                sb.AppendLine("# Anything you leave alone, delete, or get wrong stays English.");
                sb.AppendLine("# Save as UTF-8.");
                sb.AppendLine();

                var keys = new List<string>(English.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys)
                    sb.AppendLine(key + "=" + English[key]);

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Plugin.Log.LogInfo($"Wrote translation template to {path}.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not write the translation template: {e.Message}");
            }
        }
    }

    /// Puts the mod's words back whenever the game rebuilds its own.
    ///
    /// Localization.SetupLanguage repopulates m_translations from the game's CSVs, so every word
    /// added through AddWord is gone the moment it runs, at startup as well as on a language
    /// change. Patching it makes registration order irrelevant: this mod's Awake can run before
    /// or after the language is set up and the words land either way.
    ///
    /// OnLanguageChange alone is not enough: it announces a change, and the first setup is not
    /// a change.
    [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
    internal static class Patch_Localization_SetupLanguage
    {
        private static void Postfix() => Translations.Register();
    }
}
