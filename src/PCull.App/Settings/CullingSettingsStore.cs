using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace PCull.App.Settings
{
    public sealed class CullingSettingsStore
    {
        private readonly string _settingsPath;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public CullingSettingsStore(string settingsPath)
        {
            _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
        }

        public CullingSettings Load()
        {
            if (!File.Exists(_settingsPath))
                return new CullingSettings();

            var json = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<CullingSettings>(json)
                ?? throw new InvalidDataException($"Settings file is empty or invalid: {_settingsPath}");
            settings.Validate();
            return settings;
        }

        public async Task SaveAsync(CullingSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            settings.Validate();

            var directory = Path.GetDirectoryName(_settingsPath)
                ?? throw new InvalidOperationException("Settings file must have a parent directory.");
            Directory.CreateDirectory(directory);
            var tempPath = $"{_settingsPath}.{Guid.NewGuid():N}.tmp";

            try
            {
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                await File.WriteAllTextAsync(tempPath, json);
                File.Move(tempPath, _settingsPath, true);
            }
            catch
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
                throw;
            }
        }
    }
}
