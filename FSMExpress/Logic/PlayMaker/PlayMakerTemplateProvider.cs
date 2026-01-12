using AssetsTools.NET;
using System.Collections.Generic;

namespace FSMExpress.Logic.PlayMaker;

/// <summary>
/// Provides hardcoded type templates for PlayMaker types when DLLs are stripped
/// and TypeTree data is unavailable.
/// </summary>
public static class PlayMakerTemplateProvider
{
    /// <summary>
    /// Gets a template for a PlayMaker type by name.
    /// Returns null if the type is not a known PlayMaker type.
    /// </summary>
    public static AssetTypeTemplateField? GetPlayMakerTemplate(string typeName)
    {
        return typeName switch
        {
            "FsmInt" => CreateFsmIntTemplate(),
            "FsmFloat" => CreateFsmFloatTemplate(),
            "FsmBool" => CreateFsmBoolTemplate(),
            "FsmString" => CreateFsmStringTemplate(),
            "FsmVector2" => CreateFsmVector2Template(),
            "FsmVector3" => CreateFsmVector3Template(),
            "FsmColor" => CreateFsmColorTemplate(),
            "FsmRect" => CreateFsmRectTemplate(),
            "FsmQuaternion" => CreateFsmQuaternionTemplate(),
            "FsmGameObject" => CreateFsmGameObjectTemplate(),
            "FsmObject" => CreateFsmObjectTemplate(),
            "FsmMaterial" => CreateFsmMaterialTemplate(),
            "FsmTexture" => CreateFsmTextureTemplate(),
            "FsmEnum" => CreateFsmEnumTemplate(),
            "FsmArray" => CreateFsmArrayTemplate(),
            _ => null
        };
    }

    /// <summary>
    /// Enhances a template by recursively replacing DUMMY PlayMaker types with real templates.
    /// </summary>
    public static void EnhanceTemplateWithPlayMakerTypes(AssetTypeTemplateField template)
    {
        EnhanceTemplateRecursive(template);
        ValidateTemplate(template, "root");
    }

    private static void ValidateTemplate(AssetTypeTemplateField field, string path)
    {
        if (field == null)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR: Null field at {path}");
            return;
        }

        if (field.ValueType == AssetValueType.None && field.Children == null)
        {
            System.Diagnostics.Debug.WriteLine($"ERROR: Field '{path}' has ValueType.None but null Children! Type='{field.Type}'");
        }

