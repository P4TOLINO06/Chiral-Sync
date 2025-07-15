using ClickableTransparentOverlay;
using ImGuiNET;
using Microsoft.VisualBasic.Logging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Vulkan.Win32;


namespace ChiralSync
{
    public class Program : Overlay
    {
        public Program() : base(
        "Chiral Sync",
        false, // DPIAware
        Screen.PrimaryScreen.Bounds.Width,
        Screen.PrimaryScreen.Bounds.Height)
        {
            this.currentWidth = Screen.PrimaryScreen.Bounds.Width;
            this.currentHeight = Screen.PrimaryScreen.Bounds.Height;
            this.VSync = true;
            this.FPSLimit = 60;
        }

        [DllImport("user32.dll")]

        static extern short GetAsyncKeyState(int sKey);

        private CancellationTokenSource cts;
        private Task hotkeyTask;

        private string selectedBkFolder = "";
        private string selectedDestFolder = "";
        private bool foldersLoaded = false;

        bool showWindow = true;
        bool wasWindowHidden = true;

        // Backup
        float progress = 0.0f;
        string[] filesToCopy = null;
        int currentFileIndex = 0;
        private bool isBackupRunning = false;
        private bool waitingToShowDoneIcon = false;
        private DateTime backupDoneTime;
        private bool backupError = false;
        private DateTime backupErrorTime;
        bool useSubFolderBk = false;
        bool autoIncrementSubfolder = false;
        string subFolderName = "Backup";
        int count = 1; // Used for auto-incrementing subfolder names
        int selectedLimit = 4; // Default to 5 GB limit
        float limitGb = 5.0f; // Default to 5 GB

        bool waitingForOverlayKey = false;
        bool waitingForBackupKey = false;
        bool capKey = false;
        Keys lastKey = Keys.None;
        bool tempCtrl = false;
        bool tempAlt = false;
        bool tempShift = false;
        bool ctrlPressed;
        bool altPressed;
        bool shiftPressed;

        // Overlay hotkey
        Keys hotkeyMain = Keys.Insert;
        bool hotkeyCtrl = false;
        bool hotkeyAlt = false;
        bool hotkeyShift = false;
        bool hotkeyWasDown = false;
        bool hotkeyBKWasDown = false;
        bool insertWasDown = false;

        // Backup hotkey
        Keys hotkeyMainBK = Keys.None;
        bool hotkeyCtrlBK = false;
        bool hotkeyAltBK = false;
        bool hotkeyShiftBK = false;

        // Error Message
        string? errorMessage = null;
        private DateTime errorMessageTime = DateTime.Now;
        private bool isExceptionMessage = false;

        // Ui Scale
        private float uiScale = 1.0f;
        private float lastUiScale = 1.0f;
        private int currentWidth;
        private int currentHeight;
        bool resizeRequested = false;

