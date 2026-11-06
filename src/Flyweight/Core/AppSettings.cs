using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Flyweight.Core
{
    [DataContract]
    public sealed class AppSettings
    {
        public const string FileName = "settings.json";

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Flyweight", FileName);

        /// <summary>Where settings lived when this app was called Video Compressor.</summary>
        public static string FormerPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VideoCompressor", FileName);

        /// <summary>
        /// Copies settings across from the former name, the first time only. The old file stays where
        /// it is, since a folder with such a common name might belong to some other program.
        /// </summary>
        /// <returns>True if there was something to bring across.</returns>
        public static bool AdoptFormerSettings(string formerPath, string path)
        {
            try
            {
                if (File.Exists(path) || !File.Exists(formerPath)) return false;

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Copy(formerPath, path);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Settings must never block compressing, and starting from defaults is fine.
                return false;
            }
        }

        [DataMember] public LevelId SelectedLevel { get; set; } = LevelId.Balanced;

        /// <summary>Null means next to the original video.</summary>
        [DataMember] public string OutputFolder { get; set; }

        [DataMember] private LevelSettings Light { get; set; }
        [DataMember] private LevelSettings Balanced { get; set; }
        [DataMember] private LevelSettings Maximum { get; set; }

        [DataMember] public string SelectedCustomId { get; set; }
        [DataMember] public List<CustomPreset> CustomPresets { get; set; } = new List<CustomPreset>();

        /// <summary>Zero until someone resizes the window. Until then it fits its contents.</summary>
        [DataMember] public double WindowWidth { get; set; }
        [DataMember] public double WindowHeight { get; set; }

        /// <summary>The settings of whichever card is selected, built-in or custom.</summary>
        public LevelSettings Selected => FindCustom(SelectedCustomId)?.Settings ?? Get(SelectedLevel);

        public void SelectLevel(LevelId level)
        {
            SelectedLevel = level;
            SelectedCustomId = null;
        }

        public void SelectCustom(string id)
        {
            SelectedCustomId = id;
        }

        public CustomPreset FindCustom(string id)
        {
            return id == null ? null : CustomPresets.FirstOrDefault(p => p.Id == id);
        }

        /// <summary>Adds a preset starting from a copy of the given settings.</summary>
        public CustomPreset AddCustom(string name, LevelSettings copyFrom)
        {
            var preset = new CustomPreset
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8),
                Name = name,
                Settings = copyFrom.Clone(),
            };
            CustomPresets.Add(preset);
            return preset;
        }

        /// <summary>If it was selected, selection goes back to the last built-in level.</summary>
        public void RemoveCustom(string id)
        {
            CustomPresets.RemoveAll(p => p.Id == id);
            if (SelectedCustomId == id) SelectedCustomId = null;
        }

        public string SuggestPresetName()
        {
            const string stem = "My preset";
            string candidate = stem;
            for (int n = 2; CustomPresets.Any(p => string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase)); n++)
            {
                candidate = $"{stem} {n}";
            }
            return candidate;
        }

        public LevelSettings Get(LevelId level)
        {
            FillMissing();
            switch (level)
            {
                case LevelId.Light: return Light;
                case LevelId.Balanced: return Balanced;
                default: return Maximum;
            }
        }

        public void Reset(LevelId level)
        {
            switch (level)
            {
                case LevelId.Light: Light = Presets.Default(level); break;
                case LevelId.Balanced: Balanced = Presets.Default(level); break;
                default: Maximum = Presets.Default(level); break;
            }
        }

        public bool IsDefault(LevelId level)
        {
            return Get(level).SameAs(Presets.Default(level));
        }

        /// <summary>Anything missing or unreadable falls back to defaults; settings must never block compressing.</summary>
        public static AppSettings Load(string path)
        {
            AppSettings settings = null;
            try
            {
                if (File.Exists(path))
                {
                    using (var stream = File.OpenRead(path))
                    {
                        settings = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream);
                    }
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is IOException || ex is UnauthorizedAccessException)
            {
                settings = null;
            }

            settings = settings ?? new AppSettings();
            settings.TidyAfterLoading();
            return settings;
        }

        public void Save(string path)
        {
            FillMissing();

            // Levels left at their defaults aren't written, so better presets in an update reach them.
            var changesOnly = new AppSettings
            {
                SelectedLevel = SelectedLevel,
                SelectedCustomId = SelectedCustomId,
                CustomPresets = CustomPresets,
                OutputFolder = OutputFolder,
                WindowWidth = WindowWidth,
                WindowHeight = WindowHeight,
                Light = IsDefault(LevelId.Light) ? null : Light,
                Balanced = IsDefault(LevelId.Balanced) ? null : Balanced,
                Maximum = IsDefault(LevelId.Maximum) ? null : Maximum,
            };

            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            using (var stream = File.Create(temp))
            {
                new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, changesOnly);
            }
            File.Copy(temp, path, overwrite: true);
            File.Delete(temp);
        }

        [OnDeserializing]
        private void SetDefaultsBeforeReading(StreamingContext context)
        {
            SelectedLevel = LevelId.Balanced;
        }

        private void TidyAfterLoading()
        {
            if (Light != null && Presets.IsRetiredDefault(LevelId.Light, Light)) Light = null;
            if (Balanced != null && Presets.IsRetiredDefault(LevelId.Balanced, Balanced)) Balanced = null;
            if (Maximum != null && Presets.IsRetiredDefault(LevelId.Maximum, Maximum)) Maximum = null;

            FillMissing();
            CustomPresets = (CustomPresets ?? new List<CustomPreset>()).Where(p => p?.Id != null && p.Settings != null).ToList();
            if (FindCustom(SelectedCustomId) == null) SelectedCustomId = null;
        }

        private void FillMissing()
        {
            if (Light == null) Light = Presets.Default(LevelId.Light);
            if (Balanced == null) Balanced = Presets.Default(LevelId.Balanced);
            if (Maximum == null) Maximum = Presets.Default(LevelId.Maximum);
        }
    }
}
