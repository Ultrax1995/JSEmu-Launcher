using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    public partial class UpdateWindow : Window
    {
        public static UpdateWindow updateInstance;
        public static string installerDownloadURL;
        public static string installerFileName;
        public static Version expectedVersion;

        public UpdateWindow()
        {
            InitializeComponent();
            updateInstance = this;

            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(SetLanguageFile.LoadFile());
        }

        private async void UpdateWindowLoaded(object sender, RoutedEventArgs e)
        {
            SystemSounds.Beep.Play();
            await UpdateLauncher();
        }

        private static bool CanWriteToTargetDirectory(string targetExe)
        {
            try
            {
                string directory = Path.GetDirectoryName(targetExe);

                if (string.IsNullOrWhiteSpace(directory))
                    return false;

                string testFile = Path.Combine(
                    directory,
                    $".jsemu-update-test-{Guid.NewGuid():N}.tmp"
                );

                using FileStream fs = new(
                    testFile,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    1,
                    FileOptions.DeleteOnClose
                );

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task UpdateLauncher()
        {
            string updatesDirectory = Path.Combine(
                Info.APPLICATION_DATA_PATH,
                "JSEmu Launcher",
                "Updates"
            );

            string downloadedLauncher = Path.Combine(
                updatesDirectory,
                "JSEmu Launcher.update.exe"
            );

            try
            {
                Directory.CreateDirectory(updatesDirectory);

                downloadSetupProgress.IsIndeterminate = true;
                taskbarIcon.ProgressState =
                    System.Windows.Shell.TaskbarItemProgressState.Indeterminate;

                if (File.Exists(downloadedLauncher))
                    File.Delete(downloadedLauncher);

                using HttpResponseMessage response =
                    await SplashWindow.httpClient.GetAsync(
                        installerDownloadURL,
                        HttpCompletionOption.ResponseHeadersRead
                    );

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    string reason = response.ReasonPhrase ?? response.StatusCode.ToString();
                    throw new Exception(reason);
                }

                downloadSetupProgress.IsIndeterminate = false;
                taskbarIcon.ProgressState =
                    System.Windows.Shell.TaskbarItemProgressState.Normal;

                long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                await using Stream contentStream =
                    await response.Content.ReadAsStreamAsync();

                await using FileStream fileStream = new(
                    downloadedLauncher,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    8192,
                    true
                );

                byte[] buffer = new byte[8192];
                long totalBytesRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer)) != 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                    totalBytesRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        float progressPercentage =
                            (float)totalBytesRead * 100 / totalBytes;

                        downloadSetupProgress.Value = progressPercentage;
                        downloadSetupProgressText.Text =
                            $"{FindResource("item54")} {progressPercentage:0.00}%";
                        taskbarIcon.ProgressValue = progressPercentage / 100;
                    }
                }

                await fileStream.FlushAsync();

                if (totalBytesRead < 1024)
                    throw new InvalidDataException("Downloaded launcher file is unexpectedly small.");

                // Basic PE executable sanity check.
                await using (FileStream verifyStream = new(
                    downloadedLauncher,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read
                ))
                {
                    if (verifyStream.ReadByte() != 0x4D || verifyStream.ReadByte() != 0x5A)
                        throw new InvalidDataException("Downloaded file is not a valid Windows executable.");
                }

                FileVersionInfo downloadedInfo =
                    FileVersionInfo.GetVersionInfo(downloadedLauncher);

                Version downloadedVersion =
                    SplashWindow.NormalizeVersion(downloadedInfo.FileVersion);

                if (expectedVersion != null && downloadedVersion != expectedVersion)
                {
                    throw new InvalidDataException(
                        $"Downloaded launcher version {downloadedVersion} does not match expected version {expectedVersion}."
                    );
                }

                downloadSetupProgress.IsIndeterminate = true;
                taskbarIcon.ProgressState =
                    System.Windows.Shell.TaskbarItemProgressState.None;
            }
            catch (AggregateException e)
            {
                downloadSetupProgress.IsIndeterminate = true;
                taskbarIcon.ProgressState =
                    System.Windows.Shell.TaskbarItemProgressState.None;

                string exceptionList = string.Empty;

                foreach (Exception exception in e.InnerExceptions)
                    exceptionList += $"\n\n{exception.GetType().Name}: {exception.Message}";

                if (e.InnerException is HttpRequestException ex && ex.StatusCode == null)
                    exceptionList += $"\n\n{FindResource("item137")}";

                CustomMessageBox.Show(
                    $"{FindResource("item80").ToString().Replace(":", ".").Replace("：", ".")} {FindResource("item16")}{exceptionList}",
                    this
                );

                return;
            }
            catch (Exception ex)
            {
                downloadSetupProgress.IsIndeterminate = true;
                taskbarIcon.ProgressState =
                    System.Windows.Shell.TaskbarItemProgressState.None;

                CustomMessageBox.Show(
                    $"{FindResource("item80")} \"{ex.Message}\".",
                    this
                );

                return;
            }

            try
            {
                string currentExe =
                    Environment.ProcessPath ??
                    Process.GetCurrentProcess().MainModule?.FileName;

                if (string.IsNullOrWhiteSpace(currentExe))
                    throw new Exception("Unable to determine the current launcher path.");

                ProcessStartInfo updaterStart = new()
                {
                    FileName = downloadedLauncher,
                    UseShellExecute = true
                };

                updaterStart.ArgumentList.Add("--apply-update");
                updaterStart.ArgumentList.Add(Environment.ProcessId.ToString());
                updaterStart.ArgumentList.Add(currentExe);

                // If the launcher is installed in a protected folder such as
                // Program Files, start the temporary updater as administrator.
                if (!CanWriteToTargetDirectory(currentExe))
                    updaterStart.Verb = "runas";

                Process.Start(updaterStart);
            }
            catch (Exception ph)
            {
                CustomMessageBox.Show(
                    $"{FindResource("item186")} \"{ph.Message}\"\n\n{FindResource("item187")}",
                    this
                );

                return;
            }

            Environment.Exit(0);
        }

        private void MoveUpdateWindow(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseUpdateWindow(object sender, RoutedEventArgs e)
        {
            Topmost = true;
            Environment.Exit(0);
        }

        private void UpdateWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            updateInstance = null;
        }
    }
}
