using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using H1Emu_Launcher.Classes;

namespace H1Emu_Launcher
{
    public partial class App
    {
        private void ApplicationStartup(object sender, StartupEventArgs e)
        {
            // The newly downloaded launcher starts itself in this special mode.
            // It waits for the old launcher to close, replaces it, and starts
            // the updated launcher from the original path.
            if (TryApplySelfUpdate(e.Args))
            {
                Shutdown();
                return;
            }

            Resources.MergedDictionaries.Clear();
            Resources.MergedDictionaries.Add(SetLanguageFile.LoadFile());

            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            if (Process.GetProcessesByName(Process.GetCurrentProcess().ProcessName).Length > 1)
            {
                SendArgumentsToRunningInstance(
                    string.Join(' ', e.Args)
                        .Replace("h1emulauncher://", "")
                        .Replace("/\"", "")
                        .Replace("%20", " ")
                        .Split(' ')
                );

                Environment.Exit(0);
            }

            if (string.Join(' ', e.Args).Contains("-skipupdatecheck"))
                SplashWindow.checkForUpdates = false;

            if (e.Args.Length > 0)
                LauncherWindow.rawArgs = e.Args;

            StartPipeServer();

            Timeline.DesiredFrameRateProperty.OverrideMetadata(
                typeof(Timeline),
                new FrameworkPropertyMetadata { DefaultValue = 60 }
            );

            if (Directory.Exists($"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\CarouselImages"))
                Directory.Delete($"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\CarouselImages", true);

            File.Delete($"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher\\args.txt");

            // Clean a stale updater from an older successful update if possible.
            try
            {
                string staleUpdater = Path.Combine(
                    Info.APPLICATION_DATA_PATH,
                    "JSEmu Launcher",
                    "Updates",
                    "JSEmu Launcher.update.exe"
                );

                if (File.Exists(staleUpdater))
                    File.Delete(staleUpdater);
            }
            catch
            {
                // It may still be exiting after replacing the launcher.
                // The same path will be overwritten during the next update.
            }

            SplashWindow sp = new();
            sp.Show();
        }

        private static bool TryApplySelfUpdate(string[] args)
        {
            if (args.Length < 1 ||
                !string.Equals(args[0], "--apply-update", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            try
            {
                if (args.Length < 3)
                    throw new ArgumentException("Missing self-update arguments.");

                if (!int.TryParse(args[1], out int oldProcessId))
                    throw new ArgumentException("Invalid old launcher process ID.");

                string targetExe = Path.GetFullPath(args[2]);

                string updaterExe =
                    Environment.ProcessPath ??
                    Process.GetCurrentProcess().MainModule?.FileName;

                if (string.IsNullOrWhiteSpace(updaterExe))
                    throw new Exception("Unable to determine updater executable path.");

                try
                {
                    Process oldProcess = Process.GetProcessById(oldProcessId);

                    if (!oldProcess.HasExited)
                        oldProcess.WaitForExit(60000);
                }
                catch (ArgumentException)
                {
                    // Old process already exited.
                }

                Exception lastCopyError = null;

                for (int attempt = 0; attempt < 30; attempt++)
                {
                    try
                    {
                        File.Copy(updaterExe, targetExe, true);
                        lastCopyError = null;
                        break;
                    }
                    catch (IOException ex)
                    {
                        lastCopyError = ex;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        lastCopyError = ex;
                    }

                    Thread.Sleep(500);
                }

                if (lastCopyError != null)
                    throw lastCopyError;

                ProcessStartInfo launcherStart = new()
                {
                    FileName = targetExe,
                    UseShellExecute = true
                };

                // The file was just updated, so skip exactly one immediate
                // GitHub check. The next normal start checks again.
                launcherStart.ArgumentList.Add("-skipupdatecheck");

                Process.Start(launcherStart);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"JSEmu Launcher could not finish the automatic update.\n\n{ex.Message}",
                    "JSEmu Launcher Update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }

            return true;
        }

        private void StartPipeServer()
        {
            new Thread(() =>
            {
                while (true)
                {
                    NamedPipeServerStream pipeServer =
                        new("H1EmuLauncherPipe", PipeDirection.In);

                    pipeServer.WaitForConnection();

                    StreamReader reader = new(pipeServer);
                    string args = reader.ReadToEnd();

                    Dispatcher.Invoke(new Action(async delegate
                    {
                        await LauncherWindow.launcherInstance.ExecuteArguments(args.Split(' '));
                    }));

                    pipeServer.Dispose();
                }
            }).Start();
        }

        private void SendArgumentsToRunningInstance(string[] args)
        {
            try
            {
                NamedPipeClientStream pipeClient =
                    new(".", "H1EmuLauncherPipe", PipeDirection.Out);

                StreamWriter writer = new(pipeClient);

                pipeClient.Connect(1000);
                writer.Write(string.Join(' ', args));
                writer.Flush();
            }
            catch (Exception e)
            {
                CustomMessageBox.Show(
                    $"Error sending launch arguments to active instance: \"{e.Message}\".",
                    LauncherWindow.launcherInstance
                );
            }
        }

        private void TextBoxContextMenuPasteClick(object sender, RoutedEventArgs e)
        {
            try
            {
                MenuItem menuItem = (MenuItem)sender;
                ContextMenu contextMenu = (ContextMenu)menuItem.Parent;
                UIElement box = contextMenu.PlacementTarget;
                ApplicationCommands.Paste.Execute(null, box);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show($"{FindResource("item220")} \"{ex.Message}\".");
            }
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            {
                if (LauncherWindow.launcherInstance == null)
                {
                    MessageBoxResult mbr = CustomMessageBox.Show(
                        $"An exception occurred that prevented the application from starting: \"{(e.ExceptionObject as Exception).Message}\".\n\nDeleting the application data can sometimes fix this, would you like to try that and attempt to restart now?",
                        null,
                        false,
                        true,
                        true
                    );

                    if (mbr == MessageBoxResult.Yes)
                    {
                        DirectoryInfo di =
                            new($"{Info.APPLICATION_DATA_PATH}\\JSEmu Launcher");

                        foreach (var file in di.GetFiles())
                            file.Delete();

                        System.Windows.Forms.Application.Restart();
                    }
                }

                CustomMessageBox.Show(
                    $"An unhandled exception occurred: \"{(e.ExceptionObject as Exception).Message}\".\n\nThe launcher will now close.",
                    LauncherWindow.launcherInstance
                );

                Environment.Exit(1);
            }
        }
    }
}
