using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CompositorWin.Core;

namespace CompositorWin.Mcp;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private static async Task Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--dehaze")
        {
            var path = Path.GetFullPath(args[1]);
            Console.WriteLine(DocumentService.Dehaze(path));
            AgentBridge.NotifyOpen(path);
            return;
        }

        if (args.Length >= 2 && args[0] == "--contrast")
        {
            var path = Path.GetFullPath(args[1]);
            var doc = ProjectStore.Load(path);
            ProjectStore.AddAdjustmentLayer(doc, ProjectStore.EnsureIdentityBlocks(new AdjustmentRecord
            {
                Kind = "Curves",
                Curves = new CurvesSettings
                {
                    Channels =
                    [
                        [new CurvePoint { X = 0, Y = 0 }, new() { X = 64, Y = 48 }, new() { X = 192, Y = 214 }, new() { X = 255, Y = 255 }],
                        [new CurvePoint { X = 0, Y = 0 }, new() { X = 255, Y = 255 }],
                        [new CurvePoint { X = 0, Y = 0 }, new() { X = 255, Y = 255 }],
                        [new CurvePoint { X = 0, Y = 0 }, new() { X = 255, Y = 255 }]
                    ]
                }
            }), "去灰-对比");
            AgentBridge.NotifyReload(path);
            AgentBridge.NotifyOpen(path);
            Console.WriteLine("contrast curve applied");
            return;
        }

        if (args.Length >= 2 && args[0] == "--focus-sea")
        {
            var path = Path.GetFullPath(args[1]);
            Console.WriteLine(DocumentService.RefineSeaFocus(path));
            AgentBridge.NotifyOpen(path);
            return;
        }

        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;
        using var stdin = Console.OpenStandardInput();
        using var reader = new StreamReader(stdin, Encoding.UTF8);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonNode? node;
            try
            {
                node = JsonNode.Parse(line);
            }
            catch
            {
                continue;
            }

            if (node is null)
            {
                continue;
            }

            var id = node["id"];
            var method = node["method"]?.GetValue<string>();
            try
            {
                object? result = method switch
                {
                    "initialize" => Initialize(),
                    "notifications/initialized" => null,
                    "tools/list" => new { tools = Tools.List() },
                    "tools/call" => Tools.Call(node["params"]!),
                    "ping" => new { },
                    _ => throw new InvalidOperationException("Unknown method: " + method)
                };
                if (method == "notifications/initialized" || id is null)
                {
                    continue;
                }

                Write(new { jsonrpc = "2.0", id, result });
            }
            catch (Exception ex)
            {
                if (id is null)
                {
                    continue;
                }

                Write(new
                {
                    jsonrpc = "2.0",
                    id,
                    error = new { code = -32000, message = ex.Message }
                });
            }
        }
    }

    private static object Initialize() => new
    {
        protocolVersion = "2024-11-05",
        capabilities = new { tools = new { } },
        serverInfo = new { name = "compositorwin", version = "0.1.0" }
    };

    private static void Write(object payload)
    {
        var json = JsonSerializer.Serialize(payload, Json);
        Console.WriteLine(json);
        Console.Out.Flush();
    }
}

