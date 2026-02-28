using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LiveryManagerApp
{
    public partial class FolderNameDialog : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public string FinalName { get; private set; } = string.Empty;
        private string _defaultName;

        public FolderNameDialog(string defaultName, string originName)
        {
            InitializeComponent();
            _defaultName = defaultName;

            // Al abrir, mostramos el nombre por defecto en el texto
            LblOrigin.Text = $"Installing: {originName}";
            TxtEntry.Text = defaultName;
            TxtEntry.Focus();
            TxtEntry.SelectAll();

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

        // CORRECCIÓN: Ahora solo pone el nombre en el textbox sin cerrar la ventana
        private void BtnDefault_Click(object sender, RoutedEventArgs e)
        {
            TxtEntry.Text = _defaultName;
            TxtEntry.Focus();
            TxtEntry.SelectAll();
        }

        // Este es el único botón que ahora cierra la ventana y confirma el nombre
        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            string val = TxtEntry.Text.Trim();
            if (string.IsNullOrEmpty(val))
            {
                MessageBox.Show("Please enter a name.");
                return;
            }

            // Limpiamos caracteres inválidos como en el script de Python
            foreach (char c in System.IO.Path.GetInvalidFileNameChars())
            {
                val = val.Replace(c.ToString(), "");
            }

            FinalName = val;
            this.DialogResult = true; // Aquí es donde se da la orden de proceder
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            TxtEntry.Clear();
            TxtEntry.Focus();
        }
    }
}