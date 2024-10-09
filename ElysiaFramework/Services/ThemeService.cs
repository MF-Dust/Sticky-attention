using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using ElysiaFramework.Interfaces;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace ElysiaFramework.Services;

public class ThemeService : IHostedService, IThemeService
{
    public event EventHandler<ThemeUpdatedEventArgs>? ThemeUpdated;
    public int CurrentRealThemeMode { get; set; } = 0; // ����� set ��Ҫ�� public

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // ������Խ��������ʼ�������������߼�
        await Task.CompletedTask; // ���������δ����ʵ��
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // ������Խ��������߼�
        await Task.CompletedTask; // ���������δ����ʵ��
    }

    public void SetTheme(int themeMode, Color primary, Color secondary)
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();

        // ��¼��һ��������ɫ
        var lastPrimary = theme.PrimaryMid.Color;
        var lastSecondary = theme.SecondaryMid.Color;
        var lastBaseTheme = theme.GetBaseTheme();

        // ��������ģʽѡ������
        SelectTheme(theme, themeMode);

        // ������ɫ����
        SetColorAdjustment(theme);

        // ������ɫ�ʹ�ɫ
        theme.SetPrimaryColor(primary);
        theme.SetSecondaryColor(secondary);

        // ��������Ƿ��б仯
        if (HasThemeChanged(lastPrimary, lastSecondary, lastBaseTheme, paletteHelper.GetTheme()))
        {
            // ��������
            paletteHelper.SetTheme(theme);
            OnThemeUpdated(themeMode, primary, secondary);
        }
    }

    private void SelectTheme(ITheme theme, int themeMode)
    {
        switch (themeMode)
        {
            case 0:
                SetDynamicTheme(theme);
                break;
            case 1:
                theme.SetBaseTheme(new MaterialDesignLightTheme());
                break;
            case 2:
                theme.SetBaseTheme(new MaterialDesignDarkTheme());
                break;
        }
    }

    private void SetDynamicTheme(ITheme theme)
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize"))
            {
                if (key != null)
                {
                    var appsUseLightTheme = (int?)key.GetValue("AppsUseLightTheme");
                    theme.SetBaseTheme(appsUseLightTheme == 0 ? new MaterialDesignDarkTheme() : new MaterialDesignLightTheme());
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Ȩ�޲���ʱʹ��Ĭ������
            theme.SetBaseTheme(new MaterialDesignLightTheme());
        }
        catch (Exception ex)
        {
            // ��¼�����쳣����ѡ��
            Console.WriteLine($"���ö�̬����ʱ��������: {ex.Message}");
            theme.SetBaseTheme(new MaterialDesignLightTheme());
        }
    }

    private void SetColorAdjustment(ITheme theme)
    {
        ((Theme)theme).ColorAdjustment = new ColorAdjustment
        {
            DesiredContrastRatio = 4.5F,
            Contrast = Contrast.Medium,
            Colors = ColorSelection.All
        };
    }

    private bool HasThemeChanged(Color lastPrimary, Color lastSecondary, BaseTheme lastBaseTheme, ITheme currentTheme)
    {
        return lastPrimary != currentTheme.PrimaryMid.Color ||
               lastSecondary != currentTheme.SecondaryMid.Color ||
               lastBaseTheme != currentTheme.GetBaseTheme();
    }

    private void OnThemeUpdated(int themeMode, Color primary, Color secondary)
    {
        ThemeUpdated?.Invoke(this, new ThemeUpdatedEventArgs
        {
            ThemeMode = themeMode,
            Primary = primary,
            Secondary = secondary,
            RealThemeMode = (CurrentRealThemeMode = (primary == Colors.White ? 0 : 1)) // �ɸ���ʵ���߼�����
        });
    }
}
