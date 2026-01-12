using AssetsTools.NET;
using FSMExpress.Common.Interfaces;

namespace FSMExpress.Common.Assets;
public class AfAssetField(AssetTypeValueField valueField, AfAssetNamer namer) : IAssetField
{
    public bool Exists(string name)
    {
        try
        {
            var field = valueField[name];
            return field != null && !field.IsDummy;
        }
        catch
        {
            return false;
        }
    }

    public bool Exists(int index)
    {
        return !valueField[index].IsDummy;
    }

    public IAssetField GetField(string name)
    {
        return new AfAssetField(valueField[name], namer);
    }

    public IAssetField GetField(int index)
    {
        return new AfAssetField(valueField[index], namer);
    }

    public T GetValue<T>(string name)
    {
        try
        {
            var field = valueField[name];
            if (field == null)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Field '{name}' not found in {valueField.TypeName}!");
            }
            else if (field.IsDummy)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Field '{name}' is DUMMY in {valueField.TypeName}!");
            }
            return GetValueImpl<T>(field);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WARNING: Exception accessing field '{name}' in {valueField.TypeName}: {ex.GetType().Name} - {ex.Message}");
            return default(T)!;
        }
    }

    public T GetValue<T>(int index)
    {
        return GetValueImpl<T>(valueField[index]);
    }

    public List<T> GetValueArray<T>(string name, Func<IAssetField, T> mapper)
    {
        try
        {
            var field = valueField[name];
            
            // Check if the field itself is null or dummy before accessing children
            if (field == null || field.IsDummy)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Field '{name}' is null or dummy, returning empty array");
                return [];
            }

            var arrayField = field["Array"];
            
            // Check if the Array child is null or dummy
            if (arrayField == null || arrayField.IsDummy)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Array field for '{name}' is null or dummy, returning empty array");
                return [];
            }

            return GetValueArrayImpl(arrayField, mapper);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WARNING: Exception in GetValueArray for '{name}': {ex.Message}");
            return [];
        }
    }

    public List<T> GetValueArray<T>(int index, Func<IAssetField, T> mapper)
    {
        try
        {
            var field = valueField[index];
            
            // Check if the field itself is null or dummy before accessing children
            if (field == null || field.IsDummy)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Field at index {index} is null or dummy, returning empty array");
                return [];
            }

            var arrayField = field[0];
            
            // Check if the Array child is null or dummy
            if (arrayField == null || arrayField.IsDummy)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Array field at index {index} is null or dummy, returning empty array");
                return [];
            }

            return GetValueArrayImpl(arrayField, mapper);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WARNING: Exception in GetValueArray at index {index}: {ex.Message}");
            return [];
        }
    }

    private T GetValueImpl<T>(AssetTypeValueField field)
    {
        // Check if field is DUMMY before trying to read it
        if (field == null || field.IsDummy)
        {
            System.Diagnostics.Debug.WriteLine($"WARNING: Attempting to read value from DUMMY or null field! Type: {typeof(T).Name}");
            // Return default value for the type
            return default(T)!;
        }

        Type paramType = typeof(T);
        Type genericType;

        if (paramType == typeof(int) || paramType.IsEnum)
        {
            return (T)(object)field.AsInt;
        }
        else if (paramType == typeof(float))
        {
            return (T)(object)field.AsFloat;
        }
        else if (paramType == typeof(NamedAssetPPtr))
        {
            var fileId = field["m_FileID"].AsInt;
            var pathId = field["m_PathID"].AsLong;
            var name = namer.GetName(fileId, pathId) ?? string.Empty;

            var pptr = new NamedAssetPPtr(string.Empty, fileId, pathId, name);
            namer.NameAssetPPtrFile(pptr);

            return (T)(object)pptr;
        }
        else if (paramType.IsGenericType && (genericType = paramType.GetGenericTypeDefinition()) == typeof(List<>))
        {
            return GetValueArrayPrimitiveImpl<T>(field["Array"], paramType.GetGenericArguments()[0]);
        }
        else if (paramType == typeof(bool))
        {
            return (T)(object)(field.AsByte != 0);
        }
        else if (paramType == typeof(long))
        {
            return (T)(object)field.AsLong;
        }
        else if (paramType == typeof(string))
        {
            return (T)(object)field.AsString;
        }
        else if (paramType == typeof(byte))
        {
            return (T)(object)field.AsByte;
        }
        else if (paramType == typeof(uint))
        {
            return (T)(object)field.AsUInt;
        }
        else if (paramType == typeof(ushort))
        {
            return (T)(object)field.AsUShort;
        }
        else if (paramType == typeof(sbyte))
        {
            return (T)(object)field.AsSByte;
        }
        else if (paramType == typeof(short))
        {
            return (T)(object)field.AsShort;
        }
        else if (paramType == typeof(ulong))
        {
            return (T)(object)field.AsULong;
        }
        else if (paramType == typeof(double))
        {
            return (T)(object)field.AsDouble;
        }

        throw new ArgumentException($"Not a valid type to read for field ({paramType.Name})");
    }

    private T GetValueArrayPrimitiveImpl<T>(AssetTypeValueField field, Type genericType)
    {
        if (genericType == typeof(int))
        {
            var list = new List<int>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsInt);

            return (T)(object)list;
        }
        else if (genericType.IsEnum)
        {
            var list = new List<int>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsInt);

            var method = typeof(Enumerable).GetMethod("Cast")?.MakeGenericMethod(genericType)
                ?? throw new Exception("couldn't create enum list");

            var enumList = list.Select(i => Enum.ToObject(genericType, i));
            var casted = method.Invoke(null, [enumList]);
            var newList = typeof(Enumerable).GetMethod("ToList")?.MakeGenericMethod(genericType).Invoke(null, [casted])
                ?? throw new Exception("couldn't create enum list");

            return (T)newList;
        }
        else if (genericType == typeof(float))
        {
            var list = new List<float>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsFloat);

            return (T)(object)list;
        }
        else if (genericType == typeof(NamedAssetPPtr))
        {
            var list = new List<NamedAssetPPtr>(field.Children.Count);
            foreach (var child in field)
            {
                var fileId = child["m_FileID"].AsInt;
                var pathId = child["m_PathID"].AsLong;
                var name = namer.GetName(fileId, pathId) ?? string.Empty;

                var pptr = new NamedAssetPPtr(string.Empty, fileId, pathId, name);
                namer.NameAssetPPtrFile(pptr);

                list.Add(pptr);
            }

            return (T)(object)list;
        }
        else if (genericType == typeof(bool))
        {
            var list = new List<bool>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsByte != 0);

            return (T)(object)list;
        }
        else if (genericType == typeof(long))
        {
            var list = new List<long>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsLong);

            return (T)(object)list;
        }
        else if (genericType == typeof(string))
        {
            var list = new List<string>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsString);

            return (T)(object)list;
        }
        else if (genericType == typeof(byte))
        {
            // https://github.com/nesrak1/AssetsTools.NET/blob/a73a399cee3fedadcfa3c1861e6192414ad588fb/AssetTools.NET/Extra/MonoDeserializer/CommonMonoTemplateHelper.cs#L213
            // it appears mono deserializers automatically turn arrays of bytes
            // into ByteArray typed fields, so we have to use .AsByteArray here.
            return (T)(object)field.AsByteArray.ToList();
        }
        else if (genericType == typeof(uint))
        {
            var list = new List<uint>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsUInt);

            return (T)(object)list;
        }
        else if (genericType == typeof(ushort))
        {
            var list = new List<ushort>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsUShort);

            return (T)(object)list;
        }
        else if (genericType == typeof(sbyte))
        {
            var list = new List<sbyte>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsSByte);

            return (T)(object)list;
        }
        else if (genericType == typeof(short))
        {
            var list = new List<short>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsShort);

            return (T)(object)list;
        }
        else if (genericType == typeof(ulong))
        {
            var list = new List<ulong>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsULong);

            return (T)(object)list;
        }
        else if (genericType == typeof(double))
        {
            var list = new List<double>(field.Children.Count);
            foreach (var child in field)
                list.Add(child.AsDouble);

            return (T)(object)list;
        }

        throw new ArgumentException($"Not a valid type to read for list field ({genericType.Name})");
    }

    private List<T> GetValueArrayImpl<T>(AssetTypeValueField field, Func<IAssetField, T> mapper)
    {
        // Check if field is null or dummy
        if (field == null || field.IsDummy)
        {
            System.Diagnostics.Debug.WriteLine($"WARNING: GetValueArrayImpl called with null or dummy field!");
            return [];
        }

        // Arrays should have children, but if they don't (empty array), return empty list
        if (field.Children.Count == 0)
        {
            System.Diagnostics.Debug.WriteLine($"WARNING: Array field has no children (empty array)");
            return [];
        }

        var list = new List<T>(field.Children.Count);
        foreach (var child in field)
        {
            // Skip dummy children
            if (child.IsDummy)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Skipping dummy child in array");
                continue;
            }

            try
            {
                list.Add(mapper(new AfAssetField(child, namer)));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WARNING: Failed to map array element: {ex.Message}");
                // Continue processing other elements
            }
        }

        return list;
    }
}
