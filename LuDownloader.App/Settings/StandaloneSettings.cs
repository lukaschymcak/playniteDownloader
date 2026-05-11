using Newtonsoft.Json;
using System.IO;

namespace LuDownloader.App.Settings
{
    public class StandaloneSettings : BlankPlugin.AppSettings
    {
        [JsonIgnore]
        private string _filePath;

        public static StandaloneSettings Load(string dataDir)
        {
            var path = Path.Combine(dataDir, "settings.json");
            StandaloneSettings s;
            if (File.Exists(path))
            {
                try
                {
                    s = JsonConvert.DeserializeObject<StandaloneSettings>(File.ReadAllText(path))
                        ?? new StandaloneSettings();
                }
                catch
                {
                    s = new StandaloneSettings();
                }
            }
            else
            {
                s = new StandaloneSettings();
            }
            s._filePath = path;
            return s;
        }

        public StandaloneSettings CloneForEdit()
        {
            var clone = new StandaloneSettings();
            CopyValuesTo(clone);
            return clone;
        }

        public void CommitFrom(BlankPlugin.AppSettings source)
        {
            if (source == null)
                return;

            source.CopyValuesTo(this);
        }

        public void Save()
        {
            if (!string.IsNullOrEmpty(_filePath))
                File.WriteAllText(_filePath, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
    }
}
