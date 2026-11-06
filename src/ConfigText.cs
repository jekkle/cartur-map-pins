using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace CarturMapPins
{
    /// Translatable settings text (totya, 2.2.0: he could not tell what the cluster settings did
    /// because the descriptions are English only).
    ///
    /// The lines live in the pin-name translation file, which he already has, as
    /// "cfg.<Key>.name=" and "cfg.<Key>.desc=". The description is what r2modman's config editor and
    /// the .cfg comments show; the name is shown by Configuration Manager (F1) only, since the key
    /// in the .cfg file is what the setting is stored under and cannot change. Only the settings that
    /// came from the old General page are covered (the per-kind switches and icons are named after
    /// the pins, which are translated already).
    ///
    /// ConfigEntryBase.Description is read-only, so the entry's description object is replaced
    /// through its backing field. The originals are kept so a switch back to English restores them.
    internal static class ConfigText
    {
        private static readonly System.Reflection.FieldInfo DescriptionField =
            AccessTools.Field(typeof(ConfigEntryBase), "<Description>k__BackingField");

        private static readonly Dictionary<ConfigEntryBase, ConfigDescription> Original =
            new Dictionary<ConfigEntryBase, ConfigDescription>();

        public static void Apply(ConfigFile config, string language)
        {
            if (config == null || DescriptionField == null)
                return;

            Dictionary<string, string> text = Translations.LoadOverrides(language) ?? new Dictionary<string, string>();
            int changed = 0;
            foreach (ConfigDefinition def in new List<ConfigDefinition>(config.Keys))
            {
                if (!Sections.IsGeneral(def.Section))
                    continue;
                ConfigEntryBase entry = config[def];
                if (!Original.TryGetValue(entry, out ConfigDescription english))
                    Original[entry] = english = entry.Description;

                text.TryGetValue("cfg." + def.Key + ".desc", out string desc);
                text.TryGetValue("cfg." + def.Key + ".name", out string name);
                if (string.IsNullOrEmpty(desc) && string.IsNullOrEmpty(name))
                {
                    if (!ReferenceEquals(entry.Description, english))
                    {
                        DescriptionField.SetValue(entry, english);
                        changed++;
                    }
                    continue;
                }

                // The attributes object is copied rather than edited, so English can come back.
                var tags = new List<object>();
                ConfigurationManagerAttributes shown = null;
                foreach (object tag in english.Tags)
                {
                    if (tag is ConfigurationManagerAttributes a)
                        tags.Add(shown = a.Copy());
                    else
                        tags.Add(tag);
                }
                if (shown == null)
                    tags.Add(shown = new ConfigurationManagerAttributes());
                if (!string.IsNullOrEmpty(name))
                    shown.DispName = name;

                DescriptionField.SetValue(entry, new ConfigDescription(
                    string.IsNullOrEmpty(desc) ? english.Description : desc, english.AcceptableValues, tags.ToArray()));
                changed++;
            }

            // Rewrites the .cfg so its comments carry the new text.
            if (changed > 0)
            {
                config.Save();
                Plugin.Log.LogInfo($"Settings text: {changed} setting(s) shown in {language}.");
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
                foreach (ConfigDefinition def in config.Keys)
                {
                    if (!Sections.IsGeneral(def.Section) || have.Contains("cfg." + def.Key + ".desc="))
                        continue;
                    ConfigDescription english = Original.TryGetValue(config[def], out ConfigDescription o) ? o : config[def].Description;
                    if (string.IsNullOrEmpty(english.Description))
                        continue;
                    sb.AppendLine("cfg." + def.Key + ".name=" + def.Key);
                    sb.AppendLine("cfg." + def.Key + ".desc=" + english.Description.Replace("\r", "").Replace("\n", " "));
                }
                if (sb.Length > 0)
                    File.AppendAllText(path, "\n# Settings text. .desc is the description in the .cfg and the mod manager; .name is the label\n# Configuration Manager (F1) shows. The key in the .cfg file itself cannot be renamed.\n" + sb, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not add the settings text to the translation template: {e.Message}");
            }
        }
    }
}
