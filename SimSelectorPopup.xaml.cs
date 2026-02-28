using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LiveryManagerApp
{
    public partial class SimSelectorPopup : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private MainWindow _parent;

        public SimSelectorPopup(MainWindow parent)
        {
            InitializeComponent();
            _parent = parent;

            this.Loaded += (s, e) => {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    int trueValue = 1;
                    DwmSetWindowAttribute(hwnd, 20, ref trueValue, sizeof(int));
                }
                catch { }
            };
        }

        private void BtnStore_Click(object sender, RoutedEventArgs e)
        {
            _parent.Engine.UpdateSimVersion("MS_STORE");
            this.DialogResult = true;
        }

        private void BtnSteam_Click(object sender, RoutedEventArgs e)
        {
            _parent.Engine.UpdateSimVersion("STEAM");
            this.DialogResult = true;
        }
        private void BtnCustom_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFolderDialog { Title = "Select Community Folder" };
            if (ofd.ShowDialog() == true)
            {
                _parent.Engine.CurrentConfig.sim_version = "CUSTOM";
                _parent.Engine.CurrentConfig.community_path = ofd.FolderName;
                _parent.Engine.SaveConfig();

                this.DialogResult = true; // Crucial para que el MainWindow sepa que terminamos
                this.Close();
            }
        }
    }
}