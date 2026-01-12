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
        System.Diagnostics.Debug.WriteLine("testtttt");
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
            // Don't close immediately, let the user see the error and close manually
            return;
        }

        // find script indices for monobehaviours we care about
        // note: hashset required because for some reason the same
        // monobehaviour can show up multiple times in one type tree
        // bruh...
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

        // Diagnostic: check if we found any PlayMaker scripts
        bool useFallbackMethod = false;
        if (playMakerFsmSis.Count == 0 && fsmTemplateSis.Count == 0)
        {
            // No script types in the file's type tree - this can happen with older Unity versions
            // or stripped asset files. We'll use a fallback method that checks all MonoBehaviours.
            if (scriptInfos.Count == 0)
            {
                SearchText = "Using fallback detection...";
                useFallbackMethod = true;
            }
            else
            {
                var fileDir = PathUtils.GetAssetsFileDirectory(_fileInst);
                SearchText = "Error: No PlayMaker";
                await MessageBoxUtil.ShowDialog("No PlayMaker FSMs found",
                    $"Found {scriptInfos.Count} scripts in asset file, but none from PlayMaker.dll\n\n" +
                    $"Assemblies loaded from:\n{fileDir}\\Managed\n\n" +
                    "This could mean:\n" +
                    "- This asset file doesn't contain any FSMs\n" +
                    "- The game doesn't use PlayMaker\n" +
                    "- PlayMaker.dll isn't in the Managed folder\n\n" +
                    "Try opening a different asset file (like sharedassets0.assets or level file)");
                // Don't close immediately, let the user see the error and close manually
                return;
            }
        }

        var file = _fileInst.file;
        var afNamer = new AfAssetNamer(_manager, _fileInst);

        // Diagnostic counters
        int totalAssets = file.AssetInfos.Count;
        int monoBehaviourCount = 0;
        int validScriptIndexCount = 0;
        var uniqueScriptIndices = new HashSet<ushort>();
        var assetTypeCounts = new Dictionary<int, int>();
        var actualTypeCounts = new Dictionary<int, int>();
        int typeIdMismatchCount = 0;

        foreach (var info in file.AssetInfos)
        {
            // Track all asset types for diagnostics
            if (!assetTypeCounts.ContainsKey(info.TypeId))
                assetTypeCounts[info.TypeId] = 0;
            assetTypeCounts[info.TypeId]++;

            // Check both TypeId and GetTypeId - sometimes they differ
            var actualTypeId = info.GetTypeId(file);

            if (!actualTypeCounts.ContainsKey(actualTypeId))
                actualTypeCounts[actualTypeId] = 0;
            actualTypeCounts[actualTypeId]++;

            if (info.TypeId != actualTypeId)
                typeIdMismatchCount++;

            // Check for MonoBehaviour (114) or negative type IDs (custom scripts/MonoBehaviours in older Unity)
            bool isMonoBehaviour = info.TypeId == (int)AssetClassID.MonoBehaviour ||
                                   actualTypeId == (int)AssetClassID.MonoBehaviour ||
                                   info.TypeId < 0; // Negative type IDs are often MonoBehaviours in older Unity versions

            if (!isMonoBehaviour)
                continue;

            monoBehaviourCount++;

            if (useFallbackMethod)
            {
                // Fallback: Try to read the MonoBehaviour and check if it has "fsm" field
                try
                {
                    var baseField = ReadMonoBehaviourSafe(_manager, _fileInst, info);
                    if (baseField != null)
                    {
                        var fsmField = baseField["fsm"];
                        if (fsmField != null && !fsmField.IsDummy)
                        {
                            // This looks like a PlayMakerFSM!
                            // Cache the baseField since we might need it later
                            var fsmName = GetFSMNameSafe(_manager, _fileInst, info, afNamer, baseField);
                            var fsmPtr = new AssetPPtr(_fileInst.name, info.PathId);
                            _internalEntries.Add(new FsmSelectorListEntry(fsmName, fsmPtr, baseField));
                            validScriptIndexCount++;
                        }
                    }
                }
                catch
                {
                    // Not a PlayMaker FSM, skip it
                }
                continue;
            }

            var infoSi = info.GetScriptIndex(_fileInst.file);

            if (infoSi == ushort.MaxValue)
            {
                // For negative type IDs with no script index, try fallback detection
                if (info.TypeId < 0)
                {
                    try
                    {
                        var baseField = ReadMonoBehaviourSafe(_manager, _fileInst, info);
                        if (baseField != null)
                        {
                            var fsmField = baseField["fsm"];
                            if (fsmField != null && !fsmField.IsDummy)
                            {
                                // Verify we can actually read the FSM name field
                                var fsmNameField = fsmField["name"];
                                if (fsmNameField != null && !fsmNameField.IsDummy)
                                {
                                    // This looks like a valid PlayMakerFSM we can read!
                                    // Cache the baseField since GetExtAsset won't work for negative type IDs
                                    var fsmName = GetFSMNameSafe(_manager, _fileInst, info, afNamer, baseField);
                                    var fsmPtr = new AssetPPtr(_fileInst.name, info.PathId);
                                    _internalEntries.Add(new FsmSelectorListEntry(fsmName, fsmPtr, baseField));
                                    validScriptIndexCount++;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Not a PlayMaker FSM, skip it
                    }
                }
                continue;
            }

            validScriptIndexCount++;
            uniqueScriptIndices.Add(infoSi);

            if (playMakerFsmSis.Contains(infoSi))
            {
                try
                {
                    // Read the MonoBehaviour using UABEA's method with RefTypeManager support
                    var baseField = ReadMonoBehaviourSafe(_manager, _fileInst, info);

                    if (baseField != null)
                    {
                        // Debug: Check the fsm field before adding to list
                        var fsmField = baseField["fsm"];
                        bool hasFsmField = fsmField != null && !fsmField.IsDummy;
                        System.Diagnostics.Debug.WriteLine($"FSM PathId={info.PathId}, TypeId={info.TypeId}, hasFsmField={hasFsmField}, baseField.IsDummy={baseField.IsDummy}");

                        var fsmName = GetFSMNameSafe(_manager, _fileInst, info, afNamer, baseField);
                        var fsmPtr = new AssetPPtr(_fileInst.name, info.PathId);
                        _internalEntries.Add(new FsmSelectorListEntry(fsmName, fsmPtr, baseField));
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"ReadMonoBehaviourSafe returned null for PathId={info.PathId}, TypeId={info.TypeId}");
                    }
                }
                catch (Exception ex)
                {
                    // If we can't read the FSM name, skip it
                    System.Diagnostics.Debug.WriteLine($"Failed to read FSM: {ex.Message}");
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
                    // Couldn't read template FSM, skip it
                }
            }
        }

        _internalEntries.Sort((a, b) => a.Name.CompareTo(b.Name));

        // Debug: Show diagnostic info
        string monoTempGenType = _manager.MonoTempGenerator != null ? _manager.MonoTempGenerator.GetType().Name : "null";
        int typeTreeCount = _fileInst.file.Metadata.TypeTreeTypes.Count;
        string firstTypeTree = typeTreeCount > 0 ? $"TypeId={_fileInst.file.Metadata.TypeTreeTypes[0].TypeId}" : "none";
        await MessageBoxUtil.ShowDialog("Debug Info",
            $"Loaded {_internalEntries.Count} FSMs\n" +
            $"TypeTreeEnabled: {_fileInst.file.Metadata.TypeTreeEnabled}\n" +
            $"TypeTreeTypes count: {typeTreeCount}\n" +
            $"First type: {firstTypeTree}\n" +
            $"Unity Version: {_fileInst.file.Metadata.UnityVersion}\n" +
            $"MonoTempGenerator: {monoTempGenType}\n" +
            $"First FSM name: {(_internalEntries.Count > 0 ? _internalEntries[0].Name : "N/A")}");

        // Diagnostic: check if we actually found any FSM instances
        if (_internalEntries.Count == 0)
        {
            SearchText = "Error: No FSMs found";

            var playMakerIndices = string.Join(", ", playMakerFsmSis);
            var foundIndices = string.Join(", ", uniqueScriptIndices);

            // Build asset type breakdown
            var topAssetTypes = assetTypeCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .Select(kvp => $"  Type {kvp.Key}: {kvp.Value} assets")
                .ToList();
            var assetTypeBreakdown = string.Join("\n", topAssetTypes);

            var topActualTypes = actualTypeCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(10)
                .Select(kvp => $"  Type {kvp.Key}: {kvp.Value} assets")
                .ToList();
            var actualTypeBreakdown = string.Join("\n", topActualTypes);

            await MessageBoxUtil.ShowDialog("No FSMs in this file - Detailed Diagnostics",
                $"Asset Analysis:\n" +
                $"- Total assets: {totalAssets}\n" +
                $"- MonoBehaviour assets: {monoBehaviourCount}\n" +
                $"- MonoBehaviours checked: {validScriptIndexCount}\n" +
                $"- Assets with TypeId != GetTypeId: {typeIdMismatchCount}\n" +
                $"- Used fallback detection: {useFallbackMethod}\n\n" +
                $"Top 10 Asset Types (TypeId field):\n{assetTypeBreakdown}\n\n" +
                $"Top 10 Asset Types (GetTypeId()):\n{actualTypeBreakdown}\n\n" +
                $"Script Type Detection:\n" +
                $"- PlayMakerFSM script types found: {playMakerFsmSis.Count} (indices: {playMakerIndices})\n" +
                $"- FsmTemplate script types found: {fsmTemplateSis.Count}\n\n" +
                $"Script Indices in MonoBehaviours:\n" +
                $"- Unique script indices found: {uniqueScriptIndices.Count}\n" +
                $"- Indices: {(foundIndices.Length > 100 ? foundIndices.Substring(0, 100) + "..." : foundIndices)}\n\n" +
                $"{(useFallbackMethod ? "Fallback method was used (no script types in file).\n" : "")}" +
                "Try opening a different .assets file - FSMs might be in:\n" +
                "- sharedassets1.assets, sharedassets2.assets, etc.\n" +
                "- level0, level1, level2, level3 (extensionless files)\n" +
                "- resources.assets");
            // Don't close immediately, let the user see the error and close manually
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
            // Get template using the overload that takes reader, position, classId, monoId
            ushort monoId = fileInst.file.GetScriptIndex(info);
            long position = info.GetAbsoluteByteOffset(fileInst.file);

            // Even though TypeTreeEnabled is False, Unity 5.0 can have type tree data in TypeTreeTypes
            // Use None to let it try type trees first, then fall back to DLLs if needed
            var template = manager.GetTemplateBaseField(
                fileInst,
                fileInst.file.Reader,
                position,
                info.TypeId,
                monoId,
                AssetReadFlags.None
            );

            // Get RefTypeManager for MonoBehaviours (needed for Unity 5.0)
            RefTypeManager? refMan = null;
            if (info.TypeId == (int)AssetClassID.MonoBehaviour || info.TypeId < 0)
            {
                refMan = manager.GetRefTypeManager(fileInst);
            }

            // MakeValue with RefTypeManager
            lock (fileInst.LockReader)
            {
                var result = template.MakeValue(
                    fileInst.file.Reader,
                    position,
                    refMan
                );

                // Debug: check if result is dummy and what fields it has
                if (result != null)
                {
                    if (result.IsDummy)
                    {
                        System.Diagnostics.Debug.WriteLine($"ReadMonoBehaviourSafe returned dummy field for TypeId={info.TypeId}, MonoId={monoId}");
                    }
                    else
                    {
                        // Log the first few field names to see what we got
                        var fieldNames = string.Join(", ", result.Children.Take(10).Select(f => f.FieldName));
                        System.Diagnostics.Debug.WriteLine($"PathId={info.PathId}, TypeId={info.TypeId}, Fields: {fieldNames}");
                    }
                }

                return result;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReadMonoBehaviourSafe exception: {ex.Message}");
            // If the above fails, fall back to GetBaseField
            try
            {
                return manager.GetBaseField(fileInst, info);
            }
            catch
            {
                return null;
            }
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