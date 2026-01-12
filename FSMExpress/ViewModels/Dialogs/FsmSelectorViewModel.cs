using AssetsTools.NET;
using AssetsTools.NET.Extra;
using CommunityToolkit.Mvvm.ComponentModel;
using FSMExpress.Common.Assets;
using FSMExpress.Logic.Util;
using FSMExpress.Services;
using FSMExpress.Util;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

// todo: this needs to be genericized

namespace FSMExpress.ViewModels.Dialogs;
public partial class FsmSelectorViewModel : ViewModelBase, IDialogAware<IList<FsmSelectorListEntry>>
{
    [ObservableProperty]
    private string _searchText = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFsmSelected))]
    public ObservableCollection<FsmSelectorListEntry> _selectedEntries = [];
    [ObservableProperty]
    private RangeObservableCollection<FsmSelectorListEntry> _entries = [];

    private List<FsmSelectorListEntry> _internalEntries = [];

    private readonly AssetsManager _manager;
    private readonly AssetsFileInstance _fileInst;
    private readonly Action<string> _searchDb;

    public string Title => "FSM Selector";
    public int Width => 350;
    public int Height => 450;
    public event Action<IList<FsmSelectorListEntry>?>? RequestClose;

    public bool IsFsmSelected => SelectedEntries.Count > 0;

    public Task AsyncInit() => FillFsmEntries();

    public FsmSelectorViewModel(AssetsManager manager, AssetsFileInstance fileInst)
    {
        _manager = manager;
        _fileInst = fileInst;
        _searchDb = DebounceUtils.Debounce<string>(FilterEntries, 300);
    }

    partial void OnSearchTextChanged(string value) => _searchDb(value);

    private void FilterEntries(string searchText)
    {
        Entries.Clear();
        if (searchText == string.Empty)
            Entries.AddRange(_internalEntries);
        else
            Entries.AddRange(_internalEntries.Where(e => e.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)));

        if (Entries.Count > 0)
            SelectedEntries = [Entries[0]];
    }

    public async Task FillFsmEntries()
    {
        SearchText = "Loading...";
        if (!_manager.LoadMonoBehaviours(_fileInst))
        {
            var fileDir = PathUtils.GetAssetsFileDirectory(_fileInst);
            SearchText = "Error: No assemblies";
            await MessageBoxUtil.ShowDialog("Mono error",
                $"Couldn't find game assemblies in:\n{fileDir}\n\n" +
                "Looking for:\n" +
                "- Managed folder with .dll files, OR\n" +
                "- IL2CPP files (global-metadata.dat)\n\n" +
                "Try setting the game path to the [GameName]_Data folder via Config > Set game path");
            return;
        }

        var playMakerFsmSis = new HashSet<ushort>();
        var fsmTemplateSis = new HashSet<ushort>();
        var scriptInfos = AssetHelper.GetAssetsFileScriptInfos(_manager, _fileInst);
        foreach (var scriptInfo in scriptInfos)
        {
            var scriptInfoRef = scriptInfo.Value;
            var asmName = scriptInfoRef.AsmName;
            var nameSpace = scriptInfoRef.Namespace;
            var className = scriptInfoRef.ClassName;
            if (asmName == "PlayMaker.dll" && nameSpace == "" && className == "PlayMakerFSM")
                playMakerFsmSis.Add((ushort)scriptInfo.Key);
            if (asmName == "PlayMaker.dll" && nameSpace == "" && className == "FsmTemplate")
                fsmTemplateSis.Add((ushort)scriptInfo.Key);
        }

        bool useFallbackMethod = playMakerFsmSis.Count == 0 && fsmTemplateSis.Count == 0 && scriptInfos.Count == 0;

        if (playMakerFsmSis.Count == 0 && fsmTemplateSis.Count == 0 && scriptInfos.Count > 0)
        {
            var fileDir = PathUtils.GetAssetsFileDirectory(_fileInst);
            SearchText = "Error: No PlayMaker";
            await MessageBoxUtil.ShowDialog("No PlayMaker FSMs found",
                $"Found {scriptInfos.Count} scripts in asset file, but none from PlayMaker.dll\n\n" +
                "Try opening a different asset file (like sharedassets0.assets or level file)");
            return;
        }

        var file = _fileInst.file;
        var afNamer = new AfAssetNamer(_manager, _fileInst);

        foreach (var info in file.AssetInfos)
        {
            var actualTypeId = info.GetTypeId(file);
            bool isMonoBehaviour = info.TypeId == (int)AssetClassID.MonoBehaviour ||
                                   actualTypeId == (int)AssetClassID.MonoBehaviour ||
                                   info.TypeId < 0;

            if (!isMonoBehaviour)
                continue;

            if (useFallbackMethod)
            {
                try
                {
                    var baseField = ReadMonoBehaviourSafe(_manager, _fileInst, info);
                    if (baseField != null && baseField["fsm"] != null && !baseField["fsm"].IsDummy)
                    {
                        var fsmName = GetFSMNameSafe(_manager, _fileInst, info, afNamer, baseField);
                        var fsmPtr = new AssetPPtr(_fileInst.name, info.PathId);
                        _internalEntries.Add(new FsmSelectorListEntry(fsmName, fsmPtr, baseField));
                    }
                }
                catch
                {
                    // Skip non-PlayMaker MonoBehaviours
                }
                continue;
            }

            var infoSi = info.GetScriptIndex(_fileInst.file);

            if (infoSi == ushort.MaxValue && info.TypeId < 0)
            {
                try
                {
                    var baseField = ReadMonoBehaviourSafe(_manager, _fileInst, info);
                    if (baseField != null && baseField["fsm"] != null && !baseField["fsm"].IsDummy)
                    {
                        var fsmName = GetFSMNameSafe(_manager, _fileInst, info, afNamer, baseField);
                        var fsmPtr = new AssetPPtr(_fileInst.name, info.PathId);
                        _internalEntries.Add(new FsmSelectorListEntry(fsmName, fsmPtr, baseField));
                    }
                }
                catch
                {
                    // Skip non-PlayMaker MonoBehaviours
                }
                continue;
            }

            if (playMakerFsmSis.Contains(infoSi))
            {
                try
                {
                    var baseField = ReadMonoBehaviourSafe(_manager, _fileInst, info);
                    if (baseField != null)
                    {
                        var fsmName = GetFSMNameSafe(_manager, _fileInst, info, afNamer, baseField);
                        var fsmPtr = new AssetPPtr(_fileInst.name, info.PathId);
                        _internalEntries.Add(new FsmSelectorListEntry(fsmName, fsmPtr, baseField));
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to read FSM PathId={info.PathId}: {ex.Message}");
                }
            }

            if (fsmTemplateSis.Contains(infoSi))
            {
                try
                {
                    var fsmName = GetFSMNameFastTemplate(_manager, _fileInst, info, afNamer);
                    var fsmPtr = new AssetPPtr(_fileInst.name, info.PathId);
                    _internalEntries.Add(new FsmSelectorListEntry(fsmName, fsmPtr));
                }
                catch
                {
                    // Skip corrupted templates
                }
            }
        }

        _internalEntries.Sort((a, b) => a.Name.CompareTo(b.Name));

        if (_internalEntries.Count == 0)
        {
            SearchText = "Error: No FSMs found";
            await MessageBoxUtil.ShowDialog("No FSMs found",
                "No PlayMaker FSMs were found in this file.\n\n" +
                "Try opening a different .assets file - FSMs might be in:\n" +
                "- sharedassets0.assets, sharedassets1.assets, etc.\n" +
                "- level0, level1, level2 (scene files)\n" +
                "- resources.assets");
            return;
        }

        SearchText = "";
        FilterEntries(string.Empty);
    }

    /// <summary>
    /// Reads a MonoBehaviour asset using UABEA's method with RefTypeManager support for Unity 5.0
    /// </summary>
    private static AssetTypeValueField? ReadMonoBehaviourSafe(AssetsManager manager, AssetsFileInstance fileInst, AssetFileInfo info)
    {
        try
        {
            // For negative type IDs (custom MonoBehaviours in Unity 5.0), we need to manually
            // get the enhanced template and use it to read the asset
            if (info.TypeId < 0)
            {
                ushort monoId = info.GetScriptIndex(assetsFile: fileInst.file);
                long position = info.GetAbsoluteByteOffset(fileInst.file);

                // Get base MonoBehaviour template
                var template = manager.GetTemplateBaseField(
                    fileInst,
                    fileInst.file.Reader,
                    position,
                    info.TypeId,
                    monoId,
                    AssetReadFlags.None
                );

                if (template != null)
                {
                    bool templateHasFsm = template.Children.Any(f => f.Name == "fsm");

                    // If template doesn't have fsm field yet, try to enhance it using MonoTempGenerator
                    if (!templateHasFsm && manager.MonoTempGenerator != null && monoId != 0xFFFF)
                    {
                        try
                        {
                            // Read m_Script PPtr to get MonoScript info
                            AssetTypeValueField? tempBaseField = null;
                            lock (fileInst.LockReader)
                            {
                                // IMPORTANT: Reset position before reading
                                fileInst.file.Reader.Position = position;
                                tempBaseField = template.MakeValue(fileInst.file.Reader, position);
                            }

                            var scriptPPtr = tempBaseField?["m_Script"];
                            if (scriptPPtr != null)
                            {
                                int scriptFileId = scriptPPtr["m_FileID"].AsInt;
                                long scriptPathId = scriptPPtr["m_PathID"].AsLong;

                                // Get the file containing the MonoScript
                                AssetsFileInstance? scriptFileInst = fileInst;
                                if (scriptFileId != 0)
                                {
                                    var dep = fileInst.GetDependency(manager, scriptFileId - 1);
                                    if (dep != null)
                                    {
                                        scriptFileInst = dep;
                                    }
                                }

                                if (scriptFileInst != null)
                                {
                                    var scriptInfo = scriptFileInst.file.GetAssetInfo(scriptPathId);
                                    if (scriptInfo != null)
                                    {
                                        var monoScriptBaseField = manager.GetBaseField(scriptFileInst, scriptInfo);
                                        if (monoScriptBaseField != null)
                                        {
                                            var classNameField = monoScriptBaseField["m_ClassName"];
                                            var namespaceField = monoScriptBaseField["m_Namespace"];
                                            var assemblyNameField = monoScriptBaseField["m_AssemblyName"];

                                            if (classNameField != null && assemblyNameField != null)
                                            {
                                                string scriptClassName = classNameField.AsString;
                                                string scriptNamespace = namespaceField?.AsString ?? string.Empty;
                                                string assemblyName = assemblyNameField.AsString;

                                                var enhancedTemplate = manager.MonoTempGenerator.GetTemplateField(
                                                    template,
                                                    assemblyName,
                                                    scriptNamespace,
                                                    scriptClassName,
                                                    new UnityVersion(fileInst.file.Metadata.UnityVersion)
                                                );

                                                if (enhancedTemplate != null)
                                                {
                                                    template = enhancedTemplate;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch
                        {
                            // Failed to enhance, continue with base template
                        }
                    }

                    // Now create the final value field using the (possibly enhanced) template
                    // IMPORTANT: Reset reader position before reading with the enhanced template
                    RefTypeManager? refMan = manager.GetRefTypeManager(fileInst);
                    
                    lock (fileInst.LockReader)
                    {
                        fileInst.file.Reader.Position = position;
                        return template.MakeValue(
                            fileInst.file.Reader,
                            position,
                            refMan
                        );
                    }
                }

                return null;
            }
            else
            {
                // For non-negative type IDs, GetBaseField works fine
                return manager.GetBaseField(fileInst, info);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReadMonoBehaviourSafe exception: {ex.Message}");
            return null;
        }
    }

    private static string GetFSMNameSafe(AssetsManager manager, AssetsFileInstance fileInst, AssetFileInfo info, AfAssetNamer namer, AssetTypeValueField? cachedBaseField = null)
    {
        // Use cached baseField if provided, otherwise get it
        var monoBf = cachedBaseField ?? ReadMonoBehaviourSafe(manager, fileInst, info);

        if (monoBf == null)
            return "Unknown GameObject - Unknown FSM";

        string fsmName = "Unknown FSM";
        string goName = "Unknown GameObject";

        try
        {
            var fsmField = monoBf["fsm"];
            if (fsmField != null && !fsmField.IsDummy)
            {
                var nameField = fsmField["name"];
                if (nameField != null && !nameField.IsDummy)
                {
                    fsmName = nameField.AsString;
                }
            }
        }
        catch
        {
            // FSM name couldn't be read
        }

        try
        {
            var goPtr = monoBf["m_GameObject"];
            if (goPtr != null && !goPtr.IsDummy)
            {
                var fileId = goPtr["m_FileID"];
                var pathId = goPtr["m_PathID"];
                if (fileId != null && !fileId.IsDummy && pathId != null && !pathId.IsDummy)
                {
                    goName = namer.GetName(fileId.AsInt, pathId.AsLong);
                }
            }
        }
        catch
        {
            // GameObject name couldn't be read
        }

        return $"{goName} - {fsmName}";
    }

    private static string GetFSMNameFast(AssetsManager manager, AssetsFileInstance fileInst, AssetFileInfo info, AfAssetNamer namer)
    {
        var fsmTemp = manager.GetTemplateBaseField(fileInst, info);

        var nameIndex = fsmTemp.Children.FindIndex(monoTemp => monoTemp.Name == "name");
        if (nameIndex != -1)
        {
            fsmTemp.Children.RemoveRange(nameIndex + 1, fsmTemp.Children.Count - (nameIndex + 1));
        }

        AssetTypeValueField? monoBf;
        lock (fileInst.LockReader)
        {
            monoBf = fsmTemp.MakeValue(fileInst.file.Reader, info.GetAbsoluteByteOffset(fileInst.file));
        }

        var fsmName = monoBf["fsm"]["name"].AsString;
        var goPtr = monoBf["m_GameObject"];
        var goName = namer.GetName(goPtr["m_FileID"].AsInt, goPtr["m_PathID"].AsLong);
        return $"{goName} - {fsmName}";
    }

    private static string GetFSMNameFastTemplate(AssetsManager manager, AssetsFileInstance fileInst, AssetFileInfo info, AfAssetNamer namer)
    {
        var fsmTemp = manager.GetTemplateBaseField(fileInst, info);

        var nameIndex = fsmTemp.Children.FindIndex(monoTemp => monoTemp.Name == "name");
        if (nameIndex != -1)
        {
            fsmTemp.Children.RemoveRange(nameIndex + 1, fsmTemp.Children.Count - (nameIndex + 1));
        }

        AssetTypeValueField? monoBf;
        lock (fileInst.LockReader)
        {
            monoBf = fsmTemp.MakeValue(fileInst.file.Reader, info.GetAbsoluteByteOffset(fileInst.file));
        }

        var fsmName = monoBf["fsm"]["name"].AsString;
        var name = monoBf["m_Name"].AsString;
        return $"{fsmName} = {name}";
    }

    public void PickSelectedEntries()
    {
        if (SelectedEntries.Count > 0)
        {
            RequestClose?.Invoke(SelectedEntries);
        }
    }

    public void PickCancel()
    {
        RequestClose?.Invoke(null);
    }
}

public class FsmSelectorListEntry(string name, AssetPPtr ptr, AssetTypeValueField? cachedBaseField = null)
{
    public string Name { get; } = name;
    public AssetPPtr Ptr { get; } = ptr;
    public AssetTypeValueField? CachedBaseField { get; } = cachedBaseField;
}