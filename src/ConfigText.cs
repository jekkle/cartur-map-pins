using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace CarturMapPins
{
    /// What a setting says about itself, and what it is called, in the player's language.
    ///
    /// Every setting is shown as one plain sentence (ConfigPlain), a blank line, then the long description.
    /// The plain sentence is what a hover shows first in a configuration manager and what leads the .cfg
    /// comment (the owner, 2.5). The long text is the English one in the code, or the translated one.
    ///
    /// Translations live in the pin-name translation file, which players already have, as lines:
    ///   cfg.&lt;Key&gt;.name=    the label Configuration Manager (F1) shows (the key in the .cfg cannot change)
    ///   cfg.&lt;Key&gt;.desc=    the long description, in the .cfg and the mod manager
    ///   cfg.&lt;Key&gt;.plain=   the one-sentence version, shown first
    ///   cat.&lt;Page&gt;=        a page's title, keeping its number so the pages stay in order
    ///   val.&lt;Key&gt;.&lt;Value&gt;= a choice's label ("val.ClusterClickAction.Inspector=Lista")
    /// The names and descriptions cover the pages that came from the old General page (the per-kind
    /// switches and icons are named after the pins, which are translated already). Page titles and plain
    /// sentences cover every setting. A choice's value is stored under its English name whatever it is
    /// shown as, so translating it can never change what a saved file means.
    ///
    /// ConfigEntryBase.Description is read-only, so the entry's description object is replaced
    /// through its backing field. The originals are kept so a switch back to English restores them.
    internal static class ConfigText
    {
        private static readonly System.Reflection.FieldInfo DescriptionField =
            AccessTools.Field(typeof(ConfigEntryBase), "<Description>k__BackingField");

        private static readonly Dictionary<ConfigEntryBase, ConfigDescription> Original =
            new Dictionary<ConfigEntryBase, ConfigDescription>();

        /// "Key.Value" to its label, for the language in force.
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);

        /// The entry's Configuration Manager tag, added when it has none. The live tag: the description
        /// is replaced when a language is applied, so this is looked up each time, not kept.
        public static ConfigurationManagerAttributes TagOf(ConfigEntryBase entry)
        {
            if (entry == null || DescriptionField == null)
                return null;
            ConfigDescription d = entry.Description;
            foreach (object tag in d.Tags)
                if (tag is ConfigurationManagerAttributes a)
                    return a;

            var added = new ConfigurationManagerAttributes();
            var tags = new List<object>(d.Tags) { added };
            DescriptionField.SetValue(entry, new ConfigDescription(d.Description, d.AcceptableValues, tags.ToArray()));
            return added;
        }

        public static void Apply(ConfigFile config, string language)
        {
            if (config == null || DescriptionField == null)
                return;

            Dictionary<string, string> text = Translations.LoadOverrides(language) ?? new Dictionary<string, string>();
            Values.Clear();
            foreach (KeyValuePair<string, string> line in text)
                if (line.Key.StartsWith("val.", StringComparison.Ordinal) && !string.IsNullOrEmpty(line.Value))
                    Values[line.Key.Substring(4)] = line.Value;

            int changed = 0;
            var lacking = new List<string>();
            foreach (ConfigDefinition def in new List<ConfigDefinition>(config.Keys))
            {
                ConfigEntryBase entry = config[def];
                if (!Original.TryGetValue(entry, out ConfigDescription english))
                    Original[entry] = english = entry.Description;

                bool general = Sections.IsGeneral(def.Section);
                string desc = null, name = null;
                if (general)
                {
                    text.TryGetValue("cfg." + def.Key + ".desc", out desc);
                    text.TryGetValue("cfg." + def.Key + ".name", out name);
                }
                bool hidden = false;
                foreach (object tag in english.Tags)
                    if (tag is ConfigurationManagerAttributes a && a.Browsable == false)
                        hidden = true;

                string plain = null;
                if (!hidden)
                {
                    if (!text.TryGetValue("cfg." + def.Key + ".plain", out plain) || string.IsNullOrEmpty(plain))
                        plain = ConfigPlain.For(Sections.LogicalOf(def.Section), def.Key);
                    if (string.IsNullOrEmpty(plain))
                        lacking.Add(def.Section + "/" + def.Key);
                }
                text.TryGetValue("cat." + def.Section, out string category);

                bool choices = false;
                if (entry.SettingType.IsEnum)
                    foreach (string value in Enum.GetNames(entry.SettingType))
                        choices |= Values.ContainsKey(def.Key + "." + value);

                string body = string.IsNullOrEmpty(desc) ? english.Description : desc;
                string shown = string.IsNullOrEmpty(plain) ? body
                             : string.IsNullOrEmpty(body) ? plain
                             : plain + "\n\n" + body;
                bool plainChange = shown != english.Description;
                if (!plainChange && string.IsNullOrEmpty(name) && string.IsNullOrEmpty(category) && !choices)
                {
                    if (!ReferenceEquals(entry.Description, english))
                    {
                        DescriptionField.SetValue(entry, english);
                        changed++;
                    }
                    continue;
                }

                // The attributes object is copied rather than edited, so English can come back. What was done to
                // the tag in force (a setting taken off the menu, a setting greyed out) is carried to the copy.
                ConfigurationManagerAttributes live = null;
                foreach (object tag in entry.Description.Tags)
                    if (tag is ConfigurationManagerAttributes now)
                        live = now;
                var tags = new List<object>();
                ConfigurationManagerAttributes copy = null;
                foreach (object tag in english.Tags)
                {
                    if (tag is ConfigurationManagerAttributes a)
                        tags.Add(copy = a.Copy());
                    else
                        tags.Add(tag);
                }
                if (copy == null)
                    tags.Add(copy = new ConfigurationManagerAttributes());
                if (live != null)
                {
                    copy.Browsable = live.Browsable;
                    copy.ReadOnly = live.ReadOnly;
                }
                if (!string.IsNullOrEmpty(name))
                    copy.DispName = name;
                if (!string.IsNullOrEmpty(category))
                    copy.Category = category;
                if (choices && copy.CustomDrawer == null)
                    copy.CustomDrawer = ChoiceDrawer.Draw;

                if (entry.Description.Description != shown)
                    changed++;
                DescriptionField.SetValue(entry, new ConfigDescription(shown, english.AcceptableValues, tags.ToArray()));
            }

            if (lacking.Count > 0)
                Plugin.Log.LogWarning($"Settings with no plain hover sentence ({lacking.Count}): " + string.Join(", ", lacking.GetRange(0, Math.Min(12, lacking.Count))));

            // Rewrites the .cfg so its comments carry the new text.
            if (changed > 0)
            {
                config.Save();
                Plugin.Log.LogInfo($"Settings text: {changed} setting(s) updated ({language}).");
            }
        }

        /// A choice shown as buttons with their translated names. Writes the English name back, so the
        /// .cfg never holds a translation. Used only where the language has labels for the setting's values.
        private static class ChoiceDrawer
        {
            public static void Draw(ConfigEntryBase entry)
            {
                string current = entry.BoxedValue?.ToString();
                GUILayout.BeginHorizontal();
                foreach (string value in Enum.GetNames(entry.SettingType))
                {
                    bool on = value == current;
                    string label = Values.TryGetValue(entry.Definition.Key + "." + value, out string translated) ? translated : value;
                    if (GUILayout.Toggle(on, label, GUI.skin.button, GUILayout.ExpandWidth(false)) && !on)
                        entry.BoxedValue = Enum.Parse(entry.SettingType, value);
                }
                GUILayout.EndHorizontal();
            }
        }

        /// One "cfg." line per setting, appended to the English template when it lacks them, so a
        /// translator has every key and its English text to work from. Never rewrites the file.
        public static void AppendTemplate(ConfigFile config, string directory)
        {
            string path = Path.Combine(directory, "carturpins_names_english_template.txt");
            if (config == null || !File.Exists(path))
                return;
            try
            {
                string have = File.ReadAllText(path, Encoding.UTF8);
                var sb = new StringBuilder();
                var pages = new HashSet<string>();
                foreach (ConfigDefinition def in config.Keys)
                {
                    ConfigEntryBase entry = config[def];
                    ConfigDescription english = Original.TryGetValue(entry, out ConfigDescription o) ? o : entry.Description;
                    if (pages.Add(def.Section) && !have.Contains("cat." + def.Section + "="))
                        sb.AppendLine("cat." + def.Section + "=" + def.Section);
                    if (!Sections.IsGeneral(def.Section))
                        continue;
                    // Two lines in older templates held the whole text; what a translator needs now is the
                    // plain sentence, the choices' names, and the description if they have not got one yet.
                    string plain = ConfigPlain.For(Sections.LogicalOf(def.Section), def.Key);
                    if (!string.IsNullOrEmpty(plain) && !have.Contains("cfg." + def.Key + ".plain="))
                        sb.AppendLine("cfg." + def.Key + ".plain=" + plain);
                    if (entry.SettingType.IsEnum)
                        foreach (string value in Enum.GetNames(entry.SettingType))
                            if (!have.Contains("val." + def.Key + "." + value + "="))
                                sb.AppendLine("val." + def.Key + "." + value + "=" + value);
                    if (have.Contains("cfg." + def.Key + ".desc=") || string.IsNullOrEmpty(english.Description))
                        continue;
                    sb.AppendLine("cfg." + def.Key + ".name=" + def.Key);
                    sb.AppendLine("cfg." + def.Key + ".desc=" + english.Description.Replace("\r", "").Replace("\n", " "));
                }
                if (sb.Length > 0)
                    File.AppendAllText(path, "\n# Settings text. cfg.<Key>.plain is the one sentence shown first when a setting is hovered; .desc is the long\n# description in the .cfg and the mod manager; .name is the label Configuration Manager (F1) shows. cat.<Page> titles a page:\n# keep its number so the pages stay in order. val.<Key>.<Value> names a choice; what is saved stays the English value.\n# The key in the .cfg file itself cannot be renamed.\n" + sb, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not add the settings text to the translation template: {e.Message}");
            }
        }
    }
}
