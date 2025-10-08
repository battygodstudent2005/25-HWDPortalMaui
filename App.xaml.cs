using HWDPortalMaui.Services;

namespace HWDPortalMaui
{
    public partial class App : Application
    {
        public App(UpdateService updateService)
        {
            InitializeComponent();

            MainPage = new MainPage();

        }

        // 覆寫 CreateWindow 方法來設定視窗的初始位置與大小
        protected override Window CreateWindow(IActivationState activationState)
        {
            var window = base.CreateWindow(activationState);

            // --- 設定視窗大小 ---
            // 您可以在這裡自訂想要的視窗寬度
            const int WindowWidth = 1280;
            // 您可以在這裡自訂想要的視窗高度
            const int WindowHeight = 800;

            window.Width = WindowWidth;
            window.Height = WindowHeight;


            // --- 計算螢幕中心點並設定視窗位置 ---
            // 取得當前主螢幕的資訊
            var displayInfo = DeviceDisplay.Current.MainDisplayInfo;

            // 計算螢幕的中心點 X 座標
            double screenX = (displayInfo.Width / displayInfo.Density - window.Width) / 2;
            // 計算螢幕的中心點 Y 座標
            double screenY = (displayInfo.Height / displayInfo.Density - window.Height) / 2;

            // 設定視窗的 X, Y 座標
            window.X = screenX;
            window.Y = screenY;

            return window;
        }
    }
}
