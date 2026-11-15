using System;

namespace PrayerNotif.Extentions
{
    static class StringExtensions
    {
        public static string GetRandomItem(this string[] strArr)
        {
            if(strArr == null || strArr.Length == 0)
            {
                throw new ArgumentException("Array cannot be null or empty.", nameof(strArr));
            }

            var random = new Random();
            int index = random.Next(strArr.Length);
            return strArr[index];
        }
    }
}
