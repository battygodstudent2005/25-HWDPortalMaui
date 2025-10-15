// 引入必要的命名空間
using Microsoft.AspNetCore.Components;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace HWDPortalMaui.Services
{
    // 應用程式更新服務
    public class UpdateService
    {
        // 安裝檔所在的網路路徑
        private const string UpdateServerPath = @"\\tpea31hwdfs01\Automation\Automation Software\HWD_Portal_Maui\Setup";

        // 用於儲存最新安裝檔的路徑，以便在使用者同意後執行
        private string _latestInstallerPath;


        // 修改建構函式以注入 IServiceProvider
        // 這樣我們才能在需要時安全地取得 NavigationManager
        public UpdateService()
        {
        }

        // 檢查是否有新版本的非同步方法
        public async Task CheckForUpdateAsync(NavigationManager navigationManager)
        {
            try
            {
                // 取得目前應用程式的版本號
                var currentVersion = new Version(VersionTracking.CurrentVersion);
                Debug.WriteLine($"[Update Service] Current Version: {currentVersion}");

                // 檢查網路路徑是否存在
                if (!Directory.Exists(UpdateServerPath))
                {
                    Debug.WriteLine($"[Update Service] Error: Network path not found at {UpdateServerPath}");
                    return;
                }

                // 尋找遠端路徑中最新的 msix 安裝檔
                Version latestVersion = null;
                // 將 latestInstallerPath 改為區域變數
                string installerPath = null;

                // 檔名格式為 HWDPortalMaui_X.X.X.X_x64.msix，使用正規表示法來解析版本號
                var regex = new Regex(@"HWDPortalMaui_(\d+\.\d+\.\d+\.\d+)_x64\.msix");

                foreach (var file in Directory.GetFiles(UpdateServerPath, "*.msix"))
                {
                    var match = regex.Match(Path.GetFileName(file));
                    if (match.Success)
                    {
                        if (Version.TryParse(match.Groups[1].Value, out var fileVersion))
                        {
                            if (latestVersion == null || fileVersion > latestVersion)
                            {
                                latestVersion = fileVersion;
                                // 將路徑指派給區域變數
                                installerPath = file;
                            }
                        }
                    }
                }

                if (latestVersion != null)
                {
                    Debug.WriteLine($"[Update Service] Latest version found on server: {latestVersion}");

                    // 將原本的 DisplayAlert 邏輯整個替換掉
                    if (latestVersion > currentVersion)
                    {
                        // 將安裝檔路徑儲存在私有欄位中，供後續使用
                        _latestInstallerPath = installerPath;

                        // 直接使用從參數傳入的 navigationManager
                        // 仍然保留在主執行緒上執行以確保 UI 操作的安全性
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            var url = $"/update-available?latestVersion={latestVersion}&currentVersion={currentVersion}";
                            navigationManager.NavigateTo(url);
                        });
                    }
                    else
                    {
                        Debug.WriteLine("[Update Service] Application is up to date.");
                    }
                }
                else
                {
                    Debug.WriteLine("[Update Service] No valid installer found on the server.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update Service] An error occurred: {ex.Message}");
            }
        }

        // 將方法改為 async Task 以支援非同步操作
        public async Task PerformUpdate()
        {
            try
            {
                // 定義安裝批次檔的網路路徑
                const string batchInstallerPath = @"\\tpea31hwdfs01\Automation\Automation Software\HWD_Portal_Maui\APP\02_HWDPortalMauIinstall.bat";

                // 檢查批次檔是否存在
                if (File.Exists(batchInstallerPath))
                {
                    // 更新偵錯訊息
                    Debug.WriteLine("[Update Service] Starting batch installer...");

                    // 啟動批次檔安裝程式
                    Process.Start(new ProcessStartInfo(batchInstallerPath) { UseShellExecute = true });

                    // 增加延遲時間,讓安裝程式完全啟動
                    await Task.Delay(500);

                    // 使用 Environment.Exit 而不是 Application.Current.Quit()
                    // 這樣可以更乾淨地終止應用程式,避免 WebView2 的清理問題
                    Debug.WriteLine("[Update Service] Exiting application...");
                    Environment.Exit(0);
                }
                else
                {
                    // 更新錯誤訊息，指出找不到批次檔
                    Debug.WriteLine($"[Update Service] Error: Batch installer not found at {batchInstallerPath}.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Update Service] An error occurred during PerformUpdate: {ex.Message}");
            }
        }
    }
}