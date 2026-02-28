using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace LiveryManagerApp
{
    // =============================================================================
    // DATA MODELS
    // =============================================================================
    public class AircraftData
    {
        public string Type { get; set; } = string.Empty;
        public string SimFolder { get; set; } = string.Empty;
        public string Wasm { get; set; } = string.Empty;
        public bool HasVariants { get; set; }
        public Dictionary<string, string> VariantMap { get; set; } = new Dictionary<string, string>();
        public bool HasWinglets { get; set; }
        public string BaseContainer { get; set; } = string.Empty;
    }

    public class AppConfig
    {
        public string community_path { get; set; } = string.Empty;
        public string sim_version { get; set; } = "MS_STORE";
        public string last_aircraft { get; set; } = "PMDG 737-800";
        public string last_install_path { get; set; } = string.Empty;
        public string last_run_version { get; set; } = "1.0.7";
        public bool addon_linker_mode { get; set; } = false;
        public bool ask_custom_name { get; set; } = false;
    }

    public class LiveryItem
    {
        public string Name { get; set; } = string.Empty;
        public string LivPath { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty;
        public string Tags { get; set; } = string.Empty;
    }

    // =============================================================================
    // THE ENGINE (CORE LOGIC)
    // =============================================================================
    public class LiveryEngine
    {
        private const string CONFIG_FILE = "pmdg_manager_config.json";

        public Dictionary<string, AircraftData> AircraftDB { get; private set; } = new Dictionary<string, AircraftData>();
        public AppConfig CurrentConfig { get; set; } = new AppConfig();
        public List<LiveryItem> AllLiveriesData { get; private set; } = new List<LiveryItem>();

        private string _configPath = string.Empty;
        private string _generatorPath = string.Empty;

        public void Initialize()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string appFolder = Path.Combine(appData, "Livery_Manager_App");

            if (!Directory.Exists(appFolder)) Directory.CreateDirectory(appFolder);

            _configPath = Path.Combine(appFolder, "pmdg_manager_config.json");
            _generatorPath = Path.Combine(appFolder, "MSFSLayoutGenerator.exe");

            if (!File.Exists(_generatorPath))
            {
                try
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    string? resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("MSFSLayoutGenerator.exe", StringComparison.OrdinalIgnoreCase));

                    if (resourceName != null)
                    {
                        using (Stream stream = assembly.GetManifestResourceStream(resourceName)!)
                        using (FileStream fileStream = new FileStream(_generatorPath, FileMode.Create))
                        {
                            stream.CopyTo(fileStream);
                        }
                    }
                }
                catch { }
            }

            InitializeDatabase();
            LoadConfig();

            if (string.IsNullOrEmpty(CurrentConfig.community_path) || !Directory.Exists(CurrentConfig.community_path))
            {
                string detected = FindCommunityFolderAuto();

                if (!string.IsNullOrEmpty(detected) && Directory.Exists(detected))
                {
                    CurrentConfig.community_path = detected;
                    SaveConfig();
                }
            }
        }

        public void LoadConfig()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    string json = File.ReadAllText(_configPath);
                    CurrentConfig = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();
                }
                else
                {
                    CurrentConfig = new AppConfig();
                    SaveConfig();
                }
            }
            catch
            {
                CurrentConfig = new AppConfig();
                SaveConfig();
            }
        }

        public void SaveConfig()
        {
            try
            {
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                string json = System.Text.Json.JsonSerializer.Serialize(CurrentConfig, options);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        private void InitializeDatabase()
        {
            AircraftDB = new Dictionary<string, AircraftData>
            {
                { "PMDG 737-600", new AircraftData { Type = "PMDG", SimFolder = "PMDG 737-600", Wasm = "pmdg-aircraft-736", HasVariants = false, HasWinglets = false }},
                { "PMDG 737-800", new AircraftData { Type = "PMDG", SimFolder = "PMDG 737-800", Wasm = "pmdg-aircraft-738", HasVariants = true, HasWinglets = true, VariantMap = new Dictionary<string, string> { { "PAX", "b738_ext" }, { "BDSF", "b738bdsf_ext" }, { "BCF", "b738bcf_ext" }, { "BBJ2", "b73bbj2_ext" } }}},
                { "PMDG 737-900", new AircraftData { Type = "PMDG", SimFolder = "PMDG 737-900", Wasm = "pmdg-aircraft-739", HasVariants = true, HasWinglets = true, VariantMap = new Dictionary<string, string> { { "900", "b739_ext" }, { "900ER", "b739er_ext" } }}},
                { "PMDG 777-300ER", new AircraftData { Type = "PMDG", SimFolder = "PMDG 777-300ER", Wasm = "pmdg-aircraft-77w", HasVariants = false }},
                { "PMDG 777-200ER", new AircraftData { Type = "PMDG", SimFolder = "PMDG 777-200ER", Wasm = "pmdg-aircraft-77er", HasVariants = true, VariantMap = new Dictionary<string, string> { { "General Electric", "engine_ge" }, { "Rolls Royce", "engine_rr" }, { "Pratt & Whitney", "engine_pw" } }}},
                { "PMDG 777-200LR", new AircraftData { Type = "PMDG", SimFolder = "PMDG 777-200LR", Wasm = "pmdg-aircraft-77l", HasVariants = false }},
                { "PMDG 777F", new AircraftData { Type = "PMDG", SimFolder = "PMDG 777F", Wasm = "pmdg-aircraft-77f", HasVariants = false }},
                { "iFly 737 MAX 8", new AircraftData { Type = "IFLY", BaseContainer = "..\\iFly 737-MAX8", Wasm = "ifly-aircraft-737max8", HasVariants = false }}
            };
        }

        private string FindCommunityFolderAuto()
        {
            string cfgPath = CurrentConfig.sim_version == "MS_STORE"
                ? Environment.ExpandEnvironmentVariables(@"%localappdata%\Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt")
                : Environment.ExpandEnvironmentVariables(@"%appdata%\Microsoft Flight Simulator 2024\UserCfg.opt");

            if (File.Exists(cfgPath))
            {
                try
                {
                    var lines = File.ReadAllLines(cfgPath);
                    foreach (var line in lines.Reverse())
                    {
                        if (line.Trim().StartsWith("InstalledPackagesPath"))
                        {
                            string pathPart = line.Split(new[] { "InstalledPackagesPath" }, StringSplitOptions.None)[1].Trim().Replace("\"", "");
                            return Path.Combine(pathPart, "Community");
                        }
                    }
                }
                catch { }
            }
            return "";
        }

        public void UpdateLastAircraft(string selectedAc)
        {
            CurrentConfig.last_aircraft = selectedAc;
            SaveConfig();
        }

        public void UpdateSimVersion(string version)
        {
            CurrentConfig.sim_version = version;
            CurrentConfig.community_path = FindCommunityFolderAuto();
            SaveConfig();
        }

        public async Task ScanLiveriesAsync(string selectedAc)
        {
            AllLiveriesData.Clear();
            if (string.IsNullOrEmpty(CurrentConfig.community_path) || !Directory.Exists(CurrentConfig.community_path)) return;

            var acData = AircraftDB[selectedAc];
            string commPath = CurrentConfig.community_path;

            await Task.Run(() =>
            {
                if (acData.Type == "IFLY")
                {
                    foreach (var package in Directory.GetDirectories(commPath))
                    {
                        string searchRoot = Path.Combine(package, "SimObjects", "Airplanes");
                        if (Directory.Exists(searchRoot))
                        {
                            foreach (var fleet in Directory.GetDirectories(searchRoot))
                            {
                                string cfg = Path.Combine(fleet, "aircraft.cfg");
                                if (File.Exists(cfg))
                                {
                                    bool isTarget = false;
                                    string title = new DirectoryInfo(package).Name;
                                    bool foundBase = false;
                                    bool foundTitle = false;

                                    try
                                    {
                                        using (FileStream fs = new FileStream(cfg, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                        using (StreamReader reader = new StreamReader(fs))
                                        {
                                            string? line;
                                            int readCount = 0;
                                            while ((line = reader.ReadLine()) != null && readCount < 400)
                                            {
                                                readCount++;
                                                string cleanLine = line.Trim();

                                                if (!foundBase && cleanLine.StartsWith("base_container", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (cleanLine.IndexOf(acData.BaseContainer, StringComparison.OrdinalIgnoreCase) >= 0)
                                                    {
                                                        isTarget = true;
                                                    }
                                                    foundBase = true;
                                                }
                                                else if (!foundTitle && cleanLine.StartsWith("title", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    var parts = cleanLine.Split('=');
                                                    if (parts.Length > 1)
                                                    {
                                                        title = parts[1].Trim().Trim('"', '\'');
                                                        foundTitle = true;
                                                    }
                                                }

                                                if (foundBase && foundTitle) break;
                                            }
                                        }
                                    }
                                    catch { }

                                    if (isTarget)
                                    {
                                        AllLiveriesData.Add(new LiveryItem { Name = title, LivPath = fleet, RootPath = package, Tags = "" });
                                    }
                                }
                            }
                        }
                    }
                }
                else // PMDG
                {
                    string targetRel = Path.Combine("SimObjects", "Airplanes", acData.SimFolder, "liveries", "pmdg");
                    foreach (var package in Directory.GetDirectories(commPath))
                    {
                        string checkPath = Path.Combine(package, targetRel);
                        if (Directory.Exists(checkPath))
                        {
                            foreach (var liv in Directory.GetDirectories(checkPath))
                            {
                                string cfg = Path.Combine(liv, "livery.cfg");
                                string dName = new DirectoryInfo(liv).Name;
                                string tagsStr = "";

                                if (File.Exists(cfg))
                                {
                                    bool foundName = false;
                                    bool foundTags = false;

                                    try
                                    {
                                        using (FileStream fs = new FileStream(cfg, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                        using (StreamReader reader = new StreamReader(fs))
                                        {
                                            string? line;
                                            while ((line = reader.ReadLine()) != null)
                                            {
                                                string cleanLine = line.Trim();
                                                if (!foundName && cleanLine.StartsWith("name", StringComparison.OrdinalIgnoreCase) && cleanLine.Contains('='))
                                                {
                                                    dName = cleanLine.Split('=')[1].Trim().Replace("\"", "");
                                                    foundName = true;
                                                }
                                                else if (!foundTags && cleanLine.StartsWith("required_tags", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    tagsStr = cleanLine.Split('=')[1].Trim().ToLower();
                                                    foundTags = true;
                                                }

                                                if (foundName && foundTags) break;
                                            }
                                        }
                                    }
                                    catch { }
                                }
                                AllLiveriesData.Add(new LiveryItem { Name = dName, LivPath = liv, RootPath = package, Tags = tagsStr });
                            }
                        }
                    }
                }
            });
        }

        public List<LiveryItem> GetFilteredLiveries(string selectedAc, string filterText, string selVariant, bool showSsw, bool showBw)
        {
            var filtered = new List<LiveryItem>();
            if (!AircraftDB.ContainsKey(selectedAc)) return filtered;

            var acData = AircraftDB[selectedAc];
            filterText = filterText?.ToLower() ?? "";

            foreach (var item in AllLiveriesData)
            {
                if (!string.IsNullOrEmpty(filterText) && !item.Name.ToLower().Contains(filterText)) continue;

                if (acData.HasVariants)
                {
                    if (selVariant != "All")
                    {
                        string reqTags = acData.VariantMap[selVariant];
                        if (!reqTags.Split(',').All(t => item.Tags.Contains(t.Trim()))) continue;
                    }
                    else
                    {
                        // FILTRO ANTI-COLADOS: Si está en "All", debe tener al menos una de las etiquetas válidas de este avión.
                        bool isValidForAc = false;
                        foreach (var reqTags in acData.VariantMap.Values)
                        {
                            if (reqTags.Split(',').All(t => item.Tags.Contains(t.Trim())))
                            {
                                isValidForAc = true;
                                break;
                            }
                        }
                        // Si la livery no tiene NINGUNA etiqueta válida de este avión (ej. es de 736), se oculta.
                        if (!isValidForAc) continue;
                    }
                }
                else if (acData.Type == "PMDG")
                {
                    // Anti-colados estricto para los PMDG que no tienen menú de variantes explícito
                    string baseTag = "";
                    if (selectedAc.Contains("737-600")) baseTag = "b736_ext";
                    else if (selectedAc.Contains("777F")) baseTag = "b77f_ext";
                    else if (selectedAc.Contains("777-300ER")) baseTag = "b77w_ext";
                    else if (selectedAc.Contains("777-200LR")) baseTag = "b77l_ext";

                    if (!string.IsNullOrEmpty(baseTag) && !item.Tags.Contains(baseTag))
                    {
                        continue;
                    }
                }

                if (acData.HasWinglets)
                {
                    bool isSsw = item.Tags.Contains("ssw_l") || item.Tags.Contains("ssw_r");
                    bool isBw = item.Tags.Contains("bw_l") || item.Tags.Contains("bw_r");
                    if (isSsw && !showSsw) continue;
                    if (isBw && !showBw) continue;
                }

                filtered.Add(item);
            }

            return filtered;
        }

        public async Task<string> FindThumbnailAsync(string path)
        {
            return await Task.Run(() =>
            {
                List<string> candidates = new List<string>();
                string[] subFolders = { "thumbnail", "texture", "." };
                string[] extensions = { "jpg", "png", "jpeg" };

                foreach (string sub in subFolders)
                {
                    foreach (string ext in extensions)
                    {
                        string checkPath = sub == "."
                            ? Path.Combine(path, $"thumbnail.{ext}")
                            : Path.Combine(path, sub, $"thumbnail.{ext}");
                        candidates.Add(checkPath);
                    }
                }

                if (Directory.Exists(path))
                {
                    try
                    {
                        var texDirs = Directory.GetDirectories(path, "texture.*");
                        foreach (var dir in texDirs)
                        {
                            foreach (string ext in extensions)
                            {
                                candidates.Add(Path.Combine(dir, $"thumbnail.{ext}"));
                            }
                        }
                    }
                    catch { }
                }

                foreach (string candidatePath in candidates)
                {
                    if (File.Exists(candidatePath)) return candidatePath;
                }

                return string.Empty;
            });
        }

        private void ForceDeleteDirectory(string targetDir)
        {
            if (!Directory.Exists(targetDir)) return;
            try
            {
                Directory.Delete(targetDir, true);
            }
            catch
            {
                var dirInfo = new DirectoryInfo(targetDir);
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    file.Attributes = FileAttributes.Normal;
                }
                Directory.Delete(targetDir, true);
            }
        }

        public bool DeleteLivery(string path, string name, string aircraftName)
        {
            try
            {
                CleanUpWasmIni(path, aircraftName);

                if (Directory.Exists(path))
                {
                    ForceDeleteDirectory(path);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error deleting livery: " + ex.Message);
                return false;
            }
        }

        private void CleanUpWasmIni(string liveryDir, string aircraftName)
        {
            try
            {
                if (!AircraftDB.ContainsKey(aircraftName)) return;
                var acData = AircraftDB[aircraftName];

                string wasm = acData.Wasm;
                string simVersion = CurrentConfig.sim_version;

                string basePathStr = simVersion == "MS_STORE" || simVersion == "CUSTOM"
                    ? (acData.Type == "IFLY" ? $"%localappdata%\\Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalState\\WASM\\MSFS2020\\{wasm}\\work" : $"%localappdata%\\Packages\\Microsoft.Limitless_8wekyb3d8bbwe\\LocalState\\WASM\\MSFS2024\\{wasm}\\work\\Aircraft")
                    : (acData.Type == "IFLY" ? $"%appdata%\\Microsoft Flight Simulator 2024\\WASM\\MSFS2020\\{wasm}\\work" : $"%appdata%\\Microsoft Flight Simulator 2024\\WASM\\MSFS2024\\{wasm}\\work\\Aircraft");

                string expandedPath = Environment.ExpandEnvironmentVariables(basePathStr);

                var configFiles = Directory.GetFiles(liveryDir, "*.*", SearchOption.AllDirectories)
                                           .Where(f => f.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ||
                                                       f.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

                string allText = "";
                foreach (var file in configFiles)
                {
                    allText += File.ReadAllText(file) + "\n";
                }

                using (StringReader reader = new StringReader(allText))
                {
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string clean = line.Trim();

                        if (acData.Type == "IFLY")
                        {
                            if (clean.StartsWith("title", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = clean.Split(new char[] { '=' }, 2);
                                if (parts.Length > 1)
                                {
                                    string targetIniName = parts[1].Trim().Trim('"', '\'', ' ');
                                    string targetWasmIni = Path.Combine(expandedPath, $"{targetIniName}.ini");

                                    if (File.Exists(targetWasmIni)) File.Delete(targetWasmIni);
                                }
                            }
                        }
                        else
                        {
                            string cleanNoQuotes = clean.Replace("\"", "").Replace("'", "").Replace(",", "");
                            if (cleanNoQuotes.StartsWith("atc_id", StringComparison.OrdinalIgnoreCase) ||
                                cleanNoQuotes.StartsWith("registration", StringComparison.OrdinalIgnoreCase) ||
                                cleanNoQuotes.StartsWith("TailNumber", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = cleanNoQuotes.Split(new char[] { '=', ':' }, 2);
                                if (parts.Length > 1)
                                {
                                    string targetIniName = parts[1].Trim();
                                    string targetWasmIni = Path.Combine(expandedPath, $"{targetIniName}.ini");

                                    if (File.Exists(targetWasmIni)) File.Delete(targetWasmIni);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public void RunLayoutGeneratorSafeMove(string fullPath)
        {
            if (!Directory.Exists(fullPath)) return;

            bool hasLiveries = false;
            try
            {
                var cfgs = Directory.EnumerateFiles(fullPath, "*.cfg", SearchOption.AllDirectories);
                foreach (var c in cfgs)
                {
                    string n = Path.GetFileName(c).ToLower();
                    if (n == "livery.cfg" || n == "aircraft.cfg")
                    {
                        hasLiveries = true;
                        break;
                    }
                }
            }
            catch { hasLiveries = true; }

            if (!hasLiveries)
            {
                ForceDeleteDirectory(fullPath);
                return;
            }

            string tempContainer = "";
            string tempWorkPath = "";

            try
            {
                string driveRoot = Path.GetPathRoot(fullPath) ?? "C:\\";
                tempContainer = Path.Combine(driveRoot, "_DG_GENERATOR_TMP");
                if (!Directory.Exists(tempContainer)) Directory.CreateDirectory(tempContainer);

                string folderName = new DirectoryInfo(fullPath).Name;
                tempWorkPath = Path.Combine(tempContainer, folderName);

                if (Directory.Exists(tempWorkPath)) ForceDeleteDirectory(tempWorkPath);

                Directory.Move(fullPath, tempWorkPath);

                string creatorExe = Path.Combine(tempWorkPath, "MSFSLayoutGenerator.exe");
                if (File.Exists(creatorExe))
                {
                    try { File.SetAttributes(creatorExe, FileAttributes.Normal); File.Delete(creatorExe); } catch { }
                }

                if (File.Exists(_generatorPath))
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = _generatorPath,
                        Arguments = "layout.json",
                        WorkingDirectory = tempWorkPath,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";

                    using (Process proc = Process.Start(psi)!)
                    {
                        proc.WaitForExit(15000);
                    }
                }

                System.Threading.Thread.Sleep(300);

                if (Directory.Exists(fullPath)) ForceDeleteDirectory(fullPath);
                Directory.Move(tempWorkPath, fullPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error regenerando layout: {ex.Message}");
                try
                {
                    if (Directory.Exists(tempWorkPath) && !Directory.Exists(fullPath))
                    {
                        Directory.Move(tempWorkPath, fullPath);
                    }
                }
                catch { }
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempContainer) && !Directory.EnumerateFileSystemEntries(tempContainer).Any())
                    {
                        Directory.Delete(tempContainer);
                    }
                }
                catch { }
            }
        }
    }
}