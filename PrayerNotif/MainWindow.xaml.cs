using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using PrayerNotif.Extentions;
using PrayerNotif.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace PrayerNotif;
public sealed partial class MainWindow : Window
{
    const string NotifSound = @"C:\Windows\Media\Alarm02.wav";
    
    private readonly string[] Reminders = ["may allah accept your prayers -vol", "dont forget your wudu!"];
    private readonly string UserPrefrencesPath = GetUserPrefrencesPath();
    private readonly string MiscSettingsPath = GetMiscSettingsPath();
    private readonly DispatcherTimer _timer;   
    private readonly MediaPlayer _player = new();
    private readonly ObservableCollection<ScheduleItem> _schedules = [];
    private readonly ObservableCollection<string> _Coordinates = [];
    private static readonly HttpClient _httpClient = new ();
    private static readonly DataHandeler<UserPrefrences> _userPrefrencesHandler = new();
    private static readonly DataHandeler<MiscSettings> _miscSettingsHandler = new();
    private DateTime _lastFetchDate = DateTime.MinValue;
    private DateTime _lastAttemptDate = DateTime.Now;

    private static string GetUserPrefrencesPath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrayerNotif");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, "userprefrences.json");
    }
    private static string GetMiscSettingsPath()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PrayerNotif");
        Directory.CreateDirectory(appDataPath);
        return Path.Combine(appDataPath, "miscsettings.json");
    }
    public MainWindow()
    {
        InitializeComponent();

        StartupHelper.SetRunAtStartup(true);

        AppNotificationManager.Default.NotificationInvoked += (sender, args) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AppWindow.Show();
            });
        };
        AppNotificationManager.Default.Register();

        SchedulesListView.ItemsSource = _schedules;
        CordsListView.ItemsSource = _Coordinates;

        _userPrefrencesHandler.LoadData(UserPrefrencesPath);
        _miscSettingsHandler.LoadData(MiscSettingsPath);

        _timer = new()
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _timer.Tick += Timer_Tick;

        var prefrences = _userPrefrencesHandler.GetData();

        if (prefrences is not null)
        {
            var currentIndex = prefrences.CurrentCordinateIndex;
            if (prefrences.CordItems is not null && prefrences.CordItems.Count > 0 && currentIndex >= 0)
            {
                Debug.Assert(currentIndex < prefrences.CordItems.Count,
                    "CurrentCordinateIndex is out of bounds of CordItems list.");

                LatitudeBox.Value = prefrences.CordItems[currentIndex].Cords.Lat;
                LongitudeBox.Value = prefrences.CordItems[currentIndex].Cords.Lon;
                foreach (var cord in prefrences.CordItems)
                {
                    _Coordinates.Add($"{cord.Label}: Lat: {cord.Cords.Lat}, Lon: {cord.Cords.Lon}");
                }
            }

            CordsListView.SelectedIndex = currentIndex;

            MuteCheckBox.IsChecked = prefrences.IsMuted;
            VolumeSlider.Value = prefrences.SoundVolume * 100;
        }
        _timer.Start();

        _ = RefreshPrayerTimes();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshPrayerTimes();
    }
    //TODO: allow users to use the approximatedLocation even when there's coordinates already listed.
    private async Task RefreshPrayerTimes()
    {
        bool isCoordinatesEntered = !(double.IsNaN(LatitudeBox.Value) || double.IsNaN(LongitudeBox.Value));

        if (!isCoordinatesEntered && !(bool)ManualOverrideCheckBox.IsChecked!)
        {
            var cords = await GetBestLocationAsync();
            if (cords is not null)
            {
                await FetchPrayerTimesAsync((Cords)cords);
            }
            return;
        }
        var lat = LatitudeBox.Value;
        var lon = LongitudeBox.Value;

        var prefrences = _userPrefrencesHandler.GetData();
        
        var exists = prefrences?.CordItems?.Exists(x => x.Cords.Lat == lat && x.Cords.Lon == lon);
        if(prefrences is null)
        {
            prefrences = new UserPrefrences
            {
                CordItems = exists is null || exists == false ? [new CordItem(new Cords(lat, lon), "Current Location")] : null,
                CurrentCordinateIndex = 0,
                SoundVolume = VolumeSlider.Value / 100,
                IsMuted = (bool)MuteCheckBox.IsChecked!
            };
            //TODO: this is repeated twice, one can pretty it up a bit
            _Coordinates.Add($"Current Location: Lat: {lat}, Lon: {lon}");
            _userPrefrencesHandler.SaveData(UserPrefrencesPath, prefrences);
        }
        else if((exists is null || exists == false) && prefrences.CordItems is null)
        {
            //TODO: the intention is to add the current location to the list of coordinates if the list is empty but i havent tested
            //so do that, future me. you got it buddy, i know you'll have a good time doing it. i believe in you. you can do it. i know you can. i have faith in you. i know you'll do great. i know you'll succeed. i know you'll make it happen. i know you'll get it done. i know you'll figure it out. i know you'll find a way. i know you'll make it work. i know you'll get through it. i know you'll overcome it. i know you'll prevail. i know you'll triumph. i know you'll win. i know you'll succeed.
            prefrences.CordItems = [];
            prefrences.CordItems.Add(new CordItem(new Cords(lat, lon), "Current Location"));
            prefrences.CurrentCordinateIndex = prefrences.CordItems.Count - 1;
            _Coordinates.Add($"Current Location: Lat: {lat}, Lon: {lon}");
            _userPrefrencesHandler.SaveData(UserPrefrencesPath, prefrences);
        }

        var index = prefrences.CurrentCordinateIndex;
        LocationText.Text = $"{prefrences.CordItems![index].Label}";
        await FetchPrayerTimesAsync(new(lat, lon));
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var prefrences = _userPrefrencesHandler.GetData();
        prefrences ??= new UserPrefrences
        {
            SpawnWindowOnStartup = (bool)ShowOnStartupCheckBox.IsChecked!,
            SoundVolume = VolumeSlider.Value/100,
            IsMuted = (bool)MuteCheckBox.IsChecked!
        };

        prefrences.SpawnWindowOnStartup = (bool)ShowOnStartupCheckBox.IsChecked!;
        prefrences.SoundVolume = VolumeSlider.Value/100;
        prefrences.IsMuted = (bool)MuteCheckBox.IsChecked!;

        _userPrefrencesHandler.SaveData(UserPrefrencesPath, prefrences);
    }
    private async void AddCoordinates_Click(object sender, RoutedEventArgs e)
    {
        if (double.IsNaN(LatitudeSaveBox.Value) || double.IsNaN(LongitudeSaveBox.Value))
        {
            StatusText.Text = "Please enter valid coordinates.";
            return;
        }
        if(string.IsNullOrWhiteSpace(LabelTextBox.Text))
        {
            StatusText.Text = "Please enter a label for the coordinates.";
            return;
        }
        var lat = LatitudeSaveBox.Value;
        var lon = LongitudeSaveBox.Value;
        var prefrences = _userPrefrencesHandler.GetData();
        if (prefrences is null)
        {
            prefrences = new UserPrefrences
            {
                CordItems = [new CordItem(new Cords(lat, lon), LabelTextBox.Text)],
                CurrentCordinateIndex = 0,
                SpawnWindowOnStartup = true,
                SoundVolume = VolumeSlider.Value / 100,
                IsMuted = (bool)MuteCheckBox.IsChecked!
            };
            _userPrefrencesHandler.SaveData(UserPrefrencesPath, prefrences);
        }
        else
        {
            prefrences.CordItems ??= [];
            prefrences.CordItems.Add(new CordItem(new Cords(lat, lon), LabelTextBox.Text));
            _userPrefrencesHandler.SaveData(UserPrefrencesPath, prefrences);
        }
        _Coordinates.Add($"{LabelTextBox.Text}: Lat: {lat}, Lon: {lon}");
    }
    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        _userPrefrencesHandler.DeleteData(UserPrefrencesPath);
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
    public async Task<Cords?> GetBestLocationAsync()
    {
        RefreshButton.IsEnabled = false;

        try
        {
            var cords = await TryGetWindowsLocationAsync();
            if (cords != null)
            {
                return cords;
            }

            return await ApproximateLocationAsync();
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private async Task<Cords?> TryGetWindowsLocationAsync()
    {
        StatusText.Text = "Detecting precise location...";

        try
        {
            // MUST be called on the UI thread while the app window is in the foreground
            var accessStatus = await Geolocator.RequestAccessAsync();
            if (accessStatus != GeolocationAccessStatus.Allowed)
            {
                var settings = _miscSettingsHandler.GetData();
                if (settings is null || settings.WasLocationPermissionAsked == false)
                {
                    SendNotification("Location Permission Needed", "Please allow location access for precise prayer times.\nyou'll be only nagged about this once", false);
                    
                    settings = new() { WasLocationPermissionAsked = true };
                    _miscSettingsHandler.SaveData(MiscSettingsPath, settings);
                    
                }
                return null;
            }

            var geolocator = new Geolocator
            {
                DesiredAccuracy = PositionAccuracy.High
            };

            // Allow a cached position up to 5 mins old, and time out after 10 seconds
            Geoposition pos = await geolocator.GetGeopositionAsync(
                maximumAge: TimeSpan.FromMinutes(5),
                timeout: TimeSpan.FromSeconds(10));

            // Note: Use Coordinate.Point.Position (Coordinate.Latitude/Longitude are deprecated)
            var lat = pos.Coordinate.Point.Position.Latitude;
            var lon = pos.Coordinate.Point.Position.Longitude;
            var accuracyMeters = pos.Coordinate.Accuracy;
            var source = pos.Coordinate.PositionSource;

            // If Windows itself could only use IP or coarse obfuscated location,
            // you can optionally still accept it or fall back.
            LocationText.Text = $"{lat:F4}, {lon:F4} (±{accuracyMeters:F0}m via {source})";
            StatusText.Text = "Location detected.";

            return new Cords(lat, lon);
        }
        catch (Exception)
        {
            // Common if Windows Location Services are turned off globally in Settings,
            // or if the request timed out. Fall back cleanly to IP.
            return null;
        }
    }
    private async Task FetchPrayerTimesAsync(Cords data)
    {
        try
        {
            StatusText.Text = "Fetching prayer times...";

            var today = DateTime.Now.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);
            var apiUrl = string.Create(CultureInfo.InvariantCulture, 
                $"https://api.aladhan.com/v1/timings/{today}?latitude={data.Lat}&longitude={data.Lon}");

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
                    TimeSpan nextFajr = fajrCache.Value < maghrib ? fajrCache.Value + TimeSpan.FromDays(1) : fajrCache.Value;

                    TimeSpan nightDuration = nextFajr - maghrib;

                    TimeSpan firstThird = Wrap24h(maghrib + (nightDuration / 3));
                    TimeSpan midnight = Wrap24h(maghrib + (nightDuration / 2));

                    // Option 1: Delayed Isha window (1/3 of night to Islamic midnight)
                    note = $"sunna time from: {firstThird:hh\\:mm} to {midnight:hh\\:mm}";

                    // Option 2: Preferred window from Isha adhan until 1/3 of the night (or midnight)
                     //note = $"preferred: {parsedTime:hh\\:mm} to {firstThird:hh\\:mm} (midnight: {midnight:hh\\:mm})";
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
    private async void RemoveCoordinate_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)!.DataContext;
        if (item is string selectedCord)
        {
            var prefrences = _userPrefrencesHandler.GetData();
            if (prefrences?.CordItems is not null)
            {
                var cordToRemove = prefrences.CordItems.FirstOrDefault(c => $"{c.Label}: Lat: {c.Cords.Lat}, Lon: {c.Cords.Lon}" == selectedCord);
                if (cordToRemove is not null)
                {
                    prefrences.CordItems.Remove(cordToRemove);
                    _userPrefrencesHandler.SaveData(UserPrefrencesPath, prefrences);
                    _Coordinates.Remove(selectedCord);
                }
            }
        }
    }
    private async void OnCordsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CordsListView.SelectedItem is string selectedCord)
        {
            var prefrences = _userPrefrencesHandler.GetData();
            if (prefrences?.CordItems is not null)
            {
                var cordToSelect = prefrences.CordItems.FirstOrDefault(c => $"{c.Label}: Lat: {c.Cords.Lat}, Lon: {c.Cords.Lon}" == selectedCord);
                if (cordToSelect is not null)
                {
                    LatitudeBox.Value = cordToSelect.Cords.Lat;
                    LongitudeBox.Value = cordToSelect.Cords.Lon;
                    prefrences.CurrentCordinateIndex = prefrences.CordItems.IndexOf(cordToSelect);
                    _userPrefrencesHandler.SaveData(UserPrefrencesPath, prefrences);
                    await RefreshPrayerTimes();
                }
            }
        }
    }
    private async void ModifyCoordinate_Click(object sender, RoutedEventArgs e)
    {
        var item = (sender as FrameworkElement)!.DataContext;
        if (item is string selectedCord)
        {
            var prefrences = _userPrefrencesHandler.GetData();
            if (prefrences?.CordItems is not null)
            {
                var cordToModify = prefrences.CordItems.FirstOrDefault(c => $"{c.Label}: Lat: {c.Cords.Lat}, Lon: {c.Cords.Lon}" == selectedCord);
                var labelBox = new TextBox { Header = "Label", Text = cordToModify?.Label ?? "something idk" };
                var latBox = new NumberBox { Header = "Latitude", Value = cordToModify?.Cords.Lat ?? 0 };
                var lonBox = new NumberBox { Header = "Longitude", Value = cordToModify?.Cords.Lon ?? 0 };
                if (cordToModify is not null)
                {
                    var diag = new ContentDialog()
                    {
                        XamlRoot = this.Content.XamlRoot,
                        Title = "Modify Coordinates",
                        Content = new StackPanel
                        {
                            Children =
                            {
                                labelBox,
                                latBox,
                                lonBox
                            },
                            Spacing = 10
                        },
                        PrimaryButtonText = "Save",
                        CloseButtonText = "Cancel"
                    };
                    var result = await diag.ShowAsync();
                    if(result != ContentDialogResult.Primary)
                    {
                        return;
                    }
                    if(string.IsNullOrWhiteSpace(labelBox.Text) || double.IsNaN(latBox.Value) || double.IsNaN(lonBox.Value))
                    {
                        StatusText.Text = "Please enter valid coordinates and label.";
                        return;
                    }
                    
                    var newCord = new CordItem(new(latBox.Value, lonBox.Value), labelBox.Text);
                    var index = prefrences.CordItems.IndexOf(cordToModify);
                    prefrences.CordItems[index] = newCord;
                    _userPrefrencesHandler.SaveData(UserPrefrencesPath, prefrences);
                    
                    _Coordinates[index] = $"{newCord.Label}: Lat: {newCord.Cords.Lat}, Lon: {newCord.Cords.Lon}";
                    
                }
            }
        }
    }
    private async void HideWindow_Click(object? sender, RoutedEventArgs e)
    {
        AppWindow.Hide();
        SendNotification("Background Mode", "App's running in the background and will notify you of prayer times...in theory.", false);
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

    private void SendNotification(string title, string message, bool useSound = true)
    {
        var notification = new AppNotificationBuilder()
            .AddText(title)
            .AddText(message)
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);

        if ((bool)MuteCheckBox.IsChecked! || useSound == false)
        {
            return;
        }

        _player.Source = MediaSource.CreateFromUri(
            new Uri(NotifSound));
        _player.Volume = 1;
        _player.Play();
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        _player.Volume = VolumeSlider.Value / 100;
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
    public List<CordItem>? CordItems{ get; set; }
    public int CurrentCordinateIndex { get; set; } = 0;
    public bool SpawnWindowOnStartup { get; set; }
    public double SoundVolume { get; set; } = 1;
    public bool IsMuted { get; set; } = false;
}
public class MiscSettings
{
    public bool WasLocationPermissionAsked { get; set; } = false;
}
public record class CordItem(Cords Cords, string Label);
public record struct Cords(double Lat, double Lon);
static class StartupHelper
{
    public static void SetRunAtStartup(bool enable)
    {
        const string appName = "PrayerNotif";
        const string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
        if (key == null)
            return;

        if (enable)
        {
            // Environment.ProcessPath works reliably even for single-file EXEs
            string? exePath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exePath))
            {
                key.SetValue(appName, $"\"{exePath}\"");
            }
        }
        else
        {
            key.DeleteValue(appName, throwOnMissingValue: false);
        }
    }
}