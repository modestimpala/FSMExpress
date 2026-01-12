using FSMExpress.Common.Document;
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
        return value.DisplayString;
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
