#region "copyright"
/*
    Copyright © 2016 - 2022 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors 

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/
#endregion "copyright"
using NINA.Astrometry;
using NINA.Core.Locale;
using NINA.Core.MyMessageBox;
using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using NINA.Utility;
using NINA.View;
using NINA.ViewModel;
using NINA.ViewModel.Interfaces;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NINA {

    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {
        private ProfileService _profileService;
        private IMainWindowVM _mainWindowViewModel;

        private Exception InitializeUserSettings() {
            Exception userSettingsException = null;
            Configuration config;
            bool backupWasRestored = false;
            string originalFileName = string.Empty;
            string backupFileName = string.Empty;
            try {
                config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal);
                NINA.Properties.Settings.Default.GetPreviousVersion("UpdateSettings");
                try {
                    if (config.HasFile) {
                        var backup = config.FilePath + ".bkp";
                        File.Copy(config.FilePath, backup, true);
                    }
                } catch (Exception) { }
            } catch (ConfigurationErrorsException configException) {
                try {
                    userSettingsException = configException;
                    File.Delete(configException.Filename);

                    var backup = configException.Filename + ".bkp";
                    if (File.Exists(backup)) {
                        File.Copy(backup, configException.Filename, true);
                        backupFileName = backup;
                        originalFileName = configException.Filename;
                        backupWasRestored = true;
                    }
                } catch (Exception configRestoreException) {
                    userSettingsException = configRestoreException;
                }
            }

            try {
                if (NINA.Properties.Settings.Default.UpdateSettings) {
                    NINA.Properties.Settings.Default.Upgrade();
                    NINA.Properties.Settings.Default.UpdateSettings = false;
                    CoreUtil.SaveSettings(NINA.Properties.Settings.Default);
                }
            } catch (ConfigurationErrorsException configException) {
                try {
                    userSettingsException = configException;
                    if(configException?.Filename != null && File.Exists(configException.Filename)) {
                        File.Delete(configException.Filename);
                    }
                    if(backupWasRestored) {
                        // Backup was restored but it still failed to load. Both files must be corrupted
                        if(File.Exists(originalFileName)) {
                            File.Delete(originalFileName);
                        }
                        if(File.Exists(backupFileName)) {
                            File.Delete(backupFileName);
                        }
                        
                    }

                    Application.Current.Shutdown();
                    // App restart is required, as even after deleting the configuration files the configuration manager still tries to use the old values
                    System.Windows.Forms.Application.Restart();
                } catch (Exception configDeleteException) {
                    userSettingsException = configDeleteException;
                }
            }

            return userSettingsException;
        }

        private void StartupActions() {
            _ = new EarthRotationParameterUpdater().Update();
        }

        protected override void OnStartup(StartupEventArgs e) {
            Current.DispatcherUnhandledException += Current_DispatcherUnhandledException;

            var userSettingsException = InitializeUserSettings();

            _profileService =
                //TODO: Eliminate Smell by reversing direction of this dependency
                (ProfileService)Current.Resources["ProfileService"];

            ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(Int32.MaxValue));

            var startWithProfileId = e
               .Args
               .SkipWhile(x => !x.Equals("/profileid", StringComparison.OrdinalIgnoreCase))
               .Skip(1)
               .FirstOrDefault();
            _profileService = (ProfileService)Current.Resources["ProfileService"];

            if (!_profileService.TryLoad(startWithProfileId)) {
                ProfileService.ActivateInstanceOfNinaReferencingProfile(startWithProfileId);
                Shutdown();
                return;
            }
            this.RefreshJumpList(_profileService);

            _profileService.CreateWatcher();

            Logger.SetLogLevel(_profileService.ActiveProfile.ApplicationSettings.LogLevel);

            if (userSettingsException != null) {
                Logger.Error("There was an issue loading the user settings and the application tried to delete the file and reload default settings.", userSettingsException);
            }

            EventManager.RegisterClassHandler(typeof(TextBox),
                TextBox.GotFocusEvent,
                new RoutedEventHandler(TextBox_GotFocus));


            if (!NINA.Properties.Settings.Default.HardwareAcceleration) {
                Logger.Info("Disabling Hardware Acceleration");
                RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            }

            if (_profileService.Profiles.Count > 1 && !NINA.Properties.Settings.Default.UseSavedProfileSelection && !_profileService.ProfileWasSpecifiedFromCommandLineArgs) {
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var profileSelection = new ProfileSelectVM(_profileService);
                var profileSelectionWindow = new ProfileSelectView();
                profileSelectionWindow.DataContext = profileSelection;
                profileSelectionWindow.ShowDialog();
            }

            _mainWindowViewModel = CompositionRoot.Compose(_profileService);
            var mainWindow = new MainWindow();
            this.MainWindow = mainWindow;
            mainWindow.DataContext = _mainWindowViewModel;
            mainWindow.Show();
            Application.Current.ShutdownMode = ShutdownMode.OnMainWindowClose;
            ProfileService.ActivateInstanceWatcher(_profileService, mainWindow);

            StartupActions();
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e) {
            (sender as TextBox).SelectAll();
        }

        protected override void OnExit(ExitEventArgs e) {
            _mainWindowViewModel.DeviceDispatcher?.Dispose();
            this.RefreshJumpList(_profileService);
        }

        private void LogResource(ResourceDictionary res, string indentation) {
            var sb = new StringBuilder();
            foreach (var key in res.Keys) {
                sb.Append(key + ";");
            }
            Logger.Error(indentation + sb.ToString());
            LogResource(res.MergedDictionaries, indentation + "==>");
        }

        private void LogResource(Collection<ResourceDictionary> res, string indentation) {
            foreach (var dic in res) {
                LogResource(dic, indentation + "==>");
            }
        }

        private static object lockObj = new object();

        [SecurityCritical]
        private void Current_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
            lock(lockObj) { 
                Logger.Error($"An unhandled exception has occurred of type {e.Exception.GetType()}");
                if (e.Exception.InnerException != null) {
                    var message = $"{e.Exception.Message}{Environment.NewLine}{e.Exception.StackTrace}{Environment.NewLine}Inner Exception of type {e.Exception.InnerException.GetType()}: {Environment.NewLine}{e.Exception.InnerException}{e.Exception.StackTrace}";
                    Logger.Error(message);
                } else {
                    Logger.Error(e.Exception);
                }

                if (e.Exception is InvalidOperationException) {
                    /* When accessing N.I.N.A. via RDP from a mobile device or a tablet device it seems like WPF has a stylus bug during refresh.
                     * This piece of code tries to auto recover from that issue
                     */
                    if (e.Exception.StackTrace.Contains("System.Windows.Input.StylusWisp.WispLogic.RefreshTablets")) {
                        Logger.Error("The exception is related to the WPF Stylus bug and therefore tries to recover automatically");
                        e.Handled = true;
                        return;
                    }
                }

                if (e.Exception is System.Windows.Markup.XamlParseException xamlEx) {
                    Logger.Error($"Faulting xaml module: {xamlEx.BaseUri}");
                    if (Application.Current?.Resources != null) {
                        Logger.Error("The following resources are available");
                        LogResource(Application.Current.Resources, "==>");
                    }
                }

                if (Current != null) {
                    var result = Task.Factory.StartNew(
                        () => MessageBox.Show(
                                    Loc.Instance["LblApplicationInBreakMode"],
                                    Loc.Instance["LblUnhandledException"],
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Error,
                                    MessageBoxResult.No),
                        TaskCreationOptions.LongRunning).Result;
                    if (result == MessageBoxResult.Yes) {
                        e.Handled = true;
                    } else {
                        e.Handled = true;
                        Current.Shutdown();
                    }
                }
            }
        }
    }
}