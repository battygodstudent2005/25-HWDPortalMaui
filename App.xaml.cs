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
    }
}
