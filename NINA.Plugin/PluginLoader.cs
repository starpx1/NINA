﻿#region "copyright"

/*
    Copyright © 2016 - 2023 Stefan Berg <isbeorn86+NINA@googlemail.com> and the N.I.N.A. contributors

    This file is part of N.I.N.A. - Nighttime Imaging 'N' Astronomy.

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

#endregion "copyright"

using NINA.Astrometry.Interfaces;
using NINA.Core.Interfaces;
using NINA.Core.Locale;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Core.Utility.WindowService;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Image.ImageAnalysis;
using NINA.Image.Interfaces;
using NINA.PlateSolving.Interfaces;
using NINA.Plugin.Interfaces;
using NINA.Plugin.ManifestDefinition;
using NINA.Profile.Interfaces;
using NINA.Sequencer;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Utility.DateTimeProvider;
using NINA.WPF.Base.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Trinet.Core.IO.Ntfs;

namespace NINA.Plugin {

    public partial class PluginLoader : IPluginLoader {

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddDllDirectory(string lpPathName);

        public PluginLoader(IProfileService profileService,
                              ICameraMediator cameraMediator,
                              ITelescopeMediator telescopeMediator,
                              IFocuserMediator focuserMediator,
                              IFilterWheelMediator filterWheelMediator,
                              IGuiderMediator guiderMediator,
                              IRotatorMediator rotatorMediator,
                              IFlatDeviceMediator flatDeviceMediator,
                              IWeatherDataMediator weatherDataMediator,
                              IImagingMediator imagingMediator,
                              IApplicationStatusMediator applicationStatusMediator,
                              INighttimeCalculator nighttimeCalculator,
                              IPlanetariumFactory planetariumFactory,
                              IImageHistoryVM imageHistoryVM,
                              IDeepSkyObjectSearchVM deepSkyObjectSearchVM,
                              IDomeMediator domeMediator,
                              IImageSaveMediator imageSaveMediator,
                              ISwitchMediator switchMediator,
                              ISafetyMonitorMediator safetyMonitorMediator,
                              IApplicationResourceDictionary resourceDictionary,
                              IApplicationMediator applicationMediator,
                              IFramingAssistantVM framingAssistantVM,
                              IPlateSolverFactory plateSolverFactory,
                              IWindowServiceFactory windowServiceFactory,
                              IDomeFollower domeFollower,
                              IPluggableBehaviorSelector<IStarDetection> starDetectionSelector,
                              IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector,
                              IImageDataFactory imageDataFactory,
                              IMeridianFlipVMFactory meridianFlipVMFactory,
                              IAutoFocusVMFactory autoFocusVMFactory,
                              IImageControlVM imageControlVM,
                              IImageStatisticsVM imageStatisticsVM,
                              IDomeSynchronization domeSynchronization,
                              ISequenceMediator sequenceMediator,
                              IOptionsVM optionsVM,
                              IExposureDataFactory exposureDataFactory,
                              ITwilightCalculator twilightCalculator) {
            this.profileService = profileService;
            this.cameraMediator = cameraMediator;
            this.telescopeMediator = telescopeMediator;
            this.focuserMediator = focuserMediator;
            this.filterWheelMediator = filterWheelMediator;
            this.guiderMediator = guiderMediator;
            this.rotatorMediator = rotatorMediator;
            this.flatDeviceMediator = flatDeviceMediator;
            this.weatherDataMediator = weatherDataMediator;
            this.imagingMediator = imagingMediator;
            this.applicationStatusMediator = applicationStatusMediator;
            this.nighttimeCalculator = nighttimeCalculator;
            this.planetariumFactory = planetariumFactory;
            this.imageHistoryVM = imageHistoryVM;
            this.deepSkyObjectSearchVM = deepSkyObjectSearchVM;
            this.domeMediator = domeMediator;
            this.imageSaveMediator = imageSaveMediator;
            this.switchMediator = switchMediator;
            this.safetyMonitorMediator = safetyMonitorMediator;
            this.resourceDictionary = resourceDictionary;
            this.applicationMediator = applicationMediator;
            this.framingAssistantVM = framingAssistantVM;
            this.platesolverFactory = plateSolverFactory;
            this.windowServiceFactory = windowServiceFactory;
            this.domeFollower = domeFollower;
            this.starDetectionSelector = starDetectionSelector;
            this.starAnnotatorSelector = starAnnotatorSelector;
            this.imageDataFactory = imageDataFactory;
            this.meridianFlipVMFactory = meridianFlipVMFactory;
            this.autoFocusVMFactory = autoFocusVMFactory;
            this.imageControlVM = imageControlVM;
            this.imageStatisticsVM = imageStatisticsVM;
            this.domeSynchronization = domeSynchronization;
            this.sequenceMediator = sequenceMediator;
            this.optionsVM = optionsVM;
            this.exposureDataFactory = exposureDataFactory;
            this.twilightCalculator = twilightCalculator;

            DateTimeProviders = new List<IDateTimeProvider>() {
                new TimeProvider(nighttimeCalculator),
                new SunsetProvider(nighttimeCalculator),
                new NauticalDuskProvider(nighttimeCalculator),
                new DuskProvider(nighttimeCalculator),
                new DawnProvider(nighttimeCalculator),
                new NauticalDawnProvider(nighttimeCalculator),
                new SunriseProvider(nighttimeCalculator),
                new MeridianProvider(profileService)
            };
            assemblyReferencePathMap = new Dictionary<string, string>();
            compatibilityMap = new PluginCompatibilityMap();
        }

        private readonly PluginCompatibilityMap compatibilityMap;

        private void DeployFromStaging() {
            var staging = Constants.StagingFolder;
            var destination = Constants.UserExtensionsFolder;

            if (Directory.Exists(staging)) {
                try {
                    var sourcePath = staging.TrimEnd('\\', ' ');
                    var targetPath = destination.TrimEnd('\\', ' ');
                    var files = Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories)
                                         .GroupBy(s => Path.GetDirectoryName(s));
                    foreach (var folder in files) {
                        try {
                            var targetFolder = folder.Key.Replace(sourcePath, targetPath);
                            Directory.CreateDirectory(targetFolder);
                            foreach (var file in folder) {
                                var targetFile = Path.Combine(targetFolder, Path.GetFileName(file));
                                if (File.Exists(targetFile)) {
                                    File.Delete(targetFile);
                                }

                                File.Move(file, targetFile);
                            }
                        } catch (Exception ex) {
                            Logger.Error("Failed to deploy plugin file to destination", ex);
                        }
                    }                    
                } catch (Exception ex) {
                    Logger.Error("Pluging deployment from staging failed", ex);
                } finally {
                    try {
                        Directory.Delete(staging, true);
                    } catch(Exception ex) {
                        Logger.Error("Deleting staging folder failed", ex);
                    }                    
                }
            }
        }

        private void CleanupEmptyFolders() {
            try {
                if (Directory.Exists(Constants.UserExtensionsFolder)) {
                    foreach (var dir in Directory.GetDirectories(Constants.UserExtensionsFolder)) {
                        if (!Directory.EnumerateFileSystemEntries(dir, "*.*", SearchOption.AllDirectories).Any()) {
                            Directory.Delete(dir);
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Error($"Error occured on plugin folder cleanup", ex);
            }
        }

        private void DeleteFromDeletion() {
            try {
                if (Directory.Exists(Constants.DeletionFolder)) {
                    Directory.Delete(Constants.DeletionFolder, true);
                }
            } catch (Exception ex) {
                Logger.Error("Plugin deletion from deletion folder failed", ex);
            }
        }

        public Task Load() {
            return Task.Run(async () => {
                lock (lockobj) {
                    if (!initialized) {
                        try {
                            Stopwatch sw = Stopwatch.StartNew();

                            //Check for pending plugin updates and deploy them
                            CleanupEmptyFolders();
                            DeployFromStaging();
                            DeleteFromDeletion();

                            Items = new List<ISequenceItem>();
                            Conditions = new List<ISequenceCondition>();
                            Triggers = new List<ISequenceTrigger>();
                            Container = new List<ISequenceContainer>();
                            DockableVMs = new List<IDockableVM>();
                            PluggableBehaviors = new List<IPluggableBehavior>();
                            DeviceProviders = new List<IEquipmentProvider>();
                            Plugins = new Dictionary<IPluginManifest, bool>();

                            AppDomain.CurrentDomain.AssemblyResolve += CurrentDomain_AssemblyResolve;

                            /* Compose the core catalog */
                            var types = GetCoreSequencerTypes();
                            var sdkTypes = GetCoreEquipmentSDKTypes();
                            var coreCatalog = new TypeCatalog(types.Concat(sdkTypes));

                            Compose(coreCatalog, string.Empty);

                            /* Compose the plugin catalog */

                            var files = new List<string>();

                            if (Directory.Exists(Constants.CoreExtensionsFolder)) {
                                files.AddRange(Directory.GetFiles(Constants.CoreExtensionsFolder, "*.dll"));
                            }

                            // Enumerate only 1 level deep, where we'd expect plugin dlls to be in the root of the plugin folder
                            var userExtensionsDirectory = new DirectoryInfo(Constants.UserExtensionsFolder);
                            if (userExtensionsDirectory.Exists) {
                                files.AddRange(userExtensionsDirectory.GetFiles("*.dll").Select(fi => fi.FullName));
                                foreach (var subDirectory in userExtensionsDirectory.GetDirectories()) {
                                    files.AddRange(subDirectory.GetFiles("*.dll").Select(fi => fi.FullName));
                                }
                            }

                            for (int i = 0; i < files.Count; i++) {
                                var file = files[i];
                                AsyncContext.Run(() => LoadPlugin(file, (i + 1) / (double)files.Count));
                            }

                            initialized = true;
                            Debug.Print($"Time to load all plugins {sw.Elapsed}");
                        } catch (Exception ex) {
                            Logger.Error(ex);
                        } finally {
                            applicationStatusMediator.StatusUpdate(new ApplicationStatus() { Source = Loc.Instance["LblPlugins"] });
                        }
                    }
                }
            });
        }

        private async Task LoadPlugin(string file, double progress) {
            Stopwatch sw = Stopwatch.StartNew();
            try {
                var applicationVersion = new Version(CoreUtil.Version);

                var pluginFileInfo = new FileInfo(file);
                if (pluginFileInfo.AlternateDataStreamExists("Zone.Identifier")) {
                    pluginFileInfo.DeleteAlternateDataStream("Zone.Identifier");
                }
                
                var references = PluginAssemblyReader.GrabAssemblyReferences(file);

                var pluginDllDirectory = new DirectoryInfo(Path.Combine(pluginFileInfo.Directory.FullName, "dll"));
                if (pluginDllDirectory.Exists) {
                    // If there's a dll sub-directory, enumerate the references for any potentially matching dlls, and add them
                    // to a dictionary that can be used by the assembly resolver
                    foreach (var reference in references) {
                        if (assemblyReferencePathMap.ContainsKey(reference)) {
                            continue;
                        }

                        var assemblyName = new AssemblyName(reference);
                        var assemblyPath = Path.Combine(pluginDllDirectory.FullName, assemblyName.Name + ".dll");
                        if (!File.Exists(assemblyPath)) {
                            continue;
                        }
                        assemblyReferencePathMap.Add(reference, assemblyPath);
                    }
                }

                if (references.FirstOrDefault(x => x.Contains("NINA.Plugin")) != null) {
                    try {
                        var assembly = Assembly.LoadFrom(file);
                        var plugin = new AssemblyCatalog(assembly);

                        var manifestImport = new ManifestImport();
                        var container = GetContainer(plugin);
                        container.ComposeParts(manifestImport);

                        var manifest = manifestImport.PluginManifestImport;

                        try {
                            if (!PluginVersion.IsPluginCompatible(manifest.MinimumApplicationVersion, applicationVersion)) {
                                throw new Exception($"The plugin is not compatible with this version of N.I.N.A. as it requires a minimum version of {manifest.MinimumApplicationVersion}, but N.I.N.A. is {applicationVersion}");
                            }

                            if (!compatibilityMap.IsCompatible(manifest)) {

                                var isDeprecated = compatibilityMap.IsDeprecated(manifest);
                                var isNotCompatible = compatibilityMap.IsNotCompatible(manifest);
                                var isUpdateRequired = compatibilityMap.IsUpdateRequired(manifest);

                                if(isDeprecated) {
                                    throw new Exception($"This plugin is deprecated.");
                                }

                                if (isUpdateRequired) {
                                    throw new Exception($"The version of this plugin is not compatible with the current version of N.I.N.A. Please update the plugin.");
                                }

                                if(isNotCompatible) {
                                    throw new Exception($"The plugin is not compatible with this version of N.I.N.A. as it was compiled against {manifest.MinimumApplicationVersion} but it has to be built against at least {compatibilityMap.MinimumMajorVersion}. Please check if there is a plugin update available.");
                                }
                            }

                            applicationStatusMediator.StatusUpdate(new ApplicationStatus() { 
                                Source = Loc.Instance["LblPlugins"], 
                                Status = Loc.Instance["LblInitializingPlugins"],
                                Status2 = string.Format(Loc.Instance["LblLoadingPlugin"], manifest.Name, manifest.Version, manifest.Author),
                                Progress = progress,
                                ProgressType = ApplicationStatus.StatusProgressType.Percent
                            });

                            Compose(plugin, manifest.Name);

                            await manifest.Initialize();

                            Plugins[manifest] = true;

                            //Add the loaded plugin assembly to the assembly resolver
                            Assemblies.Add(plugin.Assembly);

                            // If there's a dll sub-directory for the plugin, add it to the dll search path to help deal with subsequent loads
                            if (pluginDllDirectory.Exists && !AddDllDirectory(pluginDllDirectory.FullName)) {
                                Logger.Warning($"Failed to add {pluginDllDirectory.FullName} to dll search path");
                            }
                            Logger.Info($"Successfully loaded plugin {manifest.Name} version {manifest.Version} by {manifest.Author}");
                        } catch (Exception ex) {
                            //Manifest ok - plugin composition failed
                            var failedManifest = new PluginManifest {
                                Author = manifest.Author,
                                Identifier = string.IsNullOrWhiteSpace(manifest?.Identifier) ? file : manifest.Identifier,
                                Name = manifest.Name,
                                Version = manifest.Version,
                                Descriptions = new PluginDescription {
                                    ShortDescription = $"Failed to load {file}",
                                    LongDescription = ex.Message
                                }
                            };
                            Notification.ShowError(string.Format(Loc.Instance["LblPluginFailedToLoad"], failedManifest.Name, failedManifest.Version));
                            Logger.Error($"Failed to load plugin at {file} - {failedManifest.Name} version {failedManifest.Version}", ex);
                            Plugins[failedManifest] = false;
                        }
                    } catch (Exception ex) {
                        var message = ex.Message;
                        if (ex is ReflectionTypeLoadException typeLoadException) {
                            var loaderExceptions = typeLoadException.LoaderExceptions;
                            message = string.Join(Environment.NewLine, loaderExceptions.ToList());
                        }

                        var metadata = PluginAssemblyReader.GrabPluginMetaData(file);

                        var id = metadata[nameof(GuidAttribute)];
                        var author = metadata[nameof(AssemblyCompanyAttribute)];
                        var version = new PluginVersion(metadata[nameof(AssemblyFileVersionAttribute)]);
                        var name = metadata[nameof(AssemblyTitleAttribute)];

                        //Manifest failed - Create a fake manifest using all available file meta info
                        var fvi = FileVersionInfo.GetVersionInfo(file);
                        var fileVersion = new Version(fvi.FileVersion);
                        var failedManifest = new PluginManifest {
                            Author = fvi.CompanyName,
                            Identifier = id,
                            Name = name,
                            Version = version,
                            Descriptions = new PluginDescription {
                                ShortDescription = $"Failed to load {file}",
                                LongDescription = message
                            }
                        };

                        Plugins[failedManifest] = false;
                        Logger.Error($"Failed to load plugin at {file} - {failedManifest.Name} version {failedManifest.Version} {message}");
                        Notification.ShowError(string.Format(Loc.Instance["LblPluginFailedToLoad"], failedManifest.Name, failedManifest.Version));
                    }
                } else {
                    Logger.Trace($"The dll {file} does not reference NINA.Plugin");
                }
            } catch (Exception ex) {
                //This should only happen for non plugin assemblies, that are not even targeting .NET
                Logger.Trace($"The dll inside the plugins folder failed to load. Most likely it is not a pugin but an external non .NET dependency. File: {file}, Error: {ex}");
            } finally {
                Debug.Print($"Time to load plugin {Path.GetFileNameWithoutExtension(file)} {sw.Elapsed}");
            }
        }

        private void Compose(ComposablePartCatalog catalog, string pluginName) {
            try {
                var container = GetContainer(catalog);
                var parts = new PartsImport();
                container.ComposeParts(parts);

                foreach (var template in parts.DataTemplateImports) {
                    Application.Current?.Resources.MergedDictionaries.Add(template);
                }

                Items = Items.Concat(AssignSequenceEntity(parts.ItemImports, resourceDictionary, pluginName)).ToList();
                Conditions = Conditions.Concat(AssignSequenceEntity(parts.ConditionImports, resourceDictionary, pluginName)).ToList();
                Triggers = Triggers.Concat(AssignSequenceEntity(parts.TriggerImports, resourceDictionary, pluginName)).ToList();
                Container = Container.Concat(AssignSequenceEntity(parts.ContainerImports, resourceDictionary, pluginName)).ToList();

                DockableVMs = DockableVMs.Concat(parts.DockableVMImports).ToList();
                PluggableBehaviors = PluggableBehaviors.Concat(parts.PluggableBehaviorImports).ToList();
                DeviceProviders = DeviceProviders.Concat(parts.DeviceProviderImports).ToList();
            } catch (Exception ex) {
                Logger.Error(ex);
                throw;
            }
        }

        private CompositionContainer GetContainer(ComposablePartCatalog catalog) {
            var container = new CompositionContainer(catalog);
            container.ComposeExportedValue(profileService);
            container.ComposeExportedValue(cameraMediator);
            container.ComposeExportedValue(telescopeMediator);
            container.ComposeExportedValue(focuserMediator);
            container.ComposeExportedValue(filterWheelMediator);
            container.ComposeExportedValue(guiderMediator);
            container.ComposeExportedValue(rotatorMediator);
            container.ComposeExportedValue(flatDeviceMediator);
            container.ComposeExportedValue(weatherDataMediator);
            container.ComposeExportedValue(imagingMediator);
            container.ComposeExportedValue(applicationStatusMediator);
            container.ComposeExportedValue(nighttimeCalculator);
            container.ComposeExportedValue(planetariumFactory);
            container.ComposeExportedValue(imageHistoryVM);
            container.ComposeExportedValue(deepSkyObjectSearchVM);
            container.ComposeExportedValue(domeMediator);
            container.ComposeExportedValue(imageSaveMediator);
            container.ComposeExportedValue(switchMediator);
            container.ComposeExportedValue(resourceDictionary);
            container.ComposeExportedValue(DateTimeProviders);
            container.ComposeExportedValue(safetyMonitorMediator);
            container.ComposeExportedValue(applicationMediator);
            container.ComposeExportedValue(framingAssistantVM);
            container.ComposeExportedValue(platesolverFactory);
            container.ComposeExportedValue(windowServiceFactory);
            container.ComposeExportedValue(domeFollower);
            container.ComposeExportedValue(starDetectionSelector);
            container.ComposeExportedValue(starAnnotatorSelector);
            container.ComposeExportedValue(imageDataFactory);
            container.ComposeExportedValue(autoFocusVMFactory);
            container.ComposeExportedValue(meridianFlipVMFactory);
            container.ComposeExportedValue(imageControlVM);
            container.ComposeExportedValue(imageStatisticsVM);
            container.ComposeExportedValue(domeSynchronization);
            container.ComposeExportedValue(sequenceMediator);
            container.ComposeExportedValue(optionsVM);
            container.ComposeExportedValue(exposureDataFactory);
            container.ComposeExportedValue(twilightCalculator);

            return container;
        }

        private object lockobj = new object();
        private bool initialized = false;
        public IList<IDateTimeProvider> DateTimeProviders { get; }
        public IList<ISequenceItem> Items { get; private set; }
        public IList<ISequenceCondition> Conditions { get; private set; }
        public IList<ISequenceTrigger> Triggers { get; private set; }
        public IList<ISequenceContainer> Container { get; private set; }
        public IList<IDockableVM> DockableVMs { get; private set; }
        public IList<IPluggableBehavior> PluggableBehaviors { get; private set; }
        public IDictionary<IPluginManifest, bool> Plugins { get; private set; }
        public IList<Assembly> Assemblies { get; private set; } = new List<Assembly>();
        public IList<IEquipmentProvider> DeviceProviders { get; private set; }

        private readonly IProfileService profileService;
        private readonly ICameraMediator cameraMediator;
        private readonly ITelescopeMediator telescopeMediator;
        private readonly IFocuserMediator focuserMediator;
        private readonly IFilterWheelMediator filterWheelMediator;
        private readonly IGuiderMediator guiderMediator;
        private readonly IRotatorMediator rotatorMediator;
        private readonly IFlatDeviceMediator flatDeviceMediator;
        private readonly IWeatherDataMediator weatherDataMediator;
        private readonly IImagingMediator imagingMediator;
        private readonly IApplicationStatusMediator applicationStatusMediator;
        private readonly INighttimeCalculator nighttimeCalculator;
        private readonly IPlanetariumFactory planetariumFactory;
        private readonly IImageHistoryVM imageHistoryVM;
        private readonly IDeepSkyObjectSearchVM deepSkyObjectSearchVM;
        private readonly IDomeMediator domeMediator;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly ISwitchMediator switchMediator;
        private readonly ISafetyMonitorMediator safetyMonitorMediator;
        private readonly IApplicationResourceDictionary resourceDictionary;
        private readonly IApplicationMediator applicationMediator;
        private readonly IFramingAssistantVM framingAssistantVM;
        private readonly IPlateSolverFactory platesolverFactory;
        private readonly IWindowServiceFactory windowServiceFactory;
        private readonly IDomeFollower domeFollower;
        private readonly IPluggableBehaviorSelector<IStarDetection> starDetectionSelector;
        private readonly IPluggableBehaviorSelector<IStarAnnotator> starAnnotatorSelector;
        private readonly IImageDataFactory imageDataFactory;
        private readonly IAutoFocusVMFactory autoFocusVMFactory;
        private readonly IMeridianFlipVMFactory meridianFlipVMFactory;
        private readonly IImageControlVM imageControlVM;
        private readonly IImageStatisticsVM imageStatisticsVM;
        private readonly IDomeSynchronization domeSynchronization;
        private readonly ISequenceMediator sequenceMediator;
        private readonly IOptionsVM optionsVM;
        private readonly IExposureDataFactory exposureDataFactory;
        private readonly ITwilightCalculator twilightCalculator;
        private readonly Dictionary<string, string> assemblyReferencePathMap;

        private Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args) {
            var assembly = this.Assemblies.FirstOrDefault(x => x.GetName().Name == args.Name);
            if (assembly == null) {
                if (assemblyReferencePathMap.TryGetValue(args.Name, out string assemblyPath)) {
                    try {
                        assembly = Assembly.LoadFrom(assemblyPath);
                    } catch (Exception) {
                        Logger.Warning($"Failed to load dependent assembly {args.Name} using {assemblyPath}");
                    }
                }
            }
            return assembly;
        }

        /// <summary>
        /// This returns a list of types in the NINA.Sequencer namespace to load the core plugins
        /// Furthermore this safeguards against the ASCOM assembly that is not required to load when the platform is not installed
        /// </summary>
        /// <returns></returns>
        private static IEnumerable<Type> GetCoreSequencerTypes() {
            IEnumerable<Type> loadableTypes;
            try {
                loadableTypes = Assembly.GetAssembly(typeof(ISequenceItem)).GetTypes();
            } catch (ReflectionTypeLoadException e) {
                loadableTypes = e.Types.Where(t => t != null);
            }

            List<Type> sequencerTypes = new List<Type>();
            foreach (Type t in loadableTypes) {
                try {
                    if (t.IsClass && t.Namespace?.StartsWith("NINA.Sequencer") == true) {
                        sequencerTypes.Add(t);
                    }
                } catch (Exception) {
                }
            }
            return sequencerTypes;
        }

        private static IEnumerable<Type> GetCoreEquipmentSDKTypes() {
            IEnumerable<Type> loadableTypes;
            try {
                loadableTypes = Assembly.GetAssembly(typeof(IDevice)).GetTypes();
            } catch (ReflectionTypeLoadException e) {
                loadableTypes = e.Types.Where(t => t != null);
            }

            List<Type> coreTypes = new List<Type>();
            foreach (Type t in loadableTypes) {
                try {
                    if (t.IsClass && t.GetInterfaces().Contains(typeof(IEquipmentProvider)) == true) {
                        coreTypes.Add(t);
                    }
                } catch (Exception) {
                }
            }
            return coreTypes;
        }
        
                   
        private IOrderedEnumerable<T> AssignSequenceEntity<T>(IEnumerable<Lazy<T, Dictionary<string, object>>> imports, IApplicationResourceDictionary resourceDictionary,string pluginName) where T : ISequenceEntity {
            var items = new List<T>();
            foreach (var importItem in imports) {
                try {
                    var item = importItem.Value;
                    if (importItem.Metadata.TryGetValue("Name", out var nameObj)) {
                        string name = nameObj.ToString();
                        item.Name = GrabLabel(name);
                    }
                    if (importItem.Metadata.TryGetValue("Description", out var descriptionObj)) {
                        string description = descriptionObj.ToString();
                        item.Description = GrabLabel(description);
                        if (!string.IsNullOrEmpty(pluginName)) {
                            item.Description += $"{Environment.NewLine}({pluginName})";
                        }                        
                    }
                    if (importItem.Metadata.TryGetValue("Icon", out var iconObj)) {
                        string icon = iconObj.ToString();
                        item.Icon = (System.Windows.Media.GeometryGroup)resourceDictionary[icon];
                    }
                    if (importItem.Metadata.TryGetValue("Category", out var categoryObj)) {
                        string category = categoryObj.ToString();
                        item.Category = GrabLabel(category);
                    }
                    items.Add(item);
                } catch (Exception ex) {
                    // Skip item if anything fails
                    Logger.Error("Plugin Item failed to load ", ex);
                }
            }
            return items.OrderBy(item => item.Category + item.Name);
        }

        private string GrabLabel(string label) {
            if (label.StartsWith("Lbl_")) {
                return Loc.Instance[label];
            } else {
                return label;
            }
        }
    }

    public class ManifestImport {

        [Import(typeof(IPluginManifest))]
        public IPluginManifest PluginManifestImport { get; private set; }
    }

    public class PartsImport {

        [ImportMany(typeof(ISequenceItem))]
        public IEnumerable<Lazy<ISequenceItem, Dictionary<string, object>>> ItemImports { get; private set; }

        [ImportMany(typeof(ISequenceCondition))]
        public IEnumerable<Lazy<ISequenceCondition, Dictionary<string, object>>> ConditionImports { get; private set; }

        [ImportMany(typeof(ISequenceTrigger))]
        public IEnumerable<Lazy<ISequenceTrigger, Dictionary<string, object>>> TriggerImports { get; private set; }

        [ImportMany(typeof(ISequenceContainer))]
        public IEnumerable<Lazy<ISequenceContainer, Dictionary<string, object>>> ContainerImports { get; private set; }

        [ImportMany(typeof(ResourceDictionary))]
        public IEnumerable<ResourceDictionary> DataTemplateImports { get; private set; }

        [ImportMany(typeof(IDockableVM))]
        public IEnumerable<IDockableVM> DockableVMImports { get; private set; }

        [ImportMany(typeof(IPluggableBehavior))]
        public IEnumerable<IPluggableBehavior> PluggableBehaviorImports { get; private set; }

        [ImportMany(typeof(IEquipmentProvider))]
        public IEnumerable<IEquipmentProvider> DeviceProviderImports { get; private set; }

        
    }
}