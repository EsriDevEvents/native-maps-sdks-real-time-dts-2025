using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;
using Esri.ArcGISRuntime;
using Esri.ArcGISRuntime.Security;
using Microsoft.Extensions.DependencyInjection;
using TransitTracker.ViewModel;

namespace TransitTracker;

public partial class App : Application
{
    public static MainViewModel MainViewModel => Ioc.Default.GetRequiredService<MainViewModel>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            //ArcGISRuntimeEnvironment.SetLicense("runtimelite,1000,rud2019458306,none,GB2PMD17JYEPDPF44224");
            ArcGISRuntimeEnvironment.ApiKey = "AAPTxy8BH1VEsoebNVZXo8HurOPpVREEIj3Qjy89qmF9sW_xvYLW8P9PWtFXUa38QW4nIogpe_JB8GZT-DJrGredWkbaBaqS-CrR0WdO3NdMqRepJNrmZ1lymqZc82jP6YTCiSREruZWmUVvyKhz8gJom-KM-nKfpxMF_L-oEdgVG0REudzOy6GnSsI_wRlbJ7R08Di4RAVH7H0872ZJGEC-q_5OXPEFokcG78QIUUuGfUI.AT1_LYtXV0z2";
            ArcGISRuntimeEnvironment.Initialize();

            AuthenticationManager.Current.ChallengeHandler = new ChallengeHandler(async (info) =>
            {
                return await AccessTokenCredential.CreateAsync(info.ServiceUri!, "rt_velocity1", "rt_velocity01");
            });

            Ioc.Default.ConfigureServices(new ServiceCollection()
                .AddSingleton<MainViewModel>()
                .BuildServiceProvider());
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
