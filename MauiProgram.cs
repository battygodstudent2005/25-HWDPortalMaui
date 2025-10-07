// [新增] 引用 Services 命名空間
using HWDPortalMaui.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace HWDPortalMaui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            // [新增] 從應用程式的組件中讀取嵌入的 appsettings.json 檔案
            var assembly = Assembly.GetExecutingAssembly();
            // [新增] 請注意: "HWDPortalMaui.appsettings.json" 中的 "HWDPortalMaui" 是您的專案預設命名空間，請根據您的實際情況修改
            using var stream = assembly.GetManifestResourceStream("HWDPortalMaui.appsettings.json");

            // [新增] 建立一個 ConfigurationBuilder 並從 stream 中加入 JSON 設定
            var config = new ConfigurationBuilder()
                        .AddJsonStream(stream)
                        .Build();

            // [新增] 將讀取到的設定加入到 MAUI 應用程式的設定中
            builder.Configuration.AddConfiguration(config);


            builder.Services.AddMauiBlazorWebView();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            builder.Services.AddSingleton<UserInfoService>();

            return builder.Build();
        }
    }
}