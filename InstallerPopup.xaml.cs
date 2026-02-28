using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using System.Windows.Interop;

namespace LiveryManagerApp
{
    public partial class InstallerPopup : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private MainWindow _parent;
        private List<string> _filesList = new List<string>();
        private string _targetAircraft = string.Empty;

        public InstallerPopup(MainWindow parent, string[]? preloadedFiles = null)
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

            // Vital para dar feedback de arrastre sin bloquear
            this.DragEnter += Window_DragEnter;

            string savedPath = _parent.Engine.CurrentConfig.last_install_path;
            TxtInstallPath.Text = string.IsNullOrEmpty(savedPath) ? _parent.Engine.CurrentConfig.community_path : savedPath;

            _targetAircraft = _parent.Engine.CurrentConfig.last_aircraft ?? "PMDG 737-800";
            LblTargetAircraft.Text = $"Target Aircraft: {_targetAircraft}";

            bool linkerEnabled = _parent.Engine.CurrentConfig.addon_linker_mode;
            ChkAddonLinker.IsChecked = linkerEnabled;

            if (linkerEnabled)
            {
                ChkCustomName.Visibility = Visibility.Visible;
                ChkCustomName.IsChecked = _parent.Engine.CurrentConfig.ask_custom_name;
            }

            // Si se pasaron archivos desde el MainWindow, los procesamos en segundo plano
            if (preloadedFiles != null)
            {
                ProcessDroppedFilesAsync(preloadedFiles);
            }
        }

        // =====================================================================
        // ESTA ES LA FUNCIÓN QUE FALTABA Y CAUSABA EL ERROR CS1061
        // =====================================================================
        public void AddNewFiles(string[] files)
        {
            ProcessDroppedFilesAsync(files);
        }

        private void ChkAddonLinker_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            bool isChecked = ChkAddonLinker.IsChecked ?? false;
            _parent.Engine.CurrentConfig.addon_linker_mode = isChecked;

            if (isChecked)
            {
                ChkCustomName.Visibility = Visibility.Visible;
                ChkCustomName.IsChecked = _parent.Engine.CurrentConfig.ask_custom_name;
                MessageBox.Show(this, "Addon Linker Mode ENABLED.\n\nEach livery will be installed in its own separate folder inside Community.", "Mode Changed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                ChkCustomName.Visibility = Visibility.Collapsed;
                ChkCustomName.IsChecked = false;
                MessageBox.Show(this, "Addon Linker Mode DISABLED.\n\nAll liveries will be installed into the single main folder.", "Mode Changed", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            _parent.Engine.SaveConfig();
        }

        private void ChkCustomName_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            _parent.Engine.CurrentConfig.ask_custom_name = ChkCustomName.IsChecked ?? false;
            _parent.Engine.SaveConfig();
        }

        // =====================================================================
        // LÓGICA DE DRAG & DROP DESBLOQUEADA
        // =====================================================================
        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
                {
                    string[] clonedFiles = (string[])files.Clone();
                    e.Handled = true; // Libera el Explorador instantáneamente

                    ProcessDroppedFilesAsync(clonedFiles);
                }
            }
        }

        private async void ProcessDroppedFilesAsync(string[] files)
        {
            // Pequeña pausa para que Windows destrabe el mouse completamente
            await Task.Delay(10);

            // Validamos los archivos en un hilo separado
            await Task.Run(() =>
            {
                var valid = files.Where(f => Directory.Exists(f) || f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)).ToList();

                Dispatcher.Invoke(() =>
                {
                    foreach (var f in valid)
                    {
                        if (!_filesList.Contains(f)) _filesList.Add(f);
                    }

                    TxtFiles.Text = _filesList.Count == 1 ? Path.GetFileName(_filesList[0]) : $"{_filesList.Count} items loaded";
                    LblStatus.Text = $"Ready to install {_filesList.Count} items.";
                });
            });
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog { Multiselect = true, Filter = "Zip|*.zip|Ini|*.ini|All|*.*" };
            if (ofd.ShowDialog() == true)
            {
                ProcessDroppedFilesAsync(ofd.FileNames);
            }
        }

        private void BtnChangePath_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog ofd = new OpenFolderDialog();
            if (ofd.ShowDialog() == true)
            {
                TxtInstallPath.Text = ofd.FolderName;
                _parent.Engine.CurrentConfig.last_install_path = ofd.FolderName;
                _parent.Engine.SaveConfig();
            }
        }

        private void BtnDefaultCommunity_Click(object sender, RoutedEventArgs e)
        {
            string defaultPath = _parent.Engine.CurrentConfig.community_path;
            TxtInstallPath.Text = defaultPath;
            _parent.Engine.CurrentConfig.last_install_path = defaultPath;
            _parent.Engine.SaveConfig();
        }

        private void AddFilesToList(string[] files)
        {
            ProcessDroppedFilesAsync(files);
        }

        private void BtnClearAll_Click(object sender, RoutedEventArgs e)
        {
            _filesList.Clear();
            TxtFiles.Text = "Drag files here...";
            LblStatus.Text = "Ready.";
            ProgressBarStatus.Value = 0;
            BtnInstall.IsEnabled = true;
        }

        private void BtnFixPaths_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c reg add \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\FileSystem\" /v LongPathsEnabled /t REG_DWORD /d 1 /f",
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch { }
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_filesList.Count == 0) return;
            BtnInstall.IsEnabled = false;

            string target = TxtInstallPath.Text;
            bool linker = ChkAddonLinker.IsChecked ?? false;

            await Task.Run(() => RunInstall(target, linker));
        }

        // =============================================================================
        // LÓGICA DE INSTALACIÓN MASIVA
        // =============================================================================
        private void RunInstall(string installTarget, bool useLinker)
        {
            int total = _filesList.Count;
            int current = 0;

            string driveRoot = Path.GetPathRoot(installTarget) ?? "C:\\";
            string tempDir = Path.Combine(driveRoot, "_PMDG_WIP_CACHE");
            Directory.CreateDirectory(tempDir);

            HashSet<string> foldersToRegenerate = new HashSet<string>();
            Dictionary<string, int> installCounts = new Dictionary<string, int>();

            foreach (string item in _filesList)
            {
                current++;
                string fileName = Path.GetFileName(item);
                UpdateUI(current, total, $"Processing {fileName}...");

                try
                {
                    if (item.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        string extractPath = Path.Combine(tempDir, $"pkg_{Guid.NewGuid()}");
                        ZipFile.ExtractToDirectory(item, extractPath, true);

                        var result = ProcessExtractedFolder(extractPath, installTarget, useLinker, fileName);
                        if (!string.IsNullOrEmpty(result.destRoot))
                        {
                            foldersToRegenerate.Add(result.destRoot);
                            if (installCounts.ContainsKey(result.detectedAc)) installCounts[result.detectedAc]++;
                            else installCounts[result.detectedAc] = 1;
                        }
                    }
                    else if (Directory.Exists(item))
                    {
                        var result = ProcessExtractedFolder(item, installTarget, useLinker, fileName);
                        if (!string.IsNullOrEmpty(result.destRoot))
                        {
                            foldersToRegenerate.Add(result.destRoot);
                            if (installCounts.ContainsKey(result.detectedAc)) installCounts[result.detectedAc]++;
                            else installCounts[result.detectedAc] = 1;
                        }
                    }
                    else if (item.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateUI(current, total, $"Installing config {fileName}...");
                        InstallIniToWasm(item, _targetAircraft);
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => MessageBox.Show($"Error installing {fileName}:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                }
            }

            try { Directory.Delete(tempDir, true); } catch { }

            if (foldersToRegenerate.Count > 0)
            {
                int layoutTotal = foldersToRegenerate.Count;
                int layoutCurrent = 0;
                foreach (string folder in foldersToRegenerate)
                {
                    layoutCurrent++;
                    UpdateUI(total, total, $"Regenerating Layout ({layoutCurrent}/{layoutTotal})...");
                    _parent.Engine.RunLayoutGeneratorSafeMove(folder);
                }
            }

            List<string> lines = new List<string>();
            foreach (var kvp in installCounts)
            {
                string noun = kvp.Value == 1 ? "livery" : "liveries";
                lines.Add($"- {kvp.Key}: {kvp.Value} {noun}");
            }

            string listText = lines.Count > 0 ? string.Join("\n", lines) : "Files processed successfully.";
            string finalMessage = $"Installation Complete!\n\nSummary:\n{listText}";

            Dispatcher.Invoke(() => {
                MessageBox.Show(this, finalMessage, "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                _parent.BtnRefresh_Click(this, new RoutedEventArgs());
                this.Close();
            });
        }

        // =============================================================================
        // PROCESAMIENTO Y CARPETAS
        // =============================================================================
        private (string destRoot, string detectedAc) ProcessExtractedFolder(string sourceFolder, string targetCommFolder, bool useLinker, string originalName)
        {
            string? liverySrc = FindLiveryFolder(sourceFolder);
            if (string.IsNullOrEmpty(liverySrc)) return (string.Empty, _targetAircraft);

            string exactFolderName = new DirectoryInfo(liverySrc).Name;

            if (exactFolderName.StartsWith("pkg_", StringComparison.OrdinalIgnoreCase))
            {
                exactFolderName = Path.GetFileNameWithoutExtension(originalName);
            }

            var analysis = AnalyzeConfig(liverySrc);
            string actualAircraft = analysis.aircraft;
            string atcId = analysis.atcId;

            var acData = _parent.Engine.AircraftDB[actualAircraft];
            string managerName = acData.Type == "IFLY" ? "ifly-manager-liveries" : "pmdg-manager-liveries";

            string destRoot;

            if (useLinker)
            {
                string uniqueName = exactFolderName.ToLower().Replace(" ", "-").Replace("_", "-");
                bool askCustom = Dispatcher.Invoke(() => ChkCustomName.IsChecked ?? false);

                if (askCustom)
                {
                    uniqueName = Dispatcher.Invoke(() => {
                        var dialog = new FolderNameDialog(uniqueName, originalName);
                        dialog.Owner = this;
                        if (dialog.ShowDialog() == true) return dialog.FinalName;
                        return uniqueName;
                    });
                }
                destRoot = Path.Combine(targetCommFolder, uniqueName);
            }
            else
            {
                destRoot = Path.Combine(targetCommFolder, managerName);
            }

            Directory.CreateDirectory(destRoot);

            string layoutPath = Path.Combine(destRoot, "layout.json");
            string manifestPath = Path.Combine(destRoot, "manifest.json");

            if (!File.Exists(layoutPath)) File.WriteAllText(layoutPath, "{}");
            if (!File.Exists(manifestPath))
            {
                string title = useLinker ? new DirectoryInfo(destRoot).Name : managerName;
                string manifest = $"{{\"dependencies\": [], \"content_type\": \"AIRCRAFT\", \"title\": \"{title}\", \"package_version\": \"1.0.0\"}}";
                File.WriteAllText(manifestPath, manifest);
            }

            string targetSimObjects = acData.Type == "IFLY"
                ? Path.Combine(destRoot, "SimObjects", "Airplanes")
                : Path.Combine(destRoot, "SimObjects", "Airplanes", acData.SimFolder, "liveries", "pmdg");

            Directory.CreateDirectory(targetSimObjects);

            foreach (string iniFile in Directory.GetFiles(sourceFolder, "*.ini", SearchOption.AllDirectories))
            {
                if (Path.GetFileName(iniFile).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                if (acData.Type == "IFLY")
                {
                    InstallIniToWasm(iniFile, actualAircraft, null);
                }
                else
                {
                    if (atcId != "UNKNOWN") InstallIniToWasm(iniFile, actualAircraft, atcId);
                    else InstallIniToWasm(iniFile, actualAircraft, null);
                }
            }

            string finalDestFolderName = exactFolderName;

            if (liverySrc != sourceFolder)
            {
                finalDestFolderName = new DirectoryInfo(liverySrc).Name;
            }

            string finalDest = Path.Combine(targetSimObjects, finalDestFolderName);

            if (Directory.Exists(finalDest)) Directory.Delete(finalDest, true);
            MoveDirectory(liverySrc, finalDest);

            return (destRoot, actualAircraft);
        }

        private string? FindLiveryFolder(string root)
        {
            var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories);
            var target = files.FirstOrDefault(f =>
                f.EndsWith("aircraft.cfg", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("livery.cfg", StringComparison.OrdinalIgnoreCase) ||
                f.EndsWith("livery.json", StringComparison.OrdinalIgnoreCase)
            );

            if (target != null) return Path.GetDirectoryName(target);
            return null;
        }

        private (string aircraft, string atcId) AnalyzeConfig(string liveryDir)
        {
            string detectedAc = _targetAircraft;
            string atcId = "UNKNOWN";

            try
            {
                string allText = "";

                var configFiles = Directory.GetFiles(liveryDir, "*.*").Where(f => f.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));
                foreach (var file in configFiles)
                {
                    allText += File.ReadAllText(file) + "\n";
                }

                using (StringReader reader = new StringReader(allText))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string clean = line.Trim().Replace("\"", "").Replace("'", "").Replace(",", "");

                        if (clean.StartsWith("atc_id", StringComparison.OrdinalIgnoreCase) ||
                            clean.StartsWith("registration", StringComparison.OrdinalIgnoreCase) ||
                            clean.StartsWith("TailNumber", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = clean.Split(new char[] { '=', ':' });
                            if (parts.Length > 1 && atcId == "UNKNOWN")
                            {
                                atcId = parts[1].Trim();
                            }
                        }
                    }
                }

                if (allText.IndexOf("pmdg-aircraft-77f", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("b77f", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("777F", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detectedAc = _parent.Engine.AircraftDB.Keys.FirstOrDefault(k => k.Contains("777F")) ?? detectedAc;
                }
                else if (allText.IndexOf("pmdg-aircraft-77w", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("b77w", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("777-300", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detectedAc = _parent.Engine.AircraftDB.Keys.FirstOrDefault(k => k.Contains("777-300")) ?? detectedAc;
                }
                else if (allText.IndexOf("pmdg-aircraft-772", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("b772", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("777-200ER", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("77er", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detectedAc = _parent.Engine.AircraftDB.Keys.FirstOrDefault(k => k.Contains("777-200ER")) ?? detectedAc;
                }
                else if (allText.IndexOf("pmdg-aircraft-77l", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("b77l", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("777-200LR", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detectedAc = _parent.Engine.AircraftDB.Keys.FirstOrDefault(k => k.Contains("777-200LR") || k.EndsWith("200LR")) ?? detectedAc;
                }
                else if (allText.IndexOf("pmdg-aircraft-736", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("b736", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("737-600", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detectedAc = _parent.Engine.AircraftDB.Keys.FirstOrDefault(k => k.Contains("600")) ?? detectedAc;
                }
                else if (allText.IndexOf("pmdg-aircraft-737", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("b737", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("737-700", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detectedAc = _parent.Engine.AircraftDB.Keys.FirstOrDefault(k => k.Contains("700")) ?? detectedAc;
                }
                else if (allText.IndexOf("pmdg-aircraft-738", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("b738", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("737-800", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detectedAc = _parent.Engine.AircraftDB.Keys.FirstOrDefault(k => k.Contains("800")) ?? detectedAc;
                }
                else if (allText.IndexOf("pmdg-aircraft-739", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("b739", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("737-900", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detectedAc = _parent.Engine.AircraftDB.Keys.FirstOrDefault(k => k.Contains("900")) ?? detectedAc;
                }
                else if (allText.IndexOf("ifly", StringComparison.OrdinalIgnoreCase) >= 0 || allText.IndexOf("max 8", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    detectedAc = _parent.Engine.AircraftDB.Keys.FirstOrDefault(k => k.IndexOf("iFly", StringComparison.OrdinalIgnoreCase) >= 0) ?? detectedAc;
                }

            }
            catch { }

            return (detectedAc, atcId);
        }

        private void InstallIniToWasm(string iniPath, string targetAircraft, string? customName = null)
        {
            if (Path.GetFileName(iniPath).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return;
            if (!_parent.Engine.AircraftDB.ContainsKey(targetAircraft)) return;

            var acData = _parent.Engine.AircraftDB[targetAircraft];
            string wasm = acData.Wasm;
            string simVersion = _parent.Engine.CurrentConfig.sim_version;

            string basePathStr = simVersion == "MS_STORE" || simVersion == "CUSTOM"
                ? (acData.Type == "IFLY" ? $"%localappdata%\\Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalState\\WASM\\MSFS2020\\{wasm}\\work" : $"%localappdata%\\Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalState\\WASM\\MSFS2024\\{wasm}\\work\\Aircraft")
                : (acData.Type == "IFLY" ? $"%appdata%\\Microsoft Flight Simulator 2024\\WASM\\MSFS2020\\{wasm}\\work" : $"%appdata%\\Microsoft Flight Simulator 2024\\WASM\\MSFS2024\\{wasm}\\work\\Aircraft");

            string expandedPath = Environment.ExpandEnvironmentVariables(basePathStr);
            Directory.CreateDirectory(expandedPath);

            string fileName = customName != null ? $"{customName}.ini" : Path.GetFileName(iniPath);
            string destFile = Path.Combine(expandedPath, fileName);
            File.Copy(iniPath, destFile, true);
        }

        private void MoveDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source)) MoveDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }

        private void UpdateUI(int current, int total, string text)
        {
            Dispatcher.Invoke(() => {
                ProgressBarStatus.Value = ((double)current / total) * 100;
                LblStatus.Text = text;
            });
        }
    }
}