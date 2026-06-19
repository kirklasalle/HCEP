// ──────────────────────────────────────────────────────────────
// HCEP — Human Communication Eye Protocol
// Copyright © 2026 Kirk LaSalle. All rights reserved.
// 
// PROPRIETARY & TRADE SECRET NOTICE:
// This source code and associated documentation (including the HCEP
// Theory, the engineering implementation, the supported mathematical
// formulations, the Permanent Active Directives (PAD), and the Body
// Language Protocols) contain proprietary and trade secret assets
// owned exclusively by Kirk LaSalle. Unauthorized use, copying,
// modification, or distribution is strictly prohibited.
// ──────────────────────────────────────────────────────────────
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using HCEP.Core.Interfaces;

namespace HCEP.Plugin.Api.Llm;

/// <summary>
/// Adapters to expose HCEP data to LLMs via standard patterns:
/// OpenAI Function Calling and Anthropic Model Context Protocol (MCP).
/// </summary>
public static class HcepLlmAdapters
{
    /// <summary>
    /// Returns the OpenAI-compatible Function Tool Schema for HCEP.
    /// </summary>
    public static object GetOpenAiSchema()
    {
        return new
        {
            type = "function",
            function = new
            {
                name = "get_hcep_state",
                description = "Queries HCEP (Human Communication Eye Protocol) for the active user's real-time gaze, attention target, identity name, and inferred cognitive-emotional mode (e.g. Logic, Affect, Spirit, Heart, Think).",
                parameters = new
                {
                    type = "object",
                    properties = new { }
                }
            }
        };
    }

    /// <summary>
    /// Processes a Model Context Protocol (MCP) JSON-RPC request.
    /// Supports 'tools/list' and 'tools/call' methods.
    /// </summary>
    public static object HandleMcpRequest(string requestJson, IPipelineOrchestrator orchestrator, Func<HCEP.Core.Models.SceneSnapshot?, object> mapToDto)
    {
        try
        {
            var node = JsonNode.Parse(requestJson);
            if (node == null)
            {
                return CreateMcpError(null, -32700, "Parse error");
            }

            var idNode = node["id"];
            var methodNode = node["method"];

            if (methodNode == null)
            {
                return CreateMcpError(idNode, -32600, "Invalid Request");
            }

            string method = methodNode.GetValue<string>();

            switch (method)
            {
                case "tools/list":
                    return new
                    {
                        jsonrpc = "2.0",
                        result = new
                        {
                            tools = new[]
                            {
                                new
                                {
                                    name = "get_hcep_state",
                                    description = "Get the current HCEP (Human Communication Eye Protocol) multi-modal state (tracking, identity, head position, and gaze/cognitive mode classification).",
                                    inputSchema = new
                                    {
                                        type = "object",
                                        properties = new { }
                                    }
                                }
                            }
                        },
                        id = idNode?.Deserialize<object>()
                    };

                case "tools/call":
                    var paramsNode = node["params"];
                    if (paramsNode == null || paramsNode["name"] == null)
                    {
                        return CreateMcpError(idNode, -32602, "Invalid params");
                    }

                    string toolName = paramsNode["name"]!.GetValue<string>();
                    if (toolName != "get_hcep_state")
                    {
                        return CreateMcpError(idNode, -32601, $"Tool not found: {toolName}");
                    }

                    // Get current snapshot and map it
                    var snapshot = orchestrator.LatestSnapshot;
                    var dto = mapToDto(snapshot);
                    string stateJson = JsonSerializer.Serialize(dto);

                    return new
                    {
                        jsonrpc = "2.0",
                        result = new
                        {
                            content = new[]
                            {
                                new
                                {
                                    type = "text",
                                    text = stateJson
                                }
                            }
                        },
                        id = idNode?.Deserialize<object>()
                    };

                default:
                    return CreateMcpError(idNode, -32601, $"Method not found: {method}");
            }
        }
        catch (Exception ex)
        {
            return CreateMcpError(null, -32000, $"Internal error: {ex.Message}");
        }
    }

    private static object CreateMcpError(JsonNode? idNode, int code, string message)
    {
        return new
        {
            jsonrpc = "2.0",
            error = new
            {
                code = code,
                message = message
            },
            id = idNode?.Deserialize<object>()
        };
    }
}
