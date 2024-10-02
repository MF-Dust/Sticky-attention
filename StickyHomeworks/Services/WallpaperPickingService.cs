﻿using ElysiaFramework;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;
using StickyHomeworks;
using StickyHomeworks.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ClassIsland.Services;

public sealed class WallpaperPickingService : IHostedService, INotifyPropertyChanged
{
    private SettingsService SettingsService { get; }

    private static readonly string DesktopWindowClassName = "Progman";
    private ObservableCollection<Color> _wallpaperColorPlatte = new();
    private BitmapImage _wallpaperImage = new();
    private bool _isWorking = false;

    public RegistryNotifier RegistryNotifier
    {
        get;
    }

    private DispatcherTimer UpdateTimer
    {
        get;
    } = new DispatcherTimer()
    {
        Interval = TimeSpan.FromMinutes(1)
    };

    public static void ColorToHsv(System.Windows.Media.Color color, out double hue, out double saturation, out double value)
    {
        int max = Math.Max(color.R, Math.Max(color.G, color.B));
        int min = Math.Min(color.R, Math.Min(color.G, color.B));

        hue = 0;
        saturation = (max == 0) ? 0 : 1d - (1d * min / max);
        value = max / 255d;
    }

    public ObservableCollection<Color> WallpaperColorPlatte
    {
        get => _wallpaperColorPlatte;
        set
        {
            if (Equals(value, _wallpaperColorPlatte)) return;
            _wallpaperColorPlatte = value;
            OnPropertyChanged();
        }
    }

    public BitmapImage WallpaperImage
    {
        get => _wallpaperImage;
        set
        {
            if (Equals(value, _wallpaperImage)) return;
            _wallpaperImage = value;
            OnPropertyChanged();
        }
    }

    public WallpaperPickingService(SettingsService settingsService)
    {
        SettingsService = settingsService;
        SystemEvents.UserPreferenceChanged += SystemEventsOnUserPreferenceChanged;
        RegistryNotifier = new RegistryNotifier(RegistryNotifier.HKEY_CURRENT_USER, "Control Panel\\Desktop");
        RegistryNotifier.RegistryKeyUpdated += RegistryNotifierOnRegistryKeyUpdated;
        RegistryNotifier.Start();
        UpdateTimer.Tick += UpdateTimerOnTick;
        UpdateTimer.Interval = TimeSpan.FromSeconds(SettingsService.Settings.WallpaperAutoUpdateIntervalSeconds);
        SettingsService.Settings.PropertyChanged += SettingsServiceOnPropertyChanged;
        UpdateTimer.Start();
    }

    public event EventHandler? WallpaperColorPlatteChanged;

    private void SettingsServiceOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsService.Settings.WallpaperAutoUpdateIntervalSeconds))
        {
            UpdateTimer.Interval = TimeSpan.FromSeconds(SettingsService.Settings.WallpaperAutoUpdateIntervalSeconds);
        }
    }

    private async void UpdateTimerOnTick(object? sender, EventArgs e)
    {
        if (!SettingsService.Settings.IsWallpaperAutoUpdateEnabled)
        {
            return;
        }

        await GetWallpaperAsync();
    }

    private async void RegistryNotifierOnRegistryKeyUpdated()
    {
        Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            await GetWallpaperAsync();
        });
    }

    private IntPtr HwndSourceHookProcess(IntPtr hwnd, int msg, IntPtr wparam, IntPtr lparam, ref bool handled)
    {
        if (msg == 0x0317)
        {
            Debug.WriteLine("printed");
        }
        return default;
    }

    private async void SystemEventsOnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.Desktop)
        {
            //await Task.Run(=>Thread.Sleep(TimeSpan.FromSeconds(1));
            await GetWallpaperAsync();
        }
    }


    public static Bitmap? GetScreenShot(string className)
    {
        var win = NativeWindowHelper.FindWindowByClass(className);
        if (win == IntPtr.Zero)
        {
            return null;
        }

        return WindowCaptureHelper.CaptureWindowBitBlt(win);
    }

    public static Bitmap? GetFallbackWallpaper()
    {
        try
        {
            var k = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop");
            var path = (string?)k?.GetValue("WallPaper");
            var b = Screen.PrimaryScreen.Bounds;
            return path == null ? null : new Bitmap(Image.FromFile(path), b.Width, b.Height);
        }
        catch
        {
            return null;
        }
    }

    public bool IsWorking
    {
        get => _isWorking;
        set
        {
            if (value == _isWorking) return;
            _isWorking = value;
            OnPropertyChanged();
        }
    }

    public async Task GetWallpaperAsync()
    {
        if (IsWorking)
        {
            return;
        }

        IsWorking = true;
        await Task.Run(() =>
        {
            var bitmap = SettingsService.Settings.IsFallbackModeEnabled ?
                (GetFallbackWallpaper())
                :
                (GetScreenShot(
                    SettingsService.Settings.WallpaperClassName == ""
                    ? DesktopWindowClassName
                    : SettingsService.Settings.WallpaperClassName
                ));
            if (bitmap is null)
            {
                return;
            }

            double dpiX = 1, dpiY = 1;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var mw = (MainWindow)Application.Current.MainWindow!;
                mw.GetCurrentDpi(out dpiX, out dpiY);
            });
            WallpaperImage = BitmapConveters.ConvertToBitmapImage(bitmap, (int)(750 * dpiX));
            var w = new Stopwatch();
            w.Start();
            var right = SettingsService.Settings.TargetLightValue - 0.5;
            var left = SettingsService.Settings.TargetLightValue + 0.5;
            var r = ColorOctTreeNode.ProcessImage(bitmap)
                .OrderByDescending(i =>
                {
                    var c = (Color)ColorConverter.ConvertFromString(i.Key);
                    WallpaperPickingService.ColorToHsv(c, out var h, out var s, out var v);
                    return (s + v * (-(v - right) * (v - left) * 4)) * Math.Log2(i.Value);
                })
                .ThenByDescending(i => i.Value)
                .ToList();
            WallpaperColorPlatte.Clear();
            for (var i = 0; i < Math.Min(r.Count, 5); i++)
            {
                WallpaperColorPlatte.Add((Color)ColorConverter.ConvertFromString(r[i].Key));
            }
        });

        // Update cached platte
        if (SettingsService.Settings.WallpaperColorPlatte.Count < SettingsService.Settings.SelectedPlatteIndex + 1 ||
            SettingsService.Settings.SelectedPlatteIndex == -1 ||
            WallpaperColorPlatte.Count < SettingsService.Settings.SelectedPlatteIndex + 1 ||
            SettingsService.Settings.WallpaperColorPlatte[SettingsService.Settings.SelectedPlatteIndex] !=
            WallpaperColorPlatte[SettingsService.Settings.SelectedPlatteIndex])
        {
            SettingsService.Settings.WallpaperColorPlatte.Clear();
            foreach (var i in WallpaperColorPlatte)
            {
                SettingsService.Settings.WallpaperColorPlatte.Add(i);
            }
            SettingsService.Settings.SelectedPlatteIndex = 0;
        }

        IsWorking = false;
        GC.Collect();
        WallpaperColorPlatteChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return new Task(() => { });
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return new Task(() => { });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}