        if (field.Children != null)
        {
            for (int i = 0; i < field.Children.Count; i++)
            {
                ValidateTemplate(field.Children[i], $"{path}.{field.Children[i]?.Name ?? "null"}");
            }
        }
    }

    private static void EnhanceTemplateRecursive(AssetTypeTemplateField field)
    {
        if (field.Children == null)
            return;

        for (int i = 0; i < field.Children.Count; i++)
        {
            var child = field.Children[i];

            // Check if this field's type name suggests it's an array (e.g., ends with "Variables" or "Params")
            bool looksLikeArray = child.Name.EndsWith("Variables") || child.Name.EndsWith("Params") ||
                                  child.Name.EndsWith("values") || child.Name.EndsWith("references");

            // Check if this is an array field with PlayMaker type elements
            if ((child.IsArray || looksLikeArray) && child.Children != null && child.Children.Count >= 2)
            {
                // For arrays, check the data element (second child) for PlayMaker types
                var dataElement = child.Children[1];
                var pmDataTemplate = GetPlayMakerTemplate(dataElement.Type);
                if (pmDataTemplate != null)
                {
                    System.Diagnostics.Debug.WriteLine($"  Enhancing array field '{child.Name}' data element of type '{dataElement.Type}' with PlayMaker template");
                    child.Children[1] = pmDataTemplate;
                    child.Children[1].Name = "data";
                }
                else
                {
                    // Recursively enhance the array's data element
                    EnhanceTemplateRecursive(dataElement);
                }
            }
            else
            {
                // Check if this is a single PlayMaker type field (not an array)
                var pmTemplate = GetPlayMakerTemplate(child.Type);
                if (pmTemplate != null && !looksLikeArray)
                {
                    System.Diagnostics.Debug.WriteLine($"  Enhancing single field '{child.Name}' of type '{child.Type}' with PlayMaker template");
                    // Replace the field with our enhanced template
                    field.Children[i] = pmTemplate;
                    field.Children[i].Name = child.Name;

                    // Validate the template
                    if (field.Children[i].Children == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"  WARNING: Enhanced template for '{child.Name}' has null Children!");
                    }
                }
                else
                {
                    // Recursively enhance children
                    EnhanceTemplateRecursive(child);
                }
            }
        }
    }

    // Helper to create base NamedVariable fields
    private static List<AssetTypeTemplateField> CreateNamedVariableFields()
    {
        return new List<AssetTypeTemplateField>
        {
            CreateField("useVariable", "bool", false),
            CreateField("name", "string", false),
            CreateField("tooltip", "string", false),
            CreateField("showInInspector", "bool", false),
            CreateField("networkSync", "bool", false)
        };
    }

    private static AssetTypeTemplateField CreateFsmIntTemplate()
    {
        var fields = CreateNamedVariableFields();
        fields.Add(CreateField("value", "int", false));
        return CreateComplexField("FsmInt", "FsmInt", fields);
    }

    private static AssetTypeTemplateField CreateFsmFloatTemplate()
    {
        var fields = CreateNamedVariableFields();
        fields.Add(CreateField("value", "float", false));
        return CreateComplexField("FsmFloat", "FsmFloat", fields);
    }

    private static AssetTypeTemplateField CreateFsmBoolTemplate()
    {
        var fields = CreateNamedVariableFields();
        fields.Add(CreateField("value", "bool", false));
        return CreateComplexField("FsmBool", "FsmBool", fields);
    }

    private static AssetTypeTemplateField CreateFsmStringTemplate()
    {
        var fields = CreateNamedVariableFields();
        fields.Add(CreateField("value", "string", false));
        return CreateComplexField("FsmString", "FsmString", fields);
    }

    private static AssetTypeTemplateField CreateFsmVector2Template()
    {
        var fields = CreateNamedVariableFields();
        var valueFields = new List<AssetTypeTemplateField>
        {
            CreateField("x", "float", false),
            CreateField("y", "float", false)
        };
        fields.Add(CreateComplexField("value", "Vector2f", valueFields));
        return CreateComplexField("FsmVector2", "FsmVector2", fields);
    }

    private static AssetTypeTemplateField CreateFsmVector3Template()
    {
        var fields = CreateNamedVariableFields();
        var valueFields = new List<AssetTypeTemplateField>
        {
            CreateField("x", "float", false),
            CreateField("y", "float", false),
            CreateField("z", "float", false)
        };
        fields.Add(CreateComplexField("value", "Vector3f", valueFields));
        return CreateComplexField("FsmVector3", "FsmVector3", fields);
    }

    private static AssetTypeTemplateField CreateFsmColorTemplate()
    {
        var fields = CreateNamedVariableFields();
        var valueFields = new List<AssetTypeTemplateField>
        {
            CreateField("r", "float", false),
            CreateField("g", "float", false),
            CreateField("b", "float", false),
            CreateField("a", "float", false)
        };
        // Use ColorRGBA as-is (already correct in common string table)
        fields.Add(CreateComplexField("value", "ColorRGBA", valueFields));
        return CreateComplexField("FsmColor", "FsmColor", fields);
    }

    private static AssetTypeTemplateField CreateFsmRectTemplate()
    {
        var fields = CreateNamedVariableFields();
        var valueFields = new List<AssetTypeTemplateField>
        {
            CreateField("x", "float", false),
            CreateField("y", "float", false),
            CreateField("width", "float", false),
            CreateField("height", "float", false)
        };
        // Use Rectf (note lowercase 'f')
        fields.Add(CreateComplexField("value", "Rectf", valueFields));
        return CreateComplexField("FsmRect", "FsmRect", fields);
    }

    private static AssetTypeTemplateField CreateFsmQuaternionTemplate()
    {
        var fields = CreateNamedVariableFields();
        var valueFields = new List<AssetTypeTemplateField>
        {
            CreateField("x", "float", false),
            CreateField("y", "float", false),
            CreateField("z", "float", false),
            CreateField("w", "float", false)
        };
        // Use Quaternionf (note lowercase 'f')
        fields.Add(CreateComplexField("value", "Quaternionf", valueFields));
        return CreateComplexField("FsmQuaternion", "FsmQuaternion", fields);
    }

    private static AssetTypeTemplateField CreateFsmGameObjectTemplate()
    {
        var fields = CreateNamedVariableFields();
        var valueFields = new List<AssetTypeTemplateField>
        {
            CreateField("m_FileID", "int", false),
            CreateField("m_PathID", "SInt64", false)
        };
        fields.Add(CreateComplexField("value", "PPtr<GameObject>", valueFields));
        return CreateComplexField("FsmGameObject", "FsmGameObject", fields);
    }

    private static AssetTypeTemplateField CreateFsmObjectTemplate()
    {
        var fields = CreateNamedVariableFields();
        fields.Add(CreateField("objectType", "string", false));
        var valueFields = new List<AssetTypeTemplateField>
        {
            CreateField("m_FileID", "int", false),
            CreateField("m_PathID", "SInt64", false)
        };
        fields.Add(CreateComplexField("value", "PPtr<Object>", valueFields));
        return CreateComplexField("FsmObject", "FsmObject", fields);
    }

    private static AssetTypeTemplateField CreateFsmMaterialTemplate()
    {
        var fields = CreateNamedVariableFields();
        var valueFields = new List<AssetTypeTemplateField>
        {
            CreateField("m_FileID", "int", false),
            CreateField("m_PathID", "SInt64", false)
        };
        fields.Add(CreateComplexField("value", "PPtr<Material>", valueFields));
        return CreateComplexField("FsmMaterial", "FsmMaterial", fields);
    }

    private static AssetTypeTemplateField CreateFsmTextureTemplate()
    {
        var fields = CreateNamedVariableFields();
        var valueFields = new List<AssetTypeTemplateField>
        {
            CreateField("m_FileID", "int", false),
            CreateField("m_PathID", "SInt64", false)
        };
        fields.Add(CreateComplexField("value", "PPtr<Texture>", valueFields));
        return CreateComplexField("FsmTexture", "FsmTexture", fields);
    }

    private static AssetTypeTemplateField CreateFsmEnumTemplate()
    {
        var fields = CreateNamedVariableFields();
        fields.Add(CreateField("enumName", "string", false));
        fields.Add(CreateField("value", "int", false));
        return CreateComplexField("FsmEnum", "FsmEnum", fields);
    }

    private static AssetTypeTemplateField CreateFsmArrayTemplate()
    {
        var fields = CreateNamedVariableFields();
        fields.Add(CreateField("type", "int", false)); // VariableType enum
        fields.Add(CreateField("objectType", "string", false));
        fields.Add(CreateField("sizeLimit", "int", false));
        // Arrays of values - these will be populated at runtime
        fields.Add(CreateField("floatValues", "float", true));
        fields.Add(CreateField("intValues", "int", true));
        fields.Add(CreateField("boolValues", "bool", true));
        fields.Add(CreateField("stringValues", "string", true));
        fields.Add(CreateField("vector4Values", "Vector4f", true));
        fields.Add(CreateField("objectReferences", "PPtr<Object>", true));
        return CreateComplexField("FsmArray", "FsmArray", fields);
    }

    private static AssetTypeTemplateField CreateField(string name, string type, bool isArray)
    {
        var field = new AssetTypeTemplateField
        {
            Name = name,
            Type = type,
            ValueType = isArray ? AssetValueType.Array : GetValueType(type),
            IsArray = isArray,
            IsAligned = ShouldAlign(type),
            HasValue = !isArray && IsValueType(type),
            Children = null
        };

        // Arrays need exactly 2 children: size and data element
        if (isArray)
        {
            var dataElement = new AssetTypeTemplateField
            {
                Name = "data",
                Type = type,
                ValueType = GetValueType(type),
                IsArray = false,
                IsAligned = false,
                HasValue = IsValueType(type),
                Children = null
            };

            // Handle complex types like Vector4f, PPtr, etc.
            if (type == "Vector4f")
            {
                dataElement.Children = new List<AssetTypeTemplateField>
                {
                    CreateField("x", "float", false),
                    CreateField("y", "float", false),
                    CreateField("z", "float", false),
                    CreateField("w", "float", false)
                };
            }
            else if (type.StartsWith("PPtr<"))
            {
                dataElement.Children = new List<AssetTypeTemplateField>
                {
                    CreateField("m_FileID", "int", false),
                    CreateField("m_PathID", "SInt64", false)
                };
            }

            field.Children = new List<AssetTypeTemplateField>
            {
                new AssetTypeTemplateField
                {
                    Name = "size",
                    Type = "int",
                    ValueType = AssetValueType.Int32,
                    IsArray = false,
                    IsAligned = false,
                    HasValue = true,
                    Children = null
                },
                dataElement
            };
        }

        return field;
    }

    private static AssetTypeTemplateField CreateComplexField(string name, string type, List<AssetTypeTemplateField> children)
    {
        return new AssetTypeTemplateField
        {
            Name = name,
            Type = type,
            ValueType = AssetValueType.None,
            IsArray = false,
            IsAligned = false,
            HasValue = false,
            Children = children
        };
    }

    private static AssetValueType GetValueType(string type)
    {
        return type switch
        {
            "bool" => AssetValueType.Bool,
            "UInt8" => AssetValueType.UInt8,
            "int" or "SInt32" => AssetValueType.Int32,
            "UInt32" => AssetValueType.UInt32,
            "SInt64" => AssetValueType.Int64,
            "UInt64" => AssetValueType.UInt64,
            "float" => AssetValueType.Float,
            "double" => AssetValueType.Double,
            "string" => AssetValueType.String,
            _ => AssetValueType.None
        };
    }

    private static bool IsValueType(string type)
    {
        return type switch
        {
            "bool" or "UInt8" or "int" or "SInt32" or "UInt32" or "SInt64" or "UInt64" or "float" or "double" or "string" => true,
            _ => false
        };
    }

    private static bool ShouldAlign(string type)
    {
        return type == "string" || type == "bool" || type == "UInt8";
    }
}
