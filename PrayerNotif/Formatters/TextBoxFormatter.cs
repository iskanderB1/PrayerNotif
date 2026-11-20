using System;
using System.Globalization;
using Windows.Globalization.NumberFormatting;

namespace PrayerNotif.Formatters;

public sealed class TextBoxFormatter : INumberFormatter2, INumberParser
{
    public double? ParseDouble(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalized = text.Trim().Replace(',', '.');

        return double.TryParse(
            normalized,
            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out double result)
            ? result
            : null;
    }

    public long? ParseInt(string text)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null;

    public ulong? ParseUInt(string text)
        => ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : null;

    public string FormatDouble(double value)
        => double.IsNaN(value) ? string.Empty : Math.Round(value, 8).ToString("0.########", CultureInfo.InvariantCulture);

    public string FormatInt(long value)
        => value.ToString(CultureInfo.InvariantCulture);

    public string FormatUInt(ulong value)
        => value.ToString(CultureInfo.InvariantCulture);
}