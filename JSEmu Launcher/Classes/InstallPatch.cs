using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace H1Emu_Launcher.Classes
{
    class InstallPatchClass
    {
        public static async Task<bool> InstallPatch()
        {
            try
            {
                LauncherWindow.launcherInstance.playButton.SetResourceReference(Button.ContentProperty, "item188");
                LauncherWindow.launcherInstance.taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Indeterminate;

                if (Properties.Settings.Default.gameVersionString == "22dec2016")
                {
                    // Extract FairPlay logo
                    Bitmap fairPlayLogo = new Bitmap(Properties.Resources.logo);
                    fairPlayLogo.Save($"{Properties.Settings.Default.activeDirectory}\\logo.bmp", ImageFormat.Bmp);

                    // Get the download URL for the selected asset pack
                    string assetPackJsonURL = string.Empty;

                    int selectedServerIndex = LauncherWindow.launcherInstance.serverSelector.SelectedIndex;
                    int selectedAssetPackIndex = Properties.Settings.Default.selectedAssetPack;

                    /*
                     * Auto asset pack logic:
                     *
                     * Server selector:
                     * 0 = JSEmu Servers       -> JSEmu asset pack
                     * 1 = H1Emu Servers       -> H1Emu asset pack
                     * 2 = Singleplayer        -> selected/manual asset pack fallback
                     * 3 = Separator
                     * 4 = New Server...
                     * 5+ = Custom servers     -> selected/manual asset pack fallback
                     *
                     * Asset pack selector:
                     * 0 = JSEmu.eu - Assets Pack
                     * 1 = H1Emu.com - Assets Pack
                     * 2 = Separator/New Asset Pack area
                     * 3+ = Custom asset packs from assetPacks.json
                     */

                    if (selectedServerIndex == 0)
                    {
                        // JSEmu Servers always use JSEmu assets
                        assetPackJsonURL = Info.OFFICIAL_ASSET_PACK;
                        Properties.Settings.Default.selectedAssetPack = 0;
                    }
                    else if (selectedServerIndex == 1)
                    {
                        // H1Emu Servers always use official H1Emu assets
                        assetPackJsonURL = Info.H1EMU_ASSET_PACK;
                        Properties.Settings.Default.selectedAssetPack = 1;
                    }
                    else
                    {
                        // Custom servers / singleplayer use the manually selected asset pack
                        if (selectedAssetPackIndex == 0)
                        {
                            assetPackJsonURL = Info.OFFICIAL_ASSET_PACK;
                        }
                        else if (selectedAssetPackIndex == 1)
                        {
                            assetPackJsonURL = Info.H1EMU_ASSET_PACK;
                        }
                        else if (selectedAssetPackIndex >= 3)
                        {
                            List<LauncherWindow.AssetPackList> assetPackJson =
                                JsonSerializer.Deserialize<List<LauncherWindow.AssetPackList>>(
                                    File.ReadAllText(LauncherWindow.assetPacksJsonFile)
                                );

                            assetPackJsonURL = assetPackJson[selectedAssetPackIndex - 3].AssetPackURL;
                        }
                        else
                        {
                            // Safety fallback if separator/new item somehow gets selected
                            assetPackJsonURL = Info.OFFICIAL_ASSET_PACK;
                            Properties.Settings.Default.selectedAssetPack = 0;
                        }
                    }

                    Properties.Settings.Default.Save();

                    await DownloadAssetPack(assetPackJsonURL, (filename, percentage) =>
                    {
                        LauncherWindow.launcherInstance.playButton.FontSize = 18;
                        LauncherWindow.launcherInstance.taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Normal;
                        LauncherWindow.launcherInstance.playButton.Content = LauncherWindow.launcherInstance.FindResource("item188") + $" {percentage:0.00}%";
                        LauncherWindow.launcherInstance.taskbarIcon.ProgressValue = percentage / 100;
                    });

                    LauncherWindow.launcherInstance.playButton.FontSize = 28;
                    LauncherWindow.launcherInstance.playButton.SetResourceReference(Button.ContentProperty, "item188");
                    LauncherWindow.launcherInstance.taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.Indeterminate;
                }

                // Delete BattlEye folder to prevent Steam from trying to launch the game
                if (Directory.Exists($"{Properties.Settings.Default.activeDirectory}\\BattlEye"))
                    Directory.Delete($"{Properties.Settings.Default.activeDirectory}\\BattlEye", true);

                // Replace users ClientConfig.ini with modified version
                File.WriteAllBytes($"{Properties.Settings.Default.activeDirectory}\\ClientConfig.ini", Properties.Resources.CustomClientConfig);

                // Delete any no longer needed files/old patches
                if (Directory.Exists($"{Properties.Settings.Default.activeDirectory}\\H1EmuVoice"))
                    Directory.Delete($"{Properties.Settings.Default.activeDirectory}\\H1EmuVoice", true);
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\Game_Patch_2016.zip");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\Resources\\Audio\\pc9\\SoundBanks\\Sound_Banks.zip");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\Locale\\Locales.zip");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\H1EmuVoiceClient.dll");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\H1EmuVoiceClient.runtimeconfig.json");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\NAudio.Asio.dll");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\NAudio.Core.dll");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\NAudio.dll");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\NAudio.Midi.dll");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\NAudio.Wasapi.dll");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\NAudio.WinMM.dll");
                File.Delete($"{Properties.Settings.Default.activeDirectory}\\websocket-sharp.dll");
            }
            catch (Exception e)
            {
                if (LauncherWindow.launcherInstance.serverSelector.SelectedIndex != 2)
                {
                    LauncherWindow.launcherInstance.playButton.IsEnabled = true;
                    LauncherWindow.launcherInstance.playButton.FontSize = 28;
                    LauncherWindow.launcherInstance.playButton.SetResourceReference(Button.ContentProperty, "item8");
                    LauncherWindow.launcherInstance.taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
                }
                
                if (Properties.Settings.Default.gameVersionString == "22dec2016")
                    CustomMessageBox.Show($"{LauncherWindow.launcherInstance.FindResource("item96")}\n\n{e.Message}", LauncherWindow.launcherInstance);
                return false;
            }

            LauncherWindow.launcherInstance.playButton.SetResourceReference(Button.ContentProperty, "item217");
            LauncherWindow.launcherInstance.taskbarIcon.ProgressState = System.Windows.Shell.TaskbarItemProgressState.None;
            return true;
        }

        /// <summary>
        /// Downloads and installs every file listed in an asset pack manifest. Each file is
        /// checked against the SHA-256 from the manifest both before and after downloading,
        /// so a corrupted or tampered file is never installed.
        /// </summary>
        /// <param name="assetPackJsonURL">URL of the asset pack manifest.</param>
        /// <param name="onProgress">Called with the file being downloaded and its percentage.</param>
        public static async Task DownloadAssetPack(string assetPackJsonURL, Action<string, double> onProgress)
        {
            // Query the asset pack JSON URL
            HttpResponseMessage response = await SplashWindow.httpClient.GetAsync(assetPackJsonURL, HttpCompletionOption.ResponseHeadersRead);

            // Throw an exception if we didn't get the correct response, with the first letter in the message capitalised
            if (response.StatusCode != HttpStatusCode.OK)
                throw new Exception($"{char.ToUpper(response.ReasonPhrase.First())}{response.ReasonPhrase.Substring(1)}");

            // Deserialise the asset pack JSON into an object
            string jsonAssetPack = await response.Content.ReadAsStringAsync();
            JsonEndPoints.AssetPackJson.Root jsonAssetPackDes = JsonSerializer.Deserialize<JsonEndPoints.AssetPackJson.Root>(jsonAssetPack);

            List<string> verifiedAssets = [];
            for (int i = 0; i <= 255; i++)
                verifiedAssets.Add($"Assets_{i:D3}.pack");

            string gameDirectory = Path.GetFullPath(Properties.Settings.Default.activeDirectory);
            string defaultAssetsDirectory = Path.Combine(gameDirectory, "Resources", "Assets");

            // Archives are cached outside of the game folder so their hash can still be
            // verified on later launches, after they have been extracted
            string assetCacheDirectory = $"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\AssetCache";
            Directory.CreateDirectory(assetCacheDirectory);

            // For each asset in the JSON, download the asset file
            foreach (JsonEndPoints.AssetPackJson.Asset item in jsonAssetPackDes.assets)
            {
                string expectedHash = item.hash?.Replace("sha256:", "").Trim() ?? string.Empty;

                // Never install a file that the manifest cannot vouch for
                if (string.IsNullOrWhiteSpace(expectedHash))
                    throw new Exception($"Asset \"{item.filename}\" has no hash in the asset pack manifest.");

                // Assets without an explicit path keep the original behaviour and land in Resources\Assets
                bool usesDefaultDirectory = string.IsNullOrWhiteSpace(item.path);
                string targetDirectory = usesDefaultDirectory
                    ? defaultAssetsDirectory
                    : Path.GetFullPath(Path.Combine(gameDirectory, item.path));

                string targetFile = Path.GetFullPath(Path.Combine(targetDirectory, item.filename));

                // A remote manifest must never be able to write outside of the game folder
                if (!targetFile.StartsWith(gameDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new Exception($"Asset \"{item.filename}\" points outside of the game directory.");

                Directory.CreateDirectory(targetDirectory);

                // Archives are hashed as their cached copy, plain files as the installed one
                string downloadedFile = item.extract
                    ? Path.Combine(assetCacheDirectory, item.filename)
                    : targetFile;

                if (!FileMatchesHash(downloadedFile, expectedHash))
                {
                    HttpResponseMessage responseDownloadURL = await SplashWindow.httpClient.GetAsync(item.url, HttpCompletionOption.ResponseHeadersRead);

                    // Throw an exception if we didn't get the correct response, with the first letter in the message capitalised
                    if (responseDownloadURL.StatusCode != HttpStatusCode.OK)
                        throw new Exception($"{char.ToUpper(responseDownloadURL.ReasonPhrase.First())}{responseDownloadURL.ReasonPhrase.Substring(1)}");

                    long totalBytes = responseDownloadURL.Content.Headers.ContentLength ?? -1L;

                    using (Stream contentStream = await responseDownloadURL.Content.ReadAsStreamAsync())
                    using (FileStream fileStream = new(downloadedFile, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[8192];
                        long totalBytesRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer)) != 0)
                        {
                            // Write the data to the file
                            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                            totalBytesRead += bytesRead;

                            // Report progress back to whichever window started the download
                            if (totalBytes > 0)
                                onProgress?.Invoke(item.filename, (double)totalBytesRead * 100 / totalBytes);
                        }
                    }

                    // Check what actually arrived before it is installed or extracted
                    if (!FileMatchesHash(downloadedFile, expectedHash))
                    {
                        File.Delete(downloadedFile);
                        throw new Exception($"Downloaded file \"{item.filename}\" does not match the hash in the asset pack manifest.");
                    }
                }

                // Archives are unpacked on every run so removed game files are restored
                if (item.extract)
                    ZipFile.ExtractToDirectory(downloadedFile, targetDirectory, true);

                if (usesDefaultDirectory)
                    verifiedAssets.Add(item.filename);
            }

            // Make sure that only the default game assets and the newly installed asset pack is the only thing in the "Assets" folder
            foreach (string file in Directory.GetFiles(defaultAssetsDirectory))
            {
                string fileName = Path.GetFileName(file);
                if (!verifiedAssets.Contains(fileName))
                    File.Delete(file);
            }
        }

        // Returns true when the file exists and its SHA-256 matches the expected hex digest
        private static bool FileMatchesHash(string filePath, string expectedHash)
        {
            if (!File.Exists(filePath))
                return false;

            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(filePath);

            return Convert.ToHexString(sha256.ComputeHash(stream))
                .Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        }
    }
}