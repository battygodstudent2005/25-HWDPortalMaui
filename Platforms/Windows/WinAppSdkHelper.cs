// 引入必要的命名空間
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using Windows.Management.Deployment;

// 這個類別專門用來處理 Windows App SDK 啟動引導的邏輯
internal static class WinAppSdkHelper
{
    // 定義我們應用程式需要的 Windows App SDK 最小版本 (1.8)
    private const uint MajorMinorVersion = 0x00010008;

    // 重要：VersionTag 要留空字串或 null，不要用 "stable"
    private const string VersionTag = null;

    // 使用 P/Invoke 導入 Win32 API MessageBox，以便在 MAUI UI 啟動前顯示對話框
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBox(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    // 定義 MessageBox 的樣式
    private const uint MB_OKCANCEL = 0x00000001;
    private const uint MB_ICONWARNING = 0x00000030;
    private const uint MB_ICONERROR = 0x00000010;
    private const int IDOK = 1;

    // 定義 Windows App SDK 安裝程式的下載 URL
    private const string WinAppSdkInstallUrl = "https://aka.ms/windowsappsdk/1.8/latest/windowsappruntimeinstall-x64.exe";

    public static bool CheckAndBootstrap()
    {
        // 優先檢查系統是否已安裝任何 1.8 版本的 Windows App SDK Runtime
        try
        {
            var packageManager = new PackageManager();
            // 搜尋條件改為精確包含 "Microsoft.WindowsAppRuntime.1.8"
            var packages = packageManager.FindPackagesForUser(string.Empty)
                .Where(p => p.Id.Name.Contains("Microsoft.WindowsAppRuntime.1.8"));

            // 如果找到任何符合的套件，就直接認定為成功，並跳過 Bootstrap.Initialize
            if (packages.Any())
            {
                return true; // 符合條件，直接通過
            }
        }
        catch (Exception ex)
        {
            // 如果檢查過程出錯 (例如 PackageManager 無法使用)，則在偵錯時印出訊息，並繼續執行原始的引導邏輯
            Debug.WriteLine($"[WinAppSdkHelper] Pre-check for installed packages failed: {ex.Message}");
        }

        // --- 如果上面的檢查未找到套件，則執行原始的引導邏輯作為後備方案 ---
        try
        {
            // 呼叫 Initialize 方法 (不是 TryInitialize)
            Bootstrap.Initialize(MajorMinorVersion, VersionTag);

            // 如果沒有拋出異常，表示初始化成功
            return true;
        }
        catch (Exception ex)
        {
            // 從系統讀取已安裝的版本
            string installedVersions = GetInstalledVersions();

            // 顯示詳細錯誤訊息以便診斷
            string title = "Windows App SDK 初始化失敗";
            string message = $"無法初始化 Windows App SDK 1.8\n\n" +
                            $"錯誤碼: 0x{ex.HResult:X8}\n" +
                            $"訊息: {ex.Message}\n\n" +
                            $"已安裝版本:\n{installedVersions}\n" +
                            $"需要版本: 1.8\n\n" +
                            GetErrorExplanation(ex.HResult) +
                            $"是否要重新下載安裝程式？";

            if (MessageBox(IntPtr.Zero, message, title, MB_OKCANCEL | MB_ICONERROR) == IDOK)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(WinAppSdkInstallUrl) { UseShellExecute = true });
                }
                catch { }
            }

            Environment.Exit(1);
            return false;
        }
    }

    /// <summary>
    /// 從系統讀取已安裝的 Windows App Runtime 版本
    /// </summary>
    private static string GetInstalledVersions()
    {
        try
        {
            var packageManager = new PackageManager();
            var packages = packageManager.FindPackagesForUser(string.Empty)
                .Where(p => p.Id.Name.Contains("WindowsAppRuntime"))
                .OrderByDescending(p => p.Id.Version.Major)
                .Take(5); // 只顯示前 5 個最新的

            if (packages.Any())
            {
                return string.Join("\n", packages.Select(p =>
                    $"  • {p.Id.Name} v{p.Id.Version.Major}.{p.Id.Version.Minor}.{p.Id.Version.Build}.{p.Id.Version.Revision}"));
            }
            else
            {
                return "  • (未找到已安裝的版本)";
            }
        }
        catch
        {
            return "  • (無法讀取已安裝版本)";
        }
    }

    /// <summary>
    /// 根據錯誤碼提供說明
    /// </summary>
    private static string GetErrorExplanation(int hresult)
    {
        return hresult switch
        {
            unchecked((int)0x80070032) => "錯誤說明: 不支援的請求 (ERROR_NOT_SUPPORTED)\n可能原因: 版本標籤或架構不匹配\n\n",
            unchecked((int)0x80073D54) => "錯誤說明: 找不到套件 (ERROR_PACKAGE_NOT_FOUND)\n\n",
            unchecked((int)0x80070490) => "錯誤說明: 找不到符合的套件 (ERROR_NOT_FOUND)\n\n",
            _ => $"請記下此錯誤碼以便查詢。\n\n"
        };
    }
}