        protected override void Render()
        {
            LoadHotkeyFromConfig(); // Loads the previously defined hotkeys.

            // Loads the last two previously selected paths (Backup/Destination path)
            var (backupFolders, destFolders, lastBkPath, lastDestPath, BkCount, subFolderBk, autoIncSubFolder, nameSubFolder, wScale, bkLimits ,_, _) = LoadFoldersConfig();

            if (!foldersLoaded)
            {
                selectedBkFolder = lastBkPath;
                selectedDestFolder = lastDestPath;
                count = BkCount > 0 ? BkCount : 1; // Ensure count starts at 1 if no previous backups
                useSubFolderBk = subFolderBk;
                autoIncrementSubfolder = autoIncSubFolder;
                subFolderName = nameSubFolder ?? "Backup";
                selectedLimit = bkLimits;
                uiScale = wScale;
                foldersLoaded = true;
            }

            BackupIcon();// Backup status icon

            (ctrlPressed, altPressed, shiftPressed) = GetModifiers();

            bool pathConflict =
                !string.IsNullOrWhiteSpace(selectedBkFolder) &&
                !string.IsNullOrWhiteSpace(selectedDestFolder) &&
                Path.GetFullPath(selectedBkFolder).Equals(Path.GetFullPath(selectedDestFolder), StringComparison.OrdinalIgnoreCase);

            if (!waitingForOverlayKey)
            {
                bool isHotKeyPressed = IsKeyDown(hotkeyMain);

                if (isHotKeyPressed && !hotkeyWasDown && ctrlPressed == hotkeyCtrl && altPressed == hotkeyAlt && shiftPressed == hotkeyShift)
                {
                    showWindow = !showWindow;
                }
                hotkeyWasDown = isHotKeyPressed;
            }

            bool isInsertPressed = IsKeyDown(Keys.Insert);

            if (isInsertPressed && !insertWasDown)
            {
                showWindow = !showWindow;
            }
            insertWasDown = isInsertPressed;

            if (showWindow)
            {

                if (wasWindowHidden)
                {
                    resizeRequested = true;
                    wasWindowHidden = false;
                }
                else if (!showWindow)
                {
                    wasWindowHidden = true;
                }

                ImGui.SetNextWindowSizeConstraints(
                    new Vector2(600, 450),   
                    new Vector2(900, 675)   
                );

                Vector2 windowSize = wasWindowHidden
                ? new Vector2(600, 450)
                : new Vector2(600 * uiScale, 450 * uiScale);

                if (resizeRequested || uiScale != lastUiScale)
                {
                    ImGui.StyleColorsDark();
                    ImGui.SetNextWindowSize(windowSize, ImGuiCond.Always);

                    lastUiScale = uiScale;
                    resizeRequested = false;
                }

                if (ImGui.Begin("ChiralSync"))
                {
                    ImGui.SetWindowFontScale(1.0f);
                    Vector2 availableSpace = ImGui.GetContentRegionAvail();
                    float footerHeight = 65;
                    ImGui.BeginChild("ScrollableRegion", new Vector2(availableSpace.X, availableSpace.Y - footerHeight), ImGuiChildFlags.None, ImGuiWindowFlags.AlwaysVerticalScrollbar);

                    ImGui.Text("SETTINGS");
                    ImGui.Separator();

                    // Backup Folder
                    ImGui.BeginGroup();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Backup Folder");
                    float bkTextPosY = ImGui.GetCursorPosY();

                    ImGui.SameLine();

                    Vector2 btnBkFolderSize = new Vector2(20, 18);
                    float bkButtonHeight = ImGui.GetTextLineHeightWithSpacing();
                    ImGui.SetCursorPosY(bkTextPosY - 21);

                    DrawAlgnBtn("->###btnBk", btnBkFolderSize, bkButtonHeight, () =>
                        Task.Run(() => ShowFolderDialog(true))
                    );
                    ShowHelper("Select the folder to backup");

                    ImGui.EndGroup();

                    ImGui.SameLine();

                    // Destination Folder
                    ImGui.BeginGroup();
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text("Destination Folder");
                    float destTextPosY = ImGui.GetCursorPosY();

                    ImGui.SameLine();

                    Vector2 btnDestFolderSize = new Vector2(20, 18);
                    float destButtonHeight = ImGui.GetTextLineHeightWithSpacing();
                    ImGui.SetCursorPosY(destTextPosY - 21);

                    DrawAlgnBtn("->###btnDest", btnDestFolderSize, destButtonHeight, () =>
                        Task.Run(() => ShowFolderDialog(false))
                    );
                    ShowHelper("Select the destination folder to save the backup.");

                    ImGui.EndGroup();

                    // Display selected folders
                    if (ImGui.CollapsingHeader("Selected Folders"))
                    {
                        DrawFolderSelector("Backup Folder", ref selectedBkFolder, "btnBkX", bkLastPath: true);
                        DrawFolderSelector("Destination Folder", ref selectedDestFolder, "btnDestX", pathConflict ? "It is not possible to create a backup to identical paths. Please select different paths." : null, bkLastPath: true, openFolder: true);


                        if (backupFolders.Any() || destFolders.Any())
                        {
                            if (ImGui.CollapsingHeader("Paths saved"))
                            {
                                ImGui.Text("Backup Folders:");
                                ShowHelper("Click the X button with the left mouse button to select the path or with the right mouse button to remove the path.");

                                foreach (var path in backupFolders.Where(p =>
                                    string.IsNullOrEmpty(selectedBkFolder) || !Path.GetFullPath(p).Equals(Path.GetFullPath(selectedBkFolder), StringComparison.OrdinalIgnoreCase)))
                                {
                                    string temp = path;
                                    DrawFolderSelector(" ", ref temp, $"bk_{path}");
                                    if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                                    {
                                        selectedBkFolder = path;
                                        SaveLastPaths(selectedBkFolder, selectedDestFolder);
                                    }
                                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                        RemovePath(path, true);
                                }

                                ImGui.Separator();

                                ImGui.Text("Destination Folders:");
                                foreach (var path in destFolders.Where(p =>
                                    string.IsNullOrEmpty(selectedDestFolder) || !Path.GetFullPath(p).Equals(Path.GetFullPath(selectedDestFolder), StringComparison.OrdinalIgnoreCase)))
                                {
                                    string temp = path;
                                    DrawFolderSelector(" ", ref temp, $"dest_{path}");
                                    if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                                    {
                                        selectedDestFolder = path;
                                        SaveLastPaths(selectedBkFolder, selectedDestFolder);
                                    }
                                    if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                                        RemovePath(path, false);
                                }
                            }
                        }
                    }

                    FoldersConfig(selectedBkFolder, selectedDestFolder);

                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), errorMessage);

                        // Displays an error message for 7 seconds when an exception occurs
                        if (isExceptionMessage)
                        {
                            if ((DateTime.Now - errorMessageTime).TotalSeconds >= 7)
                            {
                                errorMessage = string.Empty;
                                isExceptionMessage = false;
                            }
                        }
                    }