internal static class Tools
{
    public static object[] List() =>
    [
        Tool("create_project", "Create a new .comp document package.", new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "Folder path ending in .comp" },
                width = new { type = "integer" },
                height = new { type = "integer" },
                background = new { type = "string", description = "Optional #RRGGBB fill" }
            },
            required = new[] { "path", "width", "height" }
        }),
        Tool("import_image", "Import an image file as a new layer.", Schema("path", "imagePath", "name")),
        Tool("inspect_project", "Inspect canvas size and every layer.", Schema("path")),
        Tool("list_layers", "List layers top-to-bottom like the Layers panel.", Schema("path")),
        Tool("render_preview", "Flattened PNG preview for the model to see.", Schema("path", "maxEdge")),
        Tool("analyze_image", "Histogram, mean hue/saturation/lightness.", Schema("path")),
        Tool("add_adjustment", "Add a non-destructive adjustment layer.", new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string" },
                kind = new { type = "string", description = "Hue/Saturation, Levels, Curves, Exposure, Black & White, Color Balance, Invert" },
                name = new { type = "string" },
                hue = new { type = "number" },
                saturation = new { type = "number" },
                lightness = new { type = "number" },
                colorize = new { type = "boolean" },
                exposure = new { type = "number" },
                offset = new { type = "number" },
                gamma = new { type = "number" },
                midCyanRed = new { type = "number" },
                midMagentaGreen = new { type = "number" },
                midYellowBlue = new { type = "number" }
            },
            required = new[] { "path", "kind" }
        }),
        Tool("set_adjustment", "Replace settings on an existing adjustment layer.", new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string" },
                layerId = new { type = "string" },
                kind = new { type = "string" },
                hue = new { type = "number" },
                saturation = new { type = "number" },
                lightness = new { type = "number" }
            },
            required = new[] { "path", "layerId", "kind" }
        }),
        Tool("create_hue_mask", "Build a grayscale mask from a hue range (e.g. seawater).", new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string" },
                layerId = new { type = "string", description = "Layer that receives the mask; default last adjustment" },
                centerHue = new { type = "number", description = "0-360, cyan-blue water is often 175-210" },
                range = new { type = "number" },
                softness = new { type = "number" }
            },
            required = new[] { "path", "centerHue" }
        }),
        Tool("set_layer", "Rename, hide, opacity, blend, position, size, rotation.", new
        {
            type = "object",
            additionalProperties = true,
            properties = new { path = new { type = "string" }, layerId = new { type = "string" } },
            required = new[] { "path", "layerId" }
        }),
        Tool("export_flattened", "Export merged PNG or JPEG.", Schema("path", "outputPath", "format")),
        Tool("open_in_app", "Launch or focus CompositorWin on this .comp so the UI updates live.", Schema("path")),
        Tool("crop", "Crop the document.", new { type = "object", properties = new { path = new { type = "string" }, x = new { type = "integer" }, y = new { type = "integer" }, width = new { type = "integer" }, height = new { type = "integer" } }, required = new[] { "path", "x", "y", "width", "height" } }),
        Tool("resize_canvas", "Change canvas size.", Schema("path", "width", "height", "anchor")),
        Tool("resize_image", "Resample the whole document.", Schema("path", "width", "height")),
        Tool("add_text", "Add an editable text layer.", Schema("path", "content", "x", "y")),
        Tool("add_shape", "Add a rectangle/ellipse/line shape layer.", Schema("path", "kind", "x", "y", "width", "height")),
        Tool("add_group", "Add a layer folder.", Schema("path", "name")),
        Tool("clip_to_below", "Clip the layer to the one beneath it.", Schema("path", "layerId")),
        Tool("remove_background", "Knock out background similar to corner color.", Schema("path", "layerId")),
        Tool("subject_mask", "Build a subject mask (non-background).", Schema("path", "layerId")),
        Tool("import_psd", "Import flattened 8-bit PSD/PSB composite.", Schema("path", "psdPath")),
        Tool("add_drop_shadow", "Enable drop shadow on a layer.", Schema("path", "layerId")),
        Tool("camera_raw", "Apply Camera Raw-style settings on a layer.", new { type = "object", properties = new { path = new { type = "string" }, layerId = new { type = "string" }, exposure = new { type = "number" }, contrast = new { type = "number" }, vibrance = new { type = "number" }, temperature = new { type = "number" } }, required = new[] { "path", "layerId" } })
    ];

    public static object Call(JsonNode parameters)
    {
        var name = parameters["name"]?.GetValue<string>() ?? "";
        var args = parameters["arguments"] as JsonObject ?? [];
        string S(string key, string fallback = "") => args[key]?.GetValue<string>() ?? fallback;
        int I(string key, int fallback) => args[key] is null ? fallback : args[key]!.GetValue<int>();
        double D(string key, double fallback) => args[key] is null ? fallback : args[key]!.GetValue<double>();

        switch (name)
        {
            case "create_project":
                {
                    var doc = DocumentService.CreateProject(S("path"), I("width", 1920), I("height", 1080), string.IsNullOrWhiteSpace(S("background")) ? null : S("background"));
                    return Text(JsonSerializer.Serialize(DocumentService.Inspect(doc.PackagePath)));
                }
            case "import_image":
                {
                    var layer = DocumentService.ImportImage(S("path"), S("imagePath"), string.IsNullOrWhiteSpace(S("name")) ? null : S("name"));
                    return Text(JsonSerializer.Serialize(DocumentService.DescribeLayer(layer)));
                }
            case "inspect_project":
                return Text(JsonSerializer.Serialize(DocumentService.Inspect(S("path"))));
            case "list_layers":
                return Text(JsonSerializer.Serialize(DocumentService.ListLayers(S("path"))));
            case "render_preview":
                {
                    var png = DocumentService.RenderPreviewPng(S("path"), I("maxEdge", 800));
                    return new
                    {
                        content = new object[]
                        {
                            new { type = "image", data = Convert.ToBase64String(png), mimeType = "image/png" },
                            new { type = "text", text = "Flattened preview of " + S("path") }
                        }
                    };
                }
            case "analyze_image":
                {
                    var stats = DocumentService.Analyze(S("path"));
                    return Text(JsonSerializer.Serialize(stats));
                }
            case "add_adjustment":
                {
                    var adj = BuildAdjustment(args);
                    var layer = DocumentService.AddAdjustment(S("path"), adj, string.IsNullOrWhiteSpace(S("name")) ? null : S("name"));
                    return Text(JsonSerializer.Serialize(DocumentService.DescribeLayer(layer)));
                }
            case "set_adjustment":
                {
                    var adj = BuildAdjustment(args);
                    var layer = DocumentService.SetAdjustment(S("path"), S("layerId"), adj);
                    return Text(JsonSerializer.Serialize(DocumentService.DescribeLayer(layer)));
                }
            case "create_hue_mask":
                {
                    var layer = DocumentService.CreateHueMask(S("path"), string.IsNullOrWhiteSpace(S("layerId")) ? null : S("layerId"), D("centerHue", 190), D("range", 28), D("softness", 18));
                    return Text("Attached hue mask to " + layer.Id + " (" + layer.Name + ")");
                }
            case "set_layer":
                {
                    var patchJson = args.ToJsonString();
                    using var patch = JsonDocument.Parse(patchJson);
                    var layer = DocumentService.SetLayer(S("path"), S("layerId"), patch.RootElement);
                    return Text(JsonSerializer.Serialize(DocumentService.DescribeLayer(layer)));
                }
            case "export_flattened":
                DocumentService.ExportFlattened(S("path"), S("outputPath"), string.IsNullOrWhiteSpace(S("format")) ? "png" : S("format"));
                return Text("Exported " + S("outputPath"));
            case "open_in_app":
                return Text(AppLauncher.Open(S("path")));
            case "crop":
                {
                    var doc = ProjectStore.Load(S("path"));
                    CanvasOps.Crop(doc, I("x", 0), I("y", 0), I("width", 100), I("height", 100));
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text("cropped");
                }
            case "resize_canvas":
                {
                    var doc = ProjectStore.Load(S("path"));
                    CanvasOps.ResizeCanvas(doc, I("width", doc.Manifest.Width), I("height", doc.Manifest.Height), string.IsNullOrWhiteSpace(S("anchor")) ? "center" : S("anchor"));
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text("canvas resized");
                }
            case "resize_image":
                {
                    var doc = ProjectStore.Load(S("path"));
                    CanvasOps.ResizeImage(doc, I("width", doc.Manifest.Width), I("height", doc.Manifest.Height));
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text("image resized");
                }
            case "add_text":
                {
                    var doc = ProjectStore.Load(S("path"));
                    var layer = ProjectStore.AddTextLayer(doc, new TextRecord { Content = S("content") }, D("x", 40), D("y", 40), 480, 140);
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text(JsonSerializer.Serialize(DocumentService.DescribeLayer(layer)));
                }
            case "add_shape":
                {
                    var doc = ProjectStore.Load(S("path"));
                    var layer = ProjectStore.AddShapeLayer(doc, new ShapeRecord { Kind = string.IsNullOrWhiteSpace(S("kind")) ? "rectangle" : S("kind") }, D("x", 20), D("y", 20), D("width", 200), D("height", 120));
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text(JsonSerializer.Serialize(DocumentService.DescribeLayer(layer)));
                }
            case "add_group":
                {
                    var doc = ProjectStore.Load(S("path"));
                    var layer = ProjectStore.AddGroup(doc, string.IsNullOrWhiteSpace(S("name")) ? "Group" : S("name"));
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text(layer.Id);
                }
            case "clip_to_below":
                {
                    var doc = ProjectStore.Load(S("path"));
                    var layer = ProjectStore.FindLayer(doc, S("layerId"));
                    var i = doc.Manifest.Layers.FindIndex(l => l.Id == layer.Id);
                    if (i > 0) layer.MaskSourceId = doc.Manifest.Layers[i - 1].Id;
                    ProjectStore.Save(doc);
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text("clipped");
                }
            case "remove_background":
                {
                    var doc = ProjectStore.Load(S("path"));
                    var layer = ProjectStore.FindLayer(doc, S("layerId"));
                    using var px = ProjectStore.LoadLayerPixels(doc, layer);
                    using var nb = PixelTools.RemoveBackground(px);
                    ProjectStore.SaveLayerPixels(doc, layer, nb);
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text("background removed");
                }
            case "subject_mask":
                {
                    var doc = ProjectStore.Load(S("path"));
                    var layer = ProjectStore.FindLayer(doc, S("layerId"));
                    using var px = ProjectStore.LoadLayerPixels(doc, layer);
                    using var mask = PixelTools.SubjectMask(px);
                    ProjectStore.SetLayerMask(doc, layer.Id, mask);
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text("subject mask applied");
                }
            case "import_psd":
                {
                    var doc = ProjectStore.Load(S("path"));
                    var layer = PsdImport.ImportFlattened(doc, S("psdPath"));
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text(layer.Id);
                }
            case "add_drop_shadow":
                {
                    var doc = ProjectStore.Load(S("path"));
                    var layer = ProjectStore.FindLayer(doc, S("layerId"));
                    layer.Effects ??= new EffectsRecord();
                    layer.Effects.Shadow = new EffectShadow();
                    ProjectStore.Save(doc);
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text("shadow added");
                }
            case "camera_raw":
                {
                    var doc = ProjectStore.Load(S("path"));
                    var layer = ProjectStore.FindLayer(doc, S("layerId"));
                    layer.CameraRaw = new CameraRawRecord
                    {
                        Exposure = D("exposure", 0),
                        Contrast = D("contrast", 0),
                        Vibrance = D("vibrance", 0),
                        Temperature = D("temperature", 0)
                    };
                    ProjectStore.Save(doc);
                    AgentBridge.NotifyReload(doc.PackagePath);
                    return Text("camera raw set");
                }
            default:
                throw new InvalidOperationException("Unknown tool: " + name);
        }
    }

    private static AdjustmentRecord BuildAdjustment(JsonObject args)
    {
        double D(string key, double fallback) => args[key] is null ? fallback : args[key]!.GetValue<double>();
        var kind = args["kind"]?.GetValue<string>() ?? "Hue/Saturation";
        return ProjectStore.EnsureIdentityBlocks(new AdjustmentRecord
        {
            Kind = kind,
            Hue = D("hue", 0),
            Saturation = D("saturation", 0),
            Lightness = D("lightness", 0),
            Colorize = args["colorize"]?.GetValue<bool>() ?? false,
            Exposure = new ExposureSettings
            {
                Exposure = D("exposure", 0),
                Offset = D("offset", 0),
                Gamma = D("gamma", 1)
            },
            ColorBalance = new ColorBalanceSettings
            {
                MidCyanRed = D("midCyanRed", 0),
                MidMagentaGreen = D("midMagentaGreen", 0),
                MidYellowBlue = D("midYellowBlue", 0)
            }
        });
    }

    private static object Tool(string name, string description, object schema) => new
    {
        name,
        description,
        inputSchema = schema
    };

    private static object Schema(params string[] names)
    {
        var props = names.ToDictionary(n => n, _ => new { type = "string" });
        return new { type = "object", properties = props, required = new[] { names[0] } };
    }

    private static object Text(string text) => new { content = new object[] { new { type = "text", text } } };
}

internal static class AppLauncher
{
    public static string Open(string packagePath)
    {
        packagePath = Path.GetFullPath(packagePath);
        if (AgentBridge.Ping())
        {
            AgentBridge.NotifyOpen(packagePath);
            AgentBridge.NotifyReload(packagePath);
            return "Notified running CompositorWin to open " + packagePath;
        }

        var exe = FindAppExe();
        if (exe is null)
        {
            return "CompositorWin is not running and the app exe was not found. Start src/CompositorWin then retry.";
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            Arguments = "\"" + packagePath + "\"",
            UseShellExecute = true
        });
        return "Launched CompositorWin with " + packagePath;
    }

    private static string? FindAppExe()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "CompositorWin.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            var debug = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "CompositorWin", "bin", "Debug", "net8.0-windows", "CompositorWin.exe"));
            if (File.Exists(debug))
            {
                return debug;
            }

            var release = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", "CompositorWin", "bin", "Release", "net8.0-windows", "CompositorWin.exe"));
            if (File.Exists(release))
            {
                return release;
            }

            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }

        return null;
    }
}
