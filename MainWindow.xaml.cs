using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LiveryManagerApp
{
    public partial class MainWindow : Window
    {
        // =====================================================================
        // CONFIGURACIÓN DE ACTUALIZACIONES (Cámbialo si es necesario)
        // =====================================================================
        private const string CURRENT_VERSION = "v2.0-r1"; // Tu versión actual
        private const string GITHUB_REPO = "doguer27/livery_manager_cs";

        public LiveryEngine Engine { get; private set; }

        private InstallerPopup? _currentPopup = null;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public MainWindow()
        {
            InitializeComponent();
            Engine = new LiveryEngine();
            Engine.Initialize();
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int trueValue = 1;
                DwmSetWindowAttribute(hwnd, 20, ref trueValue, sizeof(int));
            }
            catch { }

            if (string.IsNullOrEmpty(Engine.CurrentConfig.community_path) || !Directory.Exists(Engine.CurrentConfig.community_path))
            {
                SimSelectorPopup selector = new SimSelectorPopup(this);
                selector.Owner = this;
                bool? result = selector.ShowDialog();

                if (result != true)
                {
                    Application.Current.Shutdown();
                    return;
                }
            }

            SetupUI();

            // Lanzar la comprobación de actualizaciones de forma silenciosa en segundo plano
            Task.Run(() => CheckForUpdatesAsync());
        }

        // =====================================================================
        // SISTEMA DE AUTO-UPDATE (GITHUB API)
        // =====================================================================
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    // GitHub exige un User-Agent para sus peticiones de API
                    client.DefaultRequestHeaders.Add("User-Agent", "LiveryManagerApp");
                    string url = $"https://api.github.com/repos/{GITHUB_REPO}/releases/latest";
                    string response = await client.GetStringAsync(url);

                    using (JsonDocument doc = JsonDocument.Parse(response))
                    {
                        var root = doc.RootElement;
                        string latestVersion = root.GetProperty("tag_name").GetString() ?? "";

                        // Si hay una versión más nueva en GitHub...
                        if (!string.IsNullOrEmpty(latestVersion) && latestVersion != CURRENT_VERSION)
                        {
                            string installerUrl = "";

                            // Buscamos el archivo .exe (Tu instalador) dentro del release
                            foreach (var asset in root.GetProperty("assets").EnumerateArray())
                            {
                                string assetName = asset.GetProperty("name").GetString() ?? "";
                                if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                {
                                    installerUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                    break;
                                }
                            }

                            // Si encontramos el instalador, mostramos el aviso al usuario
                            if (!string.IsNullOrEmpty(installerUrl))
                            {
                                Dispatcher.Invoke(() => ShowUpdateDialog(latestVersion, installerUrl));
                            }
                        }
                    }
                }
            }
            catch { /* Si no hay internet o falla, lo ignoramos silenciosamente */ }
        }

        private async void ShowUpdateDialog(string newVersion, string installerUrl)
        {
            var result = MessageBox.Show(this,
                $"A new version of the Manager is available! ({newVersion})\n\nWould you like to update now?",
                "Update Available",
                MessageBoxButton.YesNo, MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes)
            {
                await DownloadAndRunInstaller(installerUrl);
            }
        }

        private async Task DownloadAndRunInstaller(string installerUrl)
        {
            Window? waitWindow = null;
            try
            {
                // Ventana temporal bloqueante para que sepa que está descargando
                waitWindow = new Window()
                {
                    Title = "Downloading Update...",
                    Width = 350,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    WindowStyle = WindowStyle.ToolWindow,
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F1225")),
                    Foreground = Brushes.White,
                    Content = new TextBlock
                    {
                        Text = "Downloading new installer...\nPlease wait.",
                        Foreground = Brushes.White,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextAlignment = TextAlignment.Center
                    }
                };
                waitWindow.Show();

                // Lo bajamos a la carpeta %TEMP%
                string tempInstallerPath = Path.Combine(Path.GetTempPath(), $"LiveryManagerInstaller_{Guid.NewGuid().ToString().Substring(0, 8)}.exe");

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("User-Agent", "LiveryManagerApp");
                    byte[] fileBytes = await client.GetByteArrayAsync(installerUrl);
                    await File.WriteAllBytesAsync(tempInstallerPath, fileBytes);
                }

                waitWindow.Close();

                // Ejecutamos el Instalador descargado pasándole la orden secreta "-autoupdate"
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = tempInstallerPath,
                    Arguments = "-autoupdate",
                    UseShellExecute = true
                };
                Process.Start(psi);

                // Matamos el Manager actual para que el instalador pueda sobreescribirlo
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                if (waitWindow != null) waitWindow.Close();
                MessageBox.Show(this, "Failed to download update: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // =====================================================================


        private void SetupUI()
        {
            CmbAircraft.SelectionChanged -= CmbAircraft_SelectionChanged;
            CmbSimVersion.SelectionChanged -= CmbSimVersion_SelectionChanged;

            CmbAircraft.Items.Clear();
            foreach (var key in Engine.AircraftDB.Keys)
            {
                CmbAircraft.Items.Add(key);
            }
            CmbAircraft.SelectedItem = Engine.CurrentConfig.last_aircraft;

            CmbAircraft.SelectionChanged += CmbAircraft_SelectionChanged;

            foreach (ComboBoxItem item in CmbSimVersion.Items)
            {
                if (item.Content?.ToString() == Engine.CurrentConfig.sim_version)
                {
                    CmbSimVersion.SelectedItem = item;
                    break;
                }
            }

            CmbSimVersion.SelectionChanged += CmbSimVersion_SelectionChanged;

            UpdateAircraftUI(Engine.CurrentConfig.last_aircraft);
            RefreshLiveryGrid();
        }

        private void UpdateAircraftUI(string selectedAc)
        {
            if (string.IsNullOrEmpty(selectedAc) || !Engine.AircraftDB.ContainsKey(selectedAc)) return;

            var acData = Engine.AircraftDB[selectedAc];

            if (acData.HasVariants)
            {
                PnlVariant.Visibility = Visibility.Visible;
                CmbVariant.SelectionChanged -= CmbVariant_SelectionChanged;
                CmbVariant.Items.Clear();
                CmbVariant.Items.Add("All");
                foreach (var variant in acData.VariantMap.Keys) CmbVariant.Items.Add(variant);
                CmbVariant.SelectedIndex = 0;
                CmbVariant.SelectionChanged += CmbVariant_SelectionChanged;
            }
            else
            {
                PnlVariant.Visibility = Visibility.Collapsed;
            }

            PnlWinglets.Visibility = acData.HasWinglets ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CmbAircraft_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbAircraft.SelectedItem == null) return;

            string selectedAc = CmbAircraft.SelectedItem.ToString() ?? "";
            Engine.UpdateLastAircraft(selectedAc);
            UpdateAircraftUI(selectedAc);
            RefreshLiveryGrid();
        }

        private void CmbVariant_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) RenderCards(); }
        private void ChkFilter_Changed(object sender, RoutedEventArgs e) { if (IsLoaded) RenderCards(); }

        private void CmbSimVersion_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded || CmbSimVersion.SelectedItem == null) return;

            if (CmbSimVersion.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                string version = item.Content.ToString() ?? "MS_STORE";

                if (version == "CUSTOM")
                {
                    var ofd = new Microsoft.Win32.OpenFolderDialog
                    {
                        Title = "Select your MSFS Community Folder"
                    };

                    if (ofd.ShowDialog() == true)
                    {
                        Engine.CurrentConfig.sim_version = "CUSTOM";
                        Engine.CurrentConfig.community_path = ofd.FolderName;
                        Engine.SaveConfig();
                        RefreshLiveryGrid();
                    }
                    else
                    {
                        foreach (ComboBoxItem it in CmbSimVersion.Items)
                        {
                            if (it.Content?.ToString() == Engine.CurrentConfig.sim_version)
                            {
                                CmbSimVersion.SelectedItem = it;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    Engine.UpdateSimVersion(version);
                    RefreshLiveryGrid();
                }
            }
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            BtnClearSearch.Visibility = string.IsNullOrWhiteSpace(TxtSearch.Text) ? Visibility.Collapsed : Visibility.Visible;
            RenderCards();
        }

        private void BtnClearSearch_Click(object sender, RoutedEventArgs e) { TxtSearch.Text = ""; }
        public void BtnRefresh_Click(object sender, RoutedEventArgs e) { RefreshLiveryGrid(); }

        private void BtnAddLivery_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPopup != null && _currentPopup.IsLoaded)
            {
                _currentPopup.Activate();
                return;
            }

            _currentPopup = new InstallerPopup(this);
            _currentPopup.Owner = this;
            _currentPopup.Closed += (s, args) => _currentPopup = null;
            _currentPopup.ShowDialog();
        }

        private void BtnDonate_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo { FileName = "https://paypal.me/doguer26", UseShellExecute = true });
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    string[] clonedFiles = (string[])files.Clone();
                    e.Handled = true;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (_currentPopup != null && _currentPopup.IsLoaded)
                        {
                            _currentPopup.AddNewFiles(clonedFiles);
                            _currentPopup.Activate();
                        }
                        else
                        {
                            _currentPopup = new InstallerPopup(this, clonedFiles);
                            _currentPopup.Owner = this;
                            _currentPopup.Closed += (s, args) => _currentPopup = null;
                            _currentPopup.ShowDialog();
                        }
                    }));
                }
            }
        }

        private async void RefreshLiveryGrid()
        {
            if (!IsLoaded) return;

            string selectedAc = CmbAircraft.SelectedItem?.ToString() ?? "";
            if (string.IsNullOrEmpty(selectedAc)) return;

            LblStats.Text = $"Scanning {selectedAc} liveries...";
            LiveriesPanel.Children.Clear();

            await Engine.ScanLiveriesAsync(selectedAc);
            RenderCards();
        }

        private void RenderCards()
        {
            LiveriesPanel.Children.Clear();
            string selectedAc = CmbAircraft.SelectedItem?.ToString() ?? "";
            if (string.IsNullOrEmpty(selectedAc)) return;

            string filterText = TxtSearch.Text;
            string selVariant = CmbVariant.SelectedItem?.ToString() ?? "All";
            bool showSsw = ChkSSW.IsChecked ?? true;
            bool showBw = ChkBW.IsChecked ?? true;

            var filteredLiveries = Engine.GetFilteredLiveries(selectedAc, filterText, selVariant, showSsw, showBw);

            foreach (var item in filteredLiveries)
            {
                LiveriesPanel.Children.Add(CreateCard(item));
            }

            LblStats.Text = $"{selectedAc}: {filteredLiveries.Count} Liveries Found";
        }

        private Border CreateCard(LiveryItem item)
        {
            Border card = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1E3F")),
                CornerRadius = new CornerRadius(15),
                Margin = new Thickness(15),
                Width = 340
            };

            StackPanel sp = new StackPanel { Margin = new Thickness(15) };

            System.Windows.Shapes.Rectangle imgRect = new System.Windows.Shapes.Rectangle
            {
                RadiusX = 15,
                RadiusY = 15,
                Width = 310,
                Height = 170,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F1225"))
            };

            LoadThumbnailToRectangle(item.LivPath, imgRect);

            TextBlock txt = new TextBlock
            {
                Text = item.Name,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 14,
                Margin = new Thickness(10, 18, 10, 18),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 45
            };

            Button btnDel = new Button
            {
                Content = "🗑 Delete Livery",
                Style = (Style)FindResource("DeleteButtonStyle")
            };

            btnDel.Click += async (s, e) =>
            {
                string rutaMiniatura = await Engine.FindThumbnailAsync(item.LivPath);

                var confirmDialog = new DeleteConfirmDialog(item.Name, rutaMiniatura);
                confirmDialog.Owner = this;

                if (confirmDialog.ShowDialog() != true)
                {
                    return;
                }

                btnDel.IsEnabled = false;
                btnDel.Content = "Deleting...";

                string currentAircraft = CmbAircraft.SelectedItem?.ToString() ?? Engine.CurrentConfig.last_aircraft;

                await Task.Run(() =>
                {
                    if (Engine.DeleteLivery(item.LivPath, item.Name, currentAircraft))
                    {
                        Engine.RunLayoutGeneratorSafeMove(item.RootPath);
                    }
                });

                RefreshLiveryGrid();
            };

            sp.Children.Add(imgRect);
            sp.Children.Add(txt);
            sp.Children.Add(btnDel);
            card.Child = sp;

            return card;
        }

        private async void LoadThumbnailToRectangle(string path, System.Windows.Shapes.Rectangle rectControl)
        {
            string foundPath = await Engine.FindThumbnailAsync(path);

            if (!string.IsNullOrEmpty(foundPath))
            {
                try
                {
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(foundPath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();

                    RenderOptions.SetBitmapScalingMode(bitmap, BitmapScalingMode.HighQuality);

                    ImageBrush brush = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
                    rectControl.Fill = brush;
                }
                catch { }
            }
        }
    }
}