                    if (!pathConflict && Path.Exists(selectedDestFolder) && Path.Exists(selectedBkFolder) && !backupError)
                        errorMessage = string.Empty;

                    if (ImGui.Button("Save Backup"))
                    {
                        Backup(pathConflict, true);
                    }

                    if (isBackupRunning)
                    {
                        ImGui.ProgressBar(progress, new Vector2(300, 25), $"{progress * 100:F0}%");
                    }
                    else if (waitingToShowDoneIcon)
                    {
                        ImGui.Text("Backup completed successfully!");

                        if ((DateTime.Now - backupDoneTime).TotalSeconds >= 2)
                            waitingToShowDoneIcon = false;
                    }

                    ImGui.EndChild();

                    ImGui.BeginChild("FooterRegion", new Vector2(availableSpace.X, footerHeight), ImGuiChildFlags.None);

                    //Chiral Settings
                    if (ImGui.CollapsingHeader("Chiral Settings"))
                    {
                        // Backup Settings
                        if (ImGui.CollapsingHeader("Backup Settings"))
                        {
                            ImGui.Checkbox("Save backup in subfolder", ref useSubFolderBk);

                            ShowHelper("Create a folder with the entered name inside the selected Destination folder.");

                            if (useSubFolderBk)
                            {
                                ImGui.SameLine();

                                ImGui.PushItemWidth(200);
                                if (ImGui.InputText("Subfolder Name", ref subFolderName, 56))
                                {
                                    if (!Regex.IsMatch(subFolderName, @"^[a-zA-Z0-9 ]*$"))
                                    {
                                        subFolderName = "Backup"; // Reset to default if invalid
                                        ImGui.PushStyleColor(ImGuiCol.Text, new System.Numerics.Vector4(1, 0, 0, 1)); // red
                                        ImGui.Text("Only letters and numbers are allowed.");
                                        ImGui.PopStyleColor();
                                    }
                                }

                                ImGui.Checkbox("Auto-increment subfolder name", ref autoIncrementSubfolder);

                                ShowHelper("If enabled, backup folders will be named like 'Backup', 'Backup 2', 'Backup 3'... If not selected, the backup will overwrite the previous one.");

                                SaveBkConfigs();
                            }

                            // Backup Limit

                            string[] limits = { "1 GB", "2 GB", "3 GB", "4 GB", "5 GB", "6 GB", "7 GB", "8 GB", "9 GB", "10 GB" };
                            float[] values = { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f, 9f, 10f };

                            if (ImGui.Combo("Limit (GB)", ref selectedLimit, limits, limits.Length))
                            {
                                limitGb = values[selectedLimit];

                                SaveLimits(selectedLimit);
                            }

                            ShowHelper("Limits the Backup size (it's recommended to change the value only if you're sure the Backup will exceed the default limit (5GB)). If the limit is reached, the backup will stop.");
                        }

                        // Ui Scale & Hotkeys
                        if (ImGui.CollapsingHeader("Ui Scale & Hotkeys"))
                        {
                            // Ui Scale
                            float[] presetScales = new float[] { 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f };

                            ImGui.Text("UI Scale:");
                            ImGui.SameLine();

                            foreach (float scale in presetScales)
                            {
                                string label = scale.ToString("0.00");
                                bool isSelected = Math.Abs(uiScale - scale) < 0.01f;

                                if (isSelected)
                                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.8f, 0.3f, 1.0f)); // green button if selected

                                if (ImGui.Button(label))
                                {
                                    uiScale = scale;
                                    resizeRequested = true;

                                    SaveScale(uiScale);
                                }

                                if (isSelected)
                                    ImGui.PopStyleColor();

                                ImGui.SameLine();
                            }

