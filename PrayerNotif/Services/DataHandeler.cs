using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace PrayerNotif.Services
{
    public class DataHandeler<T>
    {
        private static object? SaveObject;
        private static readonly System.Text.Json.JsonSerializerOptions _options = new() { WriteIndented = true };

        [RequiresUnreferencedCode("Serialization of generic types")]
        [RequiresDynamicCode("Serialization of generic types")]
        public T? LoadData(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return default;
                }

                var json = File.ReadAllText(path);
                SaveObject = System.Text.Json.JsonSerializer.Deserialize<T>(json);
                return (T)SaveObject!;
            }
            catch
            {
                return default;
            }
        }

        [RequiresUnreferencedCode("Serialization of generic types")]
        [RequiresDynamicCode("Serialization of generic types")]
        public void SaveData(string path, T data)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(data, _options);
            File.WriteAllText(path, json);
            SaveObject = data;
        }

        [RequiresUnreferencedCode("")]
        [RequiresDynamicCode("")]
        public void SaveData(string path)
        {
            if (SaveObject is not null)
            {
                var json = System.Text.Json.JsonSerializer.Serialize(SaveObject, _options);
                File.WriteAllText(path, json);
            }
            Debug.Assert(false);
        }
        public void DeleteData(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            SaveObject = null;
        }

        [RequiresUnreferencedCode("Serialization of generic types")]
        [RequiresDynamicCode("Serialization of generic types")]
        public bool IsDataValid(string path)
        {
            return LoadData(path) is not null;
        }

        public T? GetData()
        {
            return (T?)SaveObject;
        }
    }
}
