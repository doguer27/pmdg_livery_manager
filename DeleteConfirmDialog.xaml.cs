using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace LiveryManagerApp
{
    public partial class DeleteConfirmDialog : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public DeleteConfirmDialog(string liveryName, string thumbnailPath)
        {
            InitializeComponent();

            this.Loaded += (s, e) => {
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    int trueValue = 1;
                    DwmSetWindowAttribute(hwnd, 20, ref trueValue, sizeof(int));
                }
                catch { }
            };

            TxtLiveryName.Text = liveryName;

            // Cargamos la imagen a prueba de bloqueos
            if (!string.IsNullOrEmpty(thumbnailPath) && File.Exists(thumbnailPath))
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(thumbnailPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad; // Libera el archivo físico al instante
                    bitmap.EndInit();
                    ImgThumbnail.Source = bitmap;
                }
                catch { }
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = true; // El usuario dijo SÍ
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // El usuario se arrepintió
        }
    }
}