using System.IO;

namespace PrayerNotif.Services
{
    public static class DataHandeler
    {
        private static readonly System.Text.Json.JsonSerializerOptions _options = new() { WriteIndented = true };
        public static T? LoadData<T>(string path)
        {
            if (!File.Exists(path))
            {
                return default;
            }

            var json = File.ReadAllText(path);
            return System.Text.Json.JsonSerializer.Deserialize<T>(json);
        }

        public static void SaveData<T>(string path, T data)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data, _options);
            File.WriteAllText(path, json);
        }
#if DEBUG
        public static void DeleteData(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
#endif
        public static bool IsDataValid<T>(string path)
        {
            return LoadData<T>(path) is not null;
        }
    }
}
