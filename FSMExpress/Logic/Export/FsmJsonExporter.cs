using FSMExpress.Common.Document;
using FSMExpress.PlayMaker;
using FSMExpress.PlayMaker.Structs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FSMExpress.Logic.Export;

/// <summary>
/// Exports FSM documents to JSON format for LLM analysis
/// </summary>
public static class FsmJsonExporter
{
    private static readonly JsonSerializerOptions _detailedOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonSerializerOptions _graphOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Export FSM to detailed JSON with all state actions and variables
    /// </summary>
    public static void ExportDetailed(FsmDocument document, string filePath)
    {
        var export = new DetailedFsmExport
        {
            Name = document.Name,
            SourceName = document.SourceName,
            States = document.Nodes.Select(n => new DetailedStateExport
            {
                Name = n.Name,
                IsStart = n.IsStart,
                IsGlobal = n.IsGlobal,
                Position = new PositionExport { X = n.Bounds.X, Y = n.Bounds.Y },
                Transitions = n.Transitions.Select(t => new TransitionExport
                {
                    Event = t.Name,
                    TargetState = t.ToNode?.Name
                }).ToList(),
                Actions = ExtractActions(n.Fields)
            }).ToList(),
            Events = document.Events.Select(e => new EventExport
            {
                Name = e.Name,
                IsSystem = e.IsSystem,
                IsGlobal = e.IsGlobal
            }).ToList(),
            Variables = ExtractVariables(document.Variables)
        };

        var json = JsonSerializer.Serialize(export, _detailedOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Export FSM to graph-optimized JSON for flow analysis
    /// </summary>
    public static void ExportGraph(FsmDocument document, string filePath)
    {
        var startNode = document.Nodes.FirstOrDefault(n => n.IsStart);
        
        var export = new GraphFsmExport
        {
            Name = document.Name,
            SourceName = document.SourceName,
            StartState = startNode?.Name,
            States = document.Nodes.Select(n => n.Name).ToList(),
            Transitions = document.Nodes.SelectMany(n => 
                n.Transitions.Select(t => new GraphTransitionExport
                {
                    From = n.Name,
                    To = t.ToNode?.Name,
                    Event = t.Name
                })
            ).ToList(),
            GlobalTransitions = document.Nodes
                .Where(n => n.IsGlobal)
                .SelectMany(n => n.Transitions.Select(t => new GraphTransitionExport
                {
                    From = "*",
                    To = t.ToNode?.Name,
                    Event = t.Name
                })).ToList(),
            Events = document.Events.Select(e => e.Name).ToList(),
            GlobalEvents = document.Events.Where(e => e.IsGlobal).Select(e => e.Name).ToList()
        };

        var json = JsonSerializer.Serialize(export, _graphOptions);
        File.WriteAllText(filePath, json);
    }

    private static List<ActionExport> ExtractActions(List<FsmDocumentNodeField> fields)
    {
        var actions = new List<ActionExport>();
        ActionExport? currentAction = null;

        foreach (var field in fields)
        {
            if (field is FsmDocumentNodeClassField classField)
            {
                if (currentAction != null)
                {
                    actions.Add(currentAction);
                }
                currentAction = new ActionExport
                {
                    ActionType = classField.TypeRef.ClassName,
                    IsEnabled = classField.IsEnabled,
                    Parameters = new Dictionary<string, object?>()
                };
            }
            else if (field is FsmDocumentNodeDataField dataField && currentAction != null)
            {
                currentAction.Parameters[dataField.Key] = GetFieldValue(dataField.Value);
            }
        }

        if (currentAction != null)
        {
            actions.Add(currentAction);
        }

        return actions;
    }

    private static Dictionary<string, object?> ExtractVariables(List<FsmDocumentNodeField> fields)
    {
        var variables = new Dictionary<string, object?>();
        string? currentType = null;

        foreach (var field in fields)
        {
            if (field is FsmDocumentNodeClassField classField)
            {
                currentType = classField.TypeRef.ClassName;
            }
            else if (field is FsmDocumentNodeDataField dataField)
            {
                variables[dataField.Key] = GetFieldValue(dataField.Value);
            }
        }

        return variables;
    }

    private static object? GetFieldValue(FsmDocumentNodeFieldValue value)
    {
        if (value is FsmPlaymakerValue pmValue)
        {
            return ExtractPlayMakerValue(pmValue);
        }
        
        if (value is FsmDocumentNodeFieldBooleanValue boolValue)
        {
            var displayStr = boolValue.DisplayString;
            if (displayStr.StartsWith("true") || displayStr.StartsWith("false"))
            {
                return bool.Parse(displayStr.Split(' ')[0]);
            }
            return displayStr;
        }
        
        if (value is FsmDocumentNodeFieldIntegerValue intValue)
        {
            var displayStr = intValue.DisplayString;
            var spaceIndex = displayStr.IndexOf(' ');
            var numStr = spaceIndex > 0 ? displayStr.Substring(0, spaceIndex) : displayStr;
            if (int.TryParse(numStr, out var result))
            {
                return result;
            }
            return displayStr;
        }
        
        if (value is FsmDocumentNodeFieldFloatValue floatValue)
        {
            var displayStr = floatValue.DisplayString;
            var spaceIndex = displayStr.IndexOf(' ');
            var numStr = spaceIndex > 0 ? displayStr.Substring(0, spaceIndex) : displayStr;
            numStr = numStr.TrimEnd('f');
            if (float.TryParse(numStr, out var result))
            {
                return result;
            }
            return displayStr;
        }
        
        if (value is FsmDocumentNodeFieldStringValue stringValue)
        {
            var displayStr = stringValue.DisplayString;
            var parenIndex = displayStr.LastIndexOf(" (");
            if (parenIndex > 0)
            {
                displayStr = displayStr.Substring(0, parenIndex);
            }
            return displayStr.Trim('"');
        }
        
        if (value is FsmDocumentNodeFieldArrayValue arrayValue)
        {
            return new Dictionary<string, object?>
            {
                ["type"] = arrayValue.DisplayType.TrimEnd('[', ']'),
                ["length"] = int.Parse(arrayValue.DisplayString.Split('[')[1].Split(' ')[0])
            };
        }
        
        if (value is FsmDocumentNodeFieldFallbackValue fallbackValue)
        {
            // Check if it's actually null
            var displayStr = fallbackValue.DisplayString;
            if (displayStr == "null")
            {
                return null;
            }
            return displayStr;
        }
        
        return value.DisplayString;
    }

    private static object? ExtractPlayMakerValue(FsmPlaymakerValue pmValue)
    {
        // Try to get the value from the primary constructor parameter (backing field)
        // In C# primary constructor parameters become private fields with the same name
        var valueField = pmValue.GetType()
            .GetField("value", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        object? valueObj = null;
        if (valueField != null)
        {
            valueObj = valueField.GetValue(pmValue);
        }
        else
        {
            // Fallback: try with underscore prefix or look for any field containing IFsmPlaymakerValuePreviewer
            var fields = pmValue.GetType().GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.FieldType.GetInterfaces().Any(i => i.Name == "IFsmPlaymakerValuePreviewer"))
                {
                    valueObj = field.GetValue(pmValue);
                    break;
                }
            }
        }

        if (valueObj == null)
        {
            // Value is legitimately null - don't return error string, just return null
            return null;
        }

        return valueObj switch
        {
            FsmBool fsmBool => string.IsNullOrEmpty(fsmBool.Name) && !fsmBool.UseVariable
                ? (object?)fsmBool.Value
                : new Dictionary<string, object?>
                {
                    ["value"] = fsmBool.Value,
                    ["name"] = fsmBool.Name,
                    ["useVariable"] = fsmBool.UseVariable
                },
            FsmInt fsmInt => string.IsNullOrEmpty(fsmInt.Name) && !fsmInt.UseVariable
                ? (object?)fsmInt.Value
                : new Dictionary<string, object?>
                {
                    ["value"] = fsmInt.Value,
                    ["name"] = fsmInt.Name,
                    ["useVariable"] = fsmInt.UseVariable
                },
            FsmFloat fsmFloat => string.IsNullOrEmpty(fsmFloat.Name) && !fsmFloat.UseVariable
                ? (object?)fsmFloat.Value
                : new Dictionary<string, object?>
                {
                    ["value"] = fsmFloat.Value,
                    ["name"] = fsmFloat.Name,
                    ["useVariable"] = fsmFloat.UseVariable
                },
            FsmString fsmString => string.IsNullOrEmpty(fsmString.Name) && !fsmString.UseVariable
                ? (object?)fsmString.Value  // Just return the string value if not using a variable
                : new Dictionary<string, object?>
                {
                    ["value"] = fsmString.Value,
                    ["name"] = fsmString.Name,
                    ["useVariable"] = fsmString.UseVariable
                },
            FsmGameObject fsmGameObject => new Dictionary<string, object?>
            {
                ["value"] = fsmGameObject.Value?.ToString(),
                ["name"] = fsmGameObject.Name,
                ["useVariable"] = fsmGameObject.UseVariable
            },
            FsmMaterial fsmMaterial => new Dictionary<string, object?>
            {
                ["value"] = fsmMaterial.Value?.ToString(),
                ["name"] = fsmMaterial.Name,
                ["useVariable"] = fsmMaterial.UseVariable,
                ["typeName"] = fsmMaterial.TypeName
            },
            FsmTexture fsmTexture => new Dictionary<string, object?>
            {
                ["value"] = fsmTexture.Value?.ToString(),
                ["name"] = fsmTexture.Name,
                ["useVariable"] = fsmTexture.UseVariable,
                ["typeName"] = fsmTexture.TypeName
            },
            FsmOwnerDefault fsmOwnerDefault => fsmOwnerDefault.OwnerOption == OwnerDefaultOption.UseOwner
                ? (object?)"[Owner]"  // Simplified representation when using owner
                : new Dictionary<string, object?>
                {
                    ["ownerOption"] = fsmOwnerDefault.OwnerOption.ToString(),
                    ["gameObject"] = fsmOwnerDefault.GameObject != null
                        ? new Dictionary<string, object?>
                        {
                            ["value"] = fsmOwnerDefault.GameObject.Value?.ToString(),
                            ["name"] = fsmOwnerDefault.GameObject.Name,
                            ["useVariable"] = fsmOwnerDefault.GameObject.UseVariable
                        }
                        : null
                },
            FsmObject fsmObject => new Dictionary<string, object?>
            {
                ["value"] = fsmObject.Value?.ToString(),
                ["name"] = fsmObject.Name,
                ["useVariable"] = fsmObject.UseVariable,
                ["typeName"] = fsmObject.TypeName
            },
            FsmVector2 fsmVector2 => new Dictionary<string, object?>
            {
                ["x"] = fsmVector2.Value.X,
                ["y"] = fsmVector2.Value.Y,
                ["name"] = fsmVector2.Name,
                ["useVariable"] = fsmVector2.UseVariable
            },
            FsmVector3 fsmVector3 => new Dictionary<string, object?>
            {
                ["x"] = fsmVector3.Value.X,
                ["y"] = fsmVector3.Value.Y,
                ["z"] = fsmVector3.Value.Z,
                ["name"] = fsmVector3.Name,
                ["useVariable"] = fsmVector3.UseVariable
            },
            FsmRect fsmRect => new Dictionary<string, object?>
            {
                ["x"] = fsmRect.Value.X,
                ["y"] = fsmRect.Value.Y,
                ["width"] = fsmRect.Value.Width,
                ["height"] = fsmRect.Value.Height,
                ["name"] = fsmRect.Name,
                ["useVariable"] = fsmRect.UseVariable
            },
            FsmQuaternion fsmQuaternion => new Dictionary<string, object?>
            {
                ["x"] = fsmQuaternion.Value.X,
                ["y"] = fsmQuaternion.Value.Y,
                ["z"] = fsmQuaternion.Value.Z,
                ["w"] = fsmQuaternion.Value.W,
                ["name"] = fsmQuaternion.Name,
                ["useVariable"] = fsmQuaternion.UseVariable
            },
            FsmColor fsmColor => new Dictionary<string, object?>
            {
                ["r"] = fsmColor.Value.R,
                ["g"] = fsmColor.Value.G,
                ["b"] = fsmColor.Value.B,
                ["a"] = fsmColor.Value.A,
                ["name"] = fsmColor.Name,
                ["useVariable"] = fsmColor.UseVariable
            },
            FsmEnum fsmEnum => new Dictionary<string, object?>
            {
                ["enumName"] = fsmEnum.EnumName,
                ["intValue"] = fsmEnum.IntValue,
                ["name"] = fsmEnum.Name,
                ["useVariable"] = fsmEnum.UseVariable
            },
            FsmVar fsmVar => new Dictionary<string, object?>
            {
                ["variableName"] = fsmVar.VariableName,
                ["varType"] = fsmVar.VarType.ToString(),
                ["useVariable"] = fsmVar.UseVariable,
                ["value"] = GetFsmVarValue(fsmVar)
            },
            FsmEvent fsmEvent => new Dictionary<string, object?>
            {
                ["name"] = fsmEvent.Name,
                ["isSystem"] = fsmEvent.IsSystemEvent,
                ["isGlobal"] = fsmEvent.IsGlobal
            },
            FsmEventTarget fsmEventTarget => new Dictionary<string, object?>
            {
                ["target"] = fsmEventTarget.Target.ToString(),
                ["excludeSelf"] = fsmEventTarget.ExcludeSelf?.Value ?? false,
                ["gameObject"] = fsmEventTarget.GameObject != null
                    ? (fsmEventTarget.GameObject.OwnerOption == OwnerDefaultOption.UseOwner
                        ? (object)"[Owner]"
                        : fsmEventTarget.GameObject.GameObject != null
                            ? new Dictionary<string, object?>
                            {
                                ["value"] = fsmEventTarget.GameObject.GameObject.Value?.ToString(),
                                ["name"] = fsmEventTarget.GameObject.GameObject.Name,
                                ["useVariable"] = fsmEventTarget.GameObject.GameObject.UseVariable
                            }
                            : null)
                    : null,
                ["fsmName"] = fsmEventTarget.FsmName?.Value,
                ["sendToChildren"] = fsmEventTarget.SendToChildren?.Value ?? false
            },
            FsmProperty fsmProperty => new Dictionary<string, object?>
            {
                ["targetObject"] = fsmProperty.TargetObject.ToString(),
                ["targetTypeName"] = fsmProperty.TargetTypeName,
                ["propertyName"] = fsmProperty.PropertyName,
                ["setProperty"] = fsmProperty.SetProperty,
                ["parameters"] = GetPropertyParameters(fsmProperty)
            },
            FsmFunctionCall fsmFunctionCall => new Dictionary<string, object?>
            {
                ["functionName"] = fsmFunctionCall.FunctionName,
                ["parameterType"] = fsmFunctionCall.ParameterType,
                ["parameter"] = GetFunctionCallParameter(fsmFunctionCall)
            },
            FsmArray fsmArray => new Dictionary<string, object?>
            {
                ["name"] = fsmArray.Name,
                ["varType"] = fsmArray.VarType.ToString(),
                ["objectTypeName"] = fsmArray.ObjectTypeName,
                ["length"] = GetArrayLength(fsmArray)
            },
            _ => $"[{valueObj.GetType().Name} not fully implemented]"
        };
    }

    private static object? GetFsmVarValue(FsmVar fsmVar)
    {
        return fsmVar.VarType switch
        {
            VariableType.Float => fsmVar.FloatValue,
            VariableType.Int => fsmVar.IntValue,
            VariableType.Bool => fsmVar.BoolValue,
            VariableType.String => fsmVar.StringValue,
            VariableType.Vector2 or VariableType.Vector3 or VariableType.Color or 
            VariableType.Rect or VariableType.Quaternion => fsmVar.Vector4Value?.ToString(),
            VariableType.Object or VariableType.GameObject or 
            VariableType.Material or VariableType.Texture => fsmVar.ObjectReference?.ToString(),
            VariableType.Array => $"Array[{fsmVar.ArrayValue?.ToString() ?? "null"}]",
            _ => null
        };
    }

    private static object? GetPropertyParameters(FsmProperty prop)
    {
        if (prop.BoolParameter.UseVariable) return new { type = "bool", name = prop.BoolParameter.Name };
        if (prop.FloatParameter.UseVariable) return new { type = "float", name = prop.FloatParameter.Name };
        if (prop.IntParameter.UseVariable) return new { type = "int", name = prop.IntParameter.Name };
        if (prop.GameObjectParameter.UseVariable) return new { type = "GameObject", name = prop.GameObjectParameter.Name };
        if (prop.StringParameter.UseVariable) return new { type = "string", name = prop.StringParameter.Name };
        if (prop.Vector2Parameter.UseVariable) return new { type = "Vector2", name = prop.Vector2Parameter.Name };
        if (prop.Vector3Parameter.UseVariable) return new { type = "Vector3", name = prop.Vector3Parameter.Name };
        if (prop.RectParamater.UseVariable) return new { type = "Rect", name = prop.RectParamater.Name };
        if (prop.QuaternionParameter.UseVariable) return new { type = "Quaternion", name = prop.QuaternionParameter.Name };
        if (prop.ObjectParameter.UseVariable) return new { type = "Object", name = prop.ObjectParameter.Name };
        if (prop.MaterialParameter.UseVariable) return new { type = "Material", name = prop.MaterialParameter.Name };
        if (prop.TextureParameter.UseVariable) return new { type = "Texture", name = prop.TextureParameter.Name };
        if (prop.ColorParameter.UseVariable) return new { type = "Color", name = prop.ColorParameter.Name };
        if (prop.EnumParameter.UseVariable) return new { type = "Enum", name = prop.EnumParameter.Name };
        if (prop.ArrayParameter.UseVariable) return new { type = "Array", name = prop.ArrayParameter.Name };
        return null;
    }

    private static object? GetFunctionCallParameter(FsmFunctionCall call)
    {
        return call.ParameterType switch
        {
            "bool" => call.BoolParameter != null ? new { value = call.BoolParameter.Value, name = call.BoolParameter.Name } : null,
            "float" => call.FloatParameter != null ? new { value = call.FloatParameter.Value, name = call.FloatParameter.Name } : null,
            "int" => call.IntParameter != null ? new { value = call.IntParameter.Value, name = call.IntParameter.Name } : null,
            "GameObject" => call.GameObjectParameter != null ? new { value = call.GameObjectParameter.Value?.ToString(), name = call.GameObjectParameter.Name } : null,
            "string" => call.StringParameter != null ? new { value = call.StringParameter.Value, name = call.StringParameter.Name } : null,
            _ => null
        };
    }

    private static int GetArrayLength(FsmArray array)
    {
        return array.VarType switch
        {
            VariableType.Float => array.FloatValues.Count,
            VariableType.Int => array.IntValues.Count,
            VariableType.Bool => array.BoolValues.Count,
            VariableType.String => array.StringValues.Count,
            VariableType.GameObject or VariableType.Object or 
            VariableType.Material or VariableType.Texture => array.ObjectReferences.Count,
            VariableType.Vector2 or VariableType.Vector3 or VariableType.Color or 
            VariableType.Rect or VariableType.Quaternion => array.Vector4Values.Count,
            _ => 0
        };
    }

    // Export DTOs
    private class DetailedFsmExport
    {
        public string Name { get; set; } = "";
        public string SourceName { get; set; } = "";
        public List<DetailedStateExport> States { get; set; } = [];
        public List<EventExport> Events { get; set; } = [];
        public Dictionary<string, object?> Variables { get; set; } = new();
    }

    private class DetailedStateExport
    {
        public string Name { get; set; } = "";
        public bool IsStart { get; set; }
        public bool IsGlobal { get; set; }
        public PositionExport? Position { get; set; }
        public List<TransitionExport> Transitions { get; set; } = [];
        public List<ActionExport> Actions { get; set; } = [];
    }

    private class GraphFsmExport
    {
        public string Name { get; set; } = "";
        public string SourceName { get; set; } = "";
        public string? StartState { get; set; }
        public List<string> States { get; set; } = [];
        public List<GraphTransitionExport> Transitions { get; set; } = [];
        public List<GraphTransitionExport> GlobalTransitions { get; set; } = [];
        public List<string> Events { get; set; } = [];
        public List<string> GlobalEvents { get; set; } = [];
    }

    private class GraphTransitionExport
    {
        public string From { get; set; } = "";
        public string? To { get; set; }
        public string Event { get; set; } = "";
    }

    private class PositionExport
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    private class TransitionExport
    {
        public string Event { get; set; } = "";
        public string? TargetState { get; set; }
    }

    private class EventExport
    {
        public string Name { get; set; } = "";
        public bool IsSystem { get; set; }
        public bool IsGlobal { get; set; }
    }

    private class ActionExport
    {
        public string ActionType { get; set; } = "";
        public bool IsEnabled { get; set; }
        public Dictionary<string, object?> Parameters { get; set; } = new();
    }
}
