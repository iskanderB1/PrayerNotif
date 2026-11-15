using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using PrayerNotif.Extentions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using PrayerNotif.Services;

namespace PrayerNotif;
public sealed partial class MainWindow : Window
{
    const string NotifSound = @"C:\Windows\Media\Alarm02.wav";
    private readonly string[] Reminders = ["may allah accept your prayers -vol", "dont forget your wudu!"];
    private readonly string UserPrefrencesPath = GetUserPrefrencesPath();       
    private readonly DispatcherTimer _timer;
    private readonly MediaPlayer _player = new();
    private readonly ObservableCollection<ScheduleItem> _schedules = [];
    private static readonly HttpClient _httpClient = new ();
    private static UserPrefrences? _userPrefrences = null;
    private DateTime _lastFetchDate = DateTime.MinValue;
    private DateTime _lastAttemptDate = DateTime.MinValue;

    private static string GetUserPrefrencesPath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrayerNotif");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, "userprefrences.json");
    }

    public MainWindow()
    {
        InitializeComponent();

        AppNotificationManager.Default.NotificationInvoked += (sender, args) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppWindow.Show();
            });
        };
        AppNotificationManager.Default.Register();

        SchedulesListView.ItemsSource = _schedules;
        _userPrefrences = DataHandeler.LoadData<UserPrefrences>(UserPrefrencesPath);
        if(_userPrefrences is not null)
        {
            LatitudeBox.Value = _userPrefrences.UserCordinates.Lat;
            LongitudeBox.Value = _userPrefrences.UserCordinates.Lon;
            ManualOverrideCheckBox.IsChecked = true;
        }

        _timer = new()
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        _ = RefreshPrayerTimes();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPrayerTimes();
    }
#if debug
    private async void Debug_Click(object sender, RoutedEventArgs e)
    {
        DataHandeler.DeleteData(UserPrefrencesPath);
        _userPrefrences = null;
    }