                            ImGui.NewLine();
                            ShowHelper("Select a scale preset for UI");

                            // Hotkeys
                            if (ImGui.Button(waitingForOverlayKey ? "Press desired hotkey..." : $"Toggle Overlay Key (Current: {FormatHotkey(hotkeyCtrl, hotkeyAlt, hotkeyShift, hotkeyMain)})"))
                            {
                                waitingForOverlayKey = true;
                                waitingForBackupKey = false;
                                capKey = false;
                                lastKey = Keys.None;
                            }

                            if (ImGui.Button(waitingForBackupKey ? "Press desired hotkey for Backup..." : $"Toggle Backup Key (Current: {FormatHotkey(hotkeyCtrlBK, hotkeyAltBK, hotkeyShiftBK, hotkeyMainBK)})"))
                            {
                                waitingForBackupKey = true;
                                waitingForOverlayKey = false;
                                capKey = false;
                                lastKey = Keys.None;
                            }

                            if (waitingForOverlayKey || waitingForBackupKey)
                            {
                                if (!capKey)
                                {
                                    for (int vk = 0x08; vk <= 0xFE; vk++)
                                    {
                                        if ((GetAsyncKeyState(vk) & 0x8000) != 0 &&
                                            vk != (int)Keys.ControlKey &&
                                            vk != (int)Keys.Menu &&
                                            vk != (int)Keys.ShiftKey &&
                                            vk != (int)Keys.LControlKey &&
                                            vk != (int)Keys.RControlKey &&
                                            vk != (int)Keys.LShiftKey &&
                                            vk != (int)Keys.RShiftKey &&
                                            vk != (int)Keys.LMenu &&
                                            vk != (int)Keys.RMenu)
                                        {
                                            (tempCtrl, tempAlt, tempShift) = GetModifiers();
                                            lastKey = (Keys)vk;
                                            capKey = true;
                                            break;
                                        }
                                    }
                                }

                                if (capKey)
                                {
                                    bool allReleased = true;
                                    for (int vk = 0x08; vk <= 0xFE; vk++)
                                    {
                                        if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                                        {
                                            allReleased = false;
                                            break;
                                        }
                                    }

                                    if (allReleased && lastKey != Keys.None)
                                    {
                                        if (waitingForOverlayKey)
                                        {
                                            hotkeyMain = lastKey;
                                            hotkeyCtrl = tempCtrl;
                                            hotkeyAlt = tempAlt;
                                            hotkeyShift = tempShift;
                                            SaveHotKey(hotkeyMain, hotkeyCtrl, hotkeyAlt, hotkeyShift, true);
                                        }
                                        else if (waitingForBackupKey)
                                        {
                                            hotkeyMainBK = lastKey;
                                            hotkeyCtrlBK = tempCtrl;
                                            hotkeyAltBK = tempAlt;
                                            hotkeyShiftBK = tempShift;
                                            SaveHotKey(hotkeyMainBK, hotkeyCtrlBK, hotkeyAltBK, hotkeyShiftBK, false);
                                        }

                                        waitingForOverlayKey = false;
                                        waitingForBackupKey = false;
                                        capKey = false;
                                        lastKey = Keys.None;
                                    }
                                }
                            }
                        }
                    }

                    if (ImGui.IsItemClicked())
                        footerHeight = 65;
                    else
                        footerHeight = 80;


                    ImGui.Separator();

                    if (ImGui.Button("Exit", new Vector2(35, 20)))
                    {
                        StopThreads();
                        Environment.Exit(0);
                    }

                    ImGui.EndChild();

                }
                ImGui.End();
            }

        }

        class FullConfig
        {
            public List<string> BackupFolders { get; set; } = new();
            public List<string> DestFolders { get; set; } = new();
            public string LastBackupFolder { get; set; }
            public string LastDestFolder { get; set; }

            public int BkFolderCount { get; set; }
            public bool UseSubFolderBk { get; set; } = false;
            public bool AutoIncrementSubFolder { get; set; } = false;
            public string SubFolderName { get; set; } = "Backup";
            public int LimitIndex { get; set; } = 5; // Default to 5 GB

            public float WindowScale { get; set; } = 1.0f;

            public HotkeyConfig Hotkey { get; set; } = new();
            public HotkeyConfig HotkeyBK { get; set; } = new();
        }

        FullConfig LoadConfig()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChiralSync Backup");
            string configFile = Path.Combine(appDataPath, "foldersConfig.json");

            if (File.Exists(configFile))
            {
                try
                {
                    string json = File.ReadAllText(configFile);
                    return JsonSerializer.Deserialize<FullConfig>(json) ?? new();
                }
                catch { }
            }

            return new();
        }

        public void StartThreads()
        {
            cts = new CancellationTokenSource();
            var token = cts.Token;

            hotkeyTask = Task.Run(() => HotkeyLoop(token), token);
        }

        public void StopThreads()
        {
            cts.Cancel();

            try
            {
                hotkeyTask?.Wait();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException))
            {
            }
        }
        private void HotkeyLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var (ctrl, alt, shift) = GetModifiers();
                bool isHotKeyBkPressed = IsKeyDown(hotkeyMainBK);

                if (isHotKeyBkPressed && !hotkeyBKWasDown && ctrl == hotkeyCtrlBK && alt == hotkeyAltBK && shift == hotkeyShiftBK)
                {
                    bool conflict = !string.IsNullOrWhiteSpace(selectedBkFolder) &&
                            !string.IsNullOrWhiteSpace(selectedDestFolder) &&
                            Path.GetFullPath(selectedBkFolder).Equals(Path.GetFullPath(selectedDestFolder), StringComparison.OrdinalIgnoreCase);

                    Backup(conflict, true);
                }
                hotkeyBKWasDown = isHotKeyBkPressed;

                Thread.Sleep(16);
            }
        }

        void Backup(bool pathConflict, bool isHotkeyBk = false)
        {

            if (!ArePathsValid(out errorMessage))
            {
                if (isHotkeyBk)
                {
                    errorMessageTime = DateTime.Now;
                    isExceptionMessage = true;
                }
                isBackupRunning = false;
                return;
            }

            errorMessage = string.Empty;
            isExceptionMessage = false;

            Task.Run(() =>
            {
                try
                {
                    errorMessage = string.Empty;

                    filesToCopy = Directory.GetFiles(selectedBkFolder, "*", SearchOption.AllDirectories);
                    currentFileIndex = 0;
                    isBackupRunning = true;
                    progress = 0f;

                    long totalBytesCopied = 0;
                    long maxBytesLimit = (long)(limitGb * 1024 * 1024 * 1024);

                    int totalFiles = filesToCopy.Length;

                    string backupBaseFolder = selectedDestFolder;

                    if (useSubFolderBk)
                    {
                        string finalBkName = string.IsNullOrWhiteSpace(subFolderName) ? "Backup" : subFolderName;

                        if (autoIncrementSubfolder)
                        {

                            string testPath;

                            if (!Path.Exists(Path.Combine(selectedDestFolder, $"{finalBkName} {count}")))
                            {
                                count = 1; // Reset count if the folder does not exist
                            }

                            do
                            {
                                testPath = Path.Combine(selectedDestFolder, count == 1 ? finalBkName : $"{finalBkName} {count}");
                                count++;
                                SaveBkFolderCount(count);
                            }
                            while (Directory.Exists(testPath));

                            backupBaseFolder = testPath;
                        }
                        else
                        {
                            backupBaseFolder = Path.Combine(selectedDestFolder, finalBkName);
                        }
                    }

                    while (currentFileIndex < totalFiles && !cts.Token.IsCancellationRequested)
                    {

                        string fileBk = filesToCopy[currentFileIndex];
                        string relPath = Path.GetRelativePath(selectedBkFolder, fileBk);
                        string destPath = Path.Combine(backupBaseFolder, relPath);

                        FileInfo fileInfo = new FileInfo(fileBk);
                        long fileSize = fileInfo.Length;

                        if (totalBytesCopied + fileSize > maxBytesLimit) // Check if the total size exceeds the limit
                            break;

                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                        try 
                        {
                            File.Copy(fileBk, destPath, true);

                            totalBytesCopied += fileSize;
                        }
                        catch (Exception)
                        {
                            backupError = true;
                            backupErrorTime = DateTime.Now;

                            errorMessage = "Backup could not be completed. Please try again.";
                            errorMessageTime = DateTime.Now;

                            isExceptionMessage = true;
                        }

                        currentFileIndex++;

                        progress = (float)currentFileIndex / totalFiles;

                    }
                    isBackupRunning = false;

                    if (!backupError)
                    {
                        waitingToShowDoneIcon = true;
                        backupDoneTime = DateTime.Now;
                    }

                }
                catch (OperationCanceledException)
                {
                    isBackupRunning = false;
                }
                catch (Exception)
                {
                    backupError = true;
                    backupErrorTime = DateTime.Now;
                    isBackupRunning = false;

                    if (isHotkeyBk)
                    {
                        errorMessage = "Backup could not be completed. Please try again.";
                        errorMessageTime = DateTime.Now;
                        isExceptionMessage = true;
                    }
                }
            }, cts.Token);
        }

        void BackupIcon()
        {
            if (showWindow) return;

            Vector2 iconSize = new Vector2(16, 16);
            Vector2 iconPos = new Vector2(5, 5);
            Vector2 center = iconPos + iconSize / 2;
            float radius = 10f;
            var drawList = ImGui.GetBackgroundDrawList();

            // Backup Error - Red Circle
            if (backupError && (DateTime.Now - backupErrorTime).TotalSeconds < 3)
            {
                Vector4 red = new Vector4(1f, 0f, 0f, 1f);
                uint col = ImGui.ColorConvertFloat4ToU32(red);
                drawList.AddCircleFilled(center, radius, col);
                return;
            }
            else
            {
                backupError = false;
            }


            // Backup completed - Green Circle
            if (!isBackupRunning)
            {
                if (waitingToShowDoneIcon && (DateTime.Now - backupDoneTime).TotalSeconds < 2)
                {
                    Vector4 green = new Vector4(0f, 1f, 0f, 1f);
                    uint col = ImGui.ColorConvertFloat4ToU32(green);
                    drawList.AddCircleFilled(center, radius, col);
                }
                else
                {
                    waitingToShowDoneIcon = false;
                }

                return;
            }

            // Backup in progress - Orange Circle
            double time = DateTime.Now.TimeOfDay.TotalSeconds;
            float pulse = (float)((Math.Sin(time * 6) + 1) / 2);

            Vector4 orange = new Vector4(1f, 0.5f, 0f, 0.5f + 0.5f * pulse);
            uint colPulse = ImGui.ColorConvertFloat4ToU32(orange);

            drawList.AddCircleFilled(center, radius, colPulse);
        }
        void FoldersConfig(string bkFolder, string destFolder)
        {
            var config = LoadConfig();

            if (!string.IsNullOrWhiteSpace(bkFolder) && !config.BackupFolders.Contains(bkFolder))
                config.BackupFolders.Add(bkFolder);

            if (!string.IsNullOrWhiteSpace(destFolder) && !config.DestFolders.Contains(destFolder))
                config.DestFolders.Add(destFolder);

            SaveConfig(config);
        }

        void SaveConfig(FullConfig config)
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChiralSync Backup");
            Directory.CreateDirectory(appDataPath);

            string configFile = Path.Combine(appDataPath, "foldersConfig.json");

            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configFile, json);
        }

        void SaveLastPaths(string bkFolder, string destFolder)
        {
            UpdateConfig(config => {
                if (!string.IsNullOrWhiteSpace(bkFolder))
                    config.LastBackupFolder = bkFolder;
                if (!string.IsNullOrWhiteSpace(destFolder))
                    config.LastDestFolder = destFolder;
            });
        }

        void SaveBkFolderCount(int count)
        {
            UpdateConfig(config => config.BkFolderCount = count);
        }

        void SaveBkConfigs()
        {
            UpdateConfig(config => {
                config.UseSubFolderBk = useSubFolderBk;
                config.AutoIncrementSubFolder = autoIncrementSubfolder;
                config.SubFolderName = subFolderName;
            });
        }

        void SaveScale(float scale)
        {
            UpdateConfig(config => config.WindowScale = scale);
        }

        void SaveLimits(int limit)
        {
            UpdateConfig(config => config.LimitIndex= limit);
        }

        (List<string> backupFolders, List<string> destFolders, string lastBk, string lastDest, int BkCount, bool useSubFolderBk, bool autoIncrementSubFolder, string subFolderName, float windowScale, int limitsGB, HotkeyConfig hotkey, HotkeyConfig hotkeyBk) LoadFoldersConfig()
        {
            var config = LoadConfig();

            return (
                config.BackupFolders ?? [],
                config.DestFolders ?? [],
                config.LastBackupFolder ?? string.Empty,
                config.LastDestFolder ?? string.Empty,
                config.BkFolderCount,
                config.UseSubFolderBk,
                config.AutoIncrementSubFolder,
                config.SubFolderName ?? "Backup",
                config.WindowScale,
                config.LimitIndex,
                config.Hotkey ?? new(),
                config.HotkeyBK ?? new()
            );
        }

        void RemovePath(string path, bool isBk)
        {
            UpdateConfig(config => {
                if (isBk)
                    config.BackupFolders.Remove(path);
                else
                    config.DestFolders.Remove(path);
            });
        }

        void SaveHotKey(Keys mainKey, bool ctrl, bool alt, bool shift, bool isOverlay = false)
        {
            UpdateConfig(config => {
                var newHotkey = new HotkeyConfig
                {
                    Main = (int)mainKey,
                    Ctrl = ctrl,
                    Alt = alt,
                    Shift = shift
                };
                if (isOverlay)
                    config.Hotkey = newHotkey;
                else
                    config.HotkeyBK = newHotkey;
            });
        }


        void LoadHotkeyFromConfig()
        {
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ChiralSync Backup");
            string configFile = Path.Combine(appDataPath, "foldersConfig.json");

            if (!File.Exists(configFile))
                return;

            try
            {
                string json = File.ReadAllText(configFile);
                var config = JsonSerializer.Deserialize<FullConfig>(json);

                if (config?.Hotkey != null && config.Hotkey.Main != 0)
                {
                    hotkeyMain = (Keys)config.Hotkey.Main;
                    hotkeyCtrl = config.Hotkey.Ctrl;
                    hotkeyAlt = config.Hotkey.Alt;
                    hotkeyShift = config.Hotkey.Shift;
                }

                if (config?.HotkeyBK != null && config.HotkeyBK.Main != 0)
                {
                    hotkeyMainBK = (Keys)config.HotkeyBK.Main;
                    hotkeyCtrlBK = config.HotkeyBK.Ctrl;
                    hotkeyAltBK = config.HotkeyBK.Alt;
                    hotkeyShiftBK = config.HotkeyBK.Shift;
                }
            }
            catch (Exception ex) { Console.WriteLine($"[Config Load Error]: {ex.Message}"); }
        }

        class HotkeyConfig
        {
            public int Main { get; set; }
            public bool Ctrl { get; set; }
            public bool Alt { get; set; }
            public bool Shift { get; set; }
        }
        string FormatHotkey(bool ctrl, bool alt, bool shift, Keys key)
        {
            string combo = "";
            if (ctrl) combo = "Ctrl + ";
            if (alt) combo = "Alt + ";
            if (shift) combo = "Shift + ";

            string keyName = key.ToString();

            if (key >= Keys.D0 && key <= Keys.D9)
            {
                keyName = keyName.Replace("D", ""); // Remove 'D' prefix for number keys
            }

            combo += keyName;
            return combo;
        }
        (bool ctrl, bool alt, bool shift) GetModifiers()
        {
            return (
                (GetAsyncKeyState((int)Keys.ControlKey) & 0x8000) != 0,
                (GetAsyncKeyState((int)Keys.Menu) & 0x8000) != 0,
                (GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0
            );
        }
        void ShowHelper(string desc)
        {
            ImGui.SameLine();
            ImGui.TextColored(new System.Numerics.Vector4(0.5f, 0.5f, 0.5f, 1f), ("(?)"));

            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 35.0f);
                ImGui.TextUnformatted(desc);
                ImGui.PopTextWrapPos();
                ImGui.EndTooltip();
            }
        }
        void DrawAlgnBtn(string lbl, Vector2 size, float algnHeight, Action OnClick)
        {
            float posY = ImGui.GetCursorPosY();

            ImGui.SetCursorPosY(posY + (algnHeight - size.Y) / 2);

            if (ImGui.Button(lbl, size))
                OnClick?.Invoke();
        }

        void DrawFolderSelector(string label, ref string folderPath, string clearBtnId, string? errorMessage = null, bool bkLastPath = false, bool openFolder = false)
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                ImGui.Text($"{label}:");

                ImGui.SameLine();
            }

            Vector2 boxSize = new Vector2(400, ImGui.GetTextLineHeightWithSpacing());

            bool isError = !string.IsNullOrWhiteSpace(errorMessage);
            var bgErrorColor = isError ? new Vector4(0.3f, 0.1f, 0.1f, 1.0f) : new Vector4(0.18f, 0.18f, 0.18f, 1.0f);
            var borderErrorColor = isError ? new Vector4(1f, 0f, 0f, 1f) : new Vector4(0.4f, 0.4f, 0.4f, 1.0f);

            ImGui.PushStyleColor(ImGuiCol.FrameBg, bgErrorColor);
            ImGui.PushStyleColor(ImGuiCol.Border, borderErrorColor);

            ImGui.InputText($"##input{clearBtnId}", ref folderPath, 512,
                            ImGuiInputTextFlags.ReadOnly);

            ImGui.PopStyleColor(2);


            if (!string.IsNullOrEmpty(folderPath))
            {
                ImGui.SameLine();

                if (ImGui.Button($"X##{clearBtnId}"))
                    folderPath = "";

                if (openFolder)
                {
                    ImGui.SameLine();

                    if (ImGui.Button($"->##open_{clearBtnId}"))
                    {
                        if (Directory.Exists(folderPath))
                            System.Diagnostics.Process.Start("explorer.exe", folderPath);
                    }

                    ShowHelper("Open destination folder");
                }

                if (bkLastPath)
                    SaveLastPaths(selectedBkFolder, selectedDestFolder);
            }

            if (isError)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0f, 0f, 1f));
                ImGui.Text(errorMessage);
                ImGui.PopStyleColor();
            }
        }
        void ShowFolderDialog(bool isBk)
        {
            var thread = new Thread(() =>
            {
                using var dialog = new FolderBrowserDialog();
                dialog.Description = isBk ? "Select the folder to backup" : "Select the destination folder to save the backup";


                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    if (isBk)
                        selectedBkFolder = dialog.SelectedPath;
                    else
                        selectedDestFolder = dialog.SelectedPath;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }

        // Utility to check if a key is down
        bool IsKeyDown(Keys key) => (GetAsyncKeyState((int)key) & 0x8000) != 0;

        // Validates the selected paths
        bool ArePathsValid(out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(selectedBkFolder) || string.IsNullOrWhiteSpace(selectedDestFolder))
            {
                error = "Please select valid backup and destination folders.";
                return false;
            }

            if (!Path.Exists(selectedBkFolder) || !Path.Exists(selectedDestFolder))
            {
                error = "The selected path does not exist. Please select a valid path.";
                return false;
            }

            if (Path.GetFullPath(selectedBkFolder) == Path.GetFullPath(selectedDestFolder))
            {
                error = "Backup and destination folders cannot be the same.";
                return false;
            }

            return true;
        }

        // Updates configuration in a consistent way
        void UpdateConfig(Action<FullConfig> update)
        {
            var config = LoadConfig();
            update(config);
            SaveConfig(config);
        }

        public static class OverlayIconHelper
        {
            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            static extern IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

            public static void SetIcon(IntPtr hwnd, string resourceName = "ChiralSync.Icon.neural.ico")
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null) return;

                using var icon = new Icon(stream);

                SendMessage(hwnd, 0x80, (IntPtr)0, icon.Handle);  // WM_SETICON, ICON_SMALL
                SendMessage(hwnd, 0x80, (IntPtr)1, icon.Handle);  // WM_SETICON, ICON_BIG
                SetClassLongPtr(hwnd, -14, icon.Handle);          // GCL_HICON
                SetClassLongPtr(hwnd, -34, icon.Handle);          // GCL_HICONSM
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            var program = new Program();

            program.StartThreads();

            program.Start().Wait();

            OverlayIconHelper.SetIcon(program.window.Handle);

            program.Run().Wait();
        }

    }
}
 