#endif
    private async Task RefreshPrayerTimes()
    {
        bool isCoordinatesEntered = !(double.IsNaN(LatitudeBox.Value) || double.IsNaN(LongitudeBox.Value));

        if (!isCoordinatesEntered || !(bool)ManualOverrideCheckBox.IsChecked!)
        {
            var cords = await ApproximateLocationAsync();
            if (cords is not null)
            {
                await FetchPrayerTimesAsync((Cords)cords);
            }
            return;
        }
        var lat = LatitudeBox.Value;
        var lon = LongitudeBox.Value;

        DataHandeler.SaveData(UserPrefrencesPath, new UserPrefrences
        {
            UserCordinates = new Cords(lat, lon),
            SpawnWindowOnStartup = _userPrefrences?.SpawnWindowOnStartup ?? true,
            SoundVolume = _userPrefrences?.SoundVolume ?? 1
        });

        LocationText.Text = "Somewhere...? idk, didnt bother to code the logic :P";
        await FetchPrayerTimesAsync(new(lat, lon));
    }
    private async Task<Cords?> ApproximateLocationAsync()
    {
        StatusText.Text = "Detecting IP location...";
        RefreshButton.IsEnabled = false;

        try
        {
            var ipResponse = await _httpClient.GetStringAsync("http://ip-api.com/json/");
            using var ipDoc = JsonDocument.Parse(ipResponse);

            var root = ipDoc.RootElement;
            if (root.GetProperty("status").GetString() != "success")
            {
                StatusText.Text = "Failed to detect location.";
                return null;
            }

            var lat = root.GetProperty("lat").GetDouble();
            var lon = root.GetProperty("lon").GetDouble();
            var city = root.GetProperty("city").GetString();
            var country = root.GetProperty("country").GetString();

            LocationText.Text = $"{city}, {country}";
            return new Cords(lat, lon);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Detection failed with error: {ex.Message}";
            return null;
        }
    }
    private async Task FetchPrayerTimesAsync(Cords data)
    {
        try
        {
            StatusText.Text = "Fetching prayer times...";

            var today = DateTime.Now.ToString("dd-MM-yyyy");
            var apiUrl = $"https://api.aladhan.com/v1/timings/{today}?latitude={data.Lat}&longitude={data.Lon}";

            var prayerResponse = await _httpClient.GetStringAsync(apiUrl);
            using var prayerDoc = JsonDocument.Parse(prayerResponse);

            var timings = prayerDoc.RootElement
                                   .GetProperty("data")
                                   .GetProperty("timings");

            _schedules.Clear();

            string[] prayerNames = ["Fajr", "Dhuhr", "Asr", "Maghrib", "Isha"];
            TimeSpan? fajrCache = null;
            TimeSpan? maghribCache = null;
            int count = 0;
            foreach (var prayer in prayerNames)
            {
                var timeStr = timings.GetProperty(prayer).GetString();
                if (!TimeSpan.TryParse(timeStr, out TimeSpan parsedTime))
                    continue; // prayer not returned / unparseable


                if (prayer == "Fajr")
                {
                    fajrCache = parsedTime;
                }
                else if (prayer == "Maghrib")
                {
                    maghribCache = parsedTime;
                }

                string note = string.Empty;
                if (prayer == "Fajr")
                {
                    note = "close your pc bratan";
                }
                else if (prayer == "Isha" && fajrCache is not null && maghribCache is not null)
                {
                    TimeSpan maghrib = maghribCache.Value;
                    TimeSpan nextFajr = fajrCache.Value < maghrib
                        ? fajrCache.Value + TimeSpan.FromDays(1)
                        : fajrCache.Value;

                    TimeSpan nightDuration = nextFajr - maghrib;

                    TimeSpan firstThird = Wrap24h(maghrib + (nightDuration / 3));
                    TimeSpan midnight = Wrap24h(maghrib + (nightDuration / 2));

                    // Option 1: Delayed Isha window (1/3 of night to Islamic midnight)
                    //note = $"sunna time from: {firstThird:hh\\:mm} to {midnight:hh\\:mm}";

                    // Option 2: Preferred window from Isha adhan until 1/3 of the night (or midnight)
                     note = $"preferred: {parsedTime:hh\\:mm} to {firstThird:hh\\:mm} (midnight: {midnight:hh\\:mm})";
                }

                bool isPrayerFinished = DateTime.Now.TimeOfDay > parsedTime;
                count = isPrayerFinished ? count + 1 : count;
                _schedules.Add(new ScheduleItem
                {
                    Name = prayer,
                    Time = parsedTime,
                    Note = note,
                    Message = $"It is time for {prayer} prayer.",
                    HasTriggeredToday = isPrayerFinished
                });
            }
#if DEBUG
            const string time = "22:38";
            var timey = TimeSpan.Parse(time);
            var debug = TimeSpan.FromMinutes((int)DateTime.Now.TimeOfDay.TotalMinutes);
            _schedules.Add(new ScheduleItem
            {
                Name = "Test",
                Time = timey,
                Message = "you aint supposed to see dis",
                HasTriggeredToday = debug > timey
            });
#endif
            _lastFetchDate = DateTime.Now.Date;
            StatusText.Text = count == 5 ? "you're all done for today!!" : $"Last updated: {DateTime.Now:HH:mm}";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Network Error: {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void HideWindow_Click(object? sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
        SendNotification("Background Mode", "App's running in the background and will notify you of prayer times...in theory.");
    }

    private async void Timer_Tick(object? sender, object? e)
    {
        if (DateTime.Now.Date > _lastFetchDate.Date)
        {
            if ((DateTime.Now - _lastAttemptDate).TotalMinutes > 5)
            {
                _lastAttemptDate = DateTime.Now;
                await RefreshPrayerTimes();
            }
            return;
        }

        var now = DateTime.Now.TimeOfDay;

        foreach (var schedule in _schedules.ToList())
        {
            if (now.Hours == schedule.Time.Hours && now.Minutes == schedule.Time.Minutes)
            {
                if (!schedule.HasTriggeredToday)
                {
                    SendNotification($"Time for {schedule.Name}", schedule.Message + $"\n{Reminders.GetRandomItem()}");
                    schedule.HasTriggeredToday = true;
                    await RefreshPrayerTimes();
                }
            }
        }
    }

    private void SendNotification(string title, string message)
    {
        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .BuildNotification();


        AppNotificationManager.Default.Show(notification);

        _player.Source = MediaSource.CreateFromUri(
            new Uri(NotifSound));
        _player.Volume = 1;
        _player.Play();
    }
    private static TimeSpan Wrap24h(TimeSpan time) =>
    time >= TimeSpan.FromDays(1) ? time - TimeSpan.FromDays(1) : time;
}

public class ScheduleItem
{
    public string? Name { get; set; }
    public string? Note { get; set; }
    public TimeSpan Time { get; set; }
    public string? Message { get; set; }
    public bool HasTriggeredToday { get; set; }

    public string DisplayTime => Time.ToString(@"hh\:mm");
    public double DoneOpacity => HasTriggeredToday ? 0.4 : 1.0;

}

public class UserPrefrences
{
    public Cords UserCordinates{ get; set; }
    public bool SpawnWindowOnStartup { get; set; }
    public double SoundVolume { get; set; } = 1;
}
public record struct Cords(double Lat, double Lon);