// ConfigKit - zero-adoption mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Core.Diagnostics;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace CakeBuild;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

public class BuildContext : FrostingContext
{
    public const string ProjectName = "configkit";
    public string BuildConfiguration { get; }
    public string Version { get; }
    public string Name { get; }
    public bool SkipJsonValidation { get; }

    public BuildContext(ICakeContext context)
        : base(context)
    {
        BuildConfiguration = context.Argument("configuration", "Release");
        SkipJsonValidation = context.Argument("skipJsonValidation", false);
        var modInfo = context.DeserializeJsonFromFile<ModInfo>($"../{ProjectName}/modinfo.json");
        Version = modInfo.Version;
        Name = modInfo.ModID;
    }
}

[TaskName("ValidateJson")]
public sealed class ValidateJsonTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        if (context.SkipJsonValidation)
        {
            return;
        }

        var jsonFiles = context.GetFiles($"../{BuildContext.ProjectName}/assets/**/*.json");
        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file.FullPath);
                JToken.Parse(json);
            }
            catch (JsonException ex)
            {
                throw new Exception(
                    $"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
            }
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.DotNetClean($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
            new DotNetCleanSettings
            {
                Configuration = context.BuildConfiguration
            });


        context.DotNetPublish($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
            new DotNetPublishSettings
            {
                Configuration = context.BuildConfiguration
            });
    }
}

[TaskName("VerifyDependencies")]
[IsDependentOn(typeof(BuildTask))]
public sealed class VerifyDependenciesTask : FrostingTask<BuildContext>
{
    // ConfigLib's payload was a class called YamlDotNet.Serialization.SettingsBuilder,
    // injected at build time into configlib.dll - where ILRepack had already merged 387
    // real YamlDotNet types, so one more in that namespace drew no attention, and it was
    // in no commit of the public repo.
    //
    // ConfigKit ships its dependencies as separate, unmodified files precisely so its own
    // assembly contains nothing but its own code. These three checks make that a property
    // of the build rather than of anyone's memory:
    //
    //   1. every type in ConfigKit.dll belongs to a namespace we own,
    //   2. every third-party dll matches the hash of its official published build,
    //   3. every third-party dll ships with its licence.
    //
    // Check 1 would have caught ConfigLib's payload the day it shipped.

    private static readonly string[] OwnNamespacePrefixes =
    {
        "ConfigKit",
        "SimpleExpressionEngine"
    };

    // Emitted by the compiler rather than written by us. Listed exactly, so a new one
    // fails the build and gets added deliberately instead of silently widening the net.
    private static readonly string[] CompilerNamespaces =
    {
        "System.Runtime.CompilerServices",
        "System.Diagnostics.CodeAnalysis",
        "Microsoft.CodeAnalysis",
        "System.Text.RegularExpressions.Generated"
    };

    // sha256 of the official NuGet build. YamlDotNet 13.7.1, lib/net7.0/YamlDotNet.dll.
    private static readonly Dictionary<string, string> PinnedHashes = new()
    {
        ["YamlDotNet.dll"] = "a08b5c6e543b58a807ca3b498e7c86792aa21626379ceb0588c532e185cd07d1"
    };

    public override void Run(BuildContext context)
    {
        string publishDir = $"../{BuildContext.ProjectName}/bin/{context.BuildConfiguration}/Mods/mod/publish";
        if (!Directory.Exists(publishDir))
        {
            publishDir = $"../{BuildContext.ProjectName}/bin/{context.BuildConfiguration}/Mods/mod";
        }

        VerifyOwnAssembly(context, Path.Combine(publishDir, "ConfigKit.dll"));
        VerifyThirdPartyAssemblies(context, publishDir);
    }

    private static void VerifyOwnAssembly(BuildContext context, string path)
    {
        var foreign = new List<string>();

        using (FileStream stream = File.OpenRead(path))
        using (var reader = new PEReader(stream))
        {
            MetadataReader metadata = reader.GetMetadataReader();
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(handle);
                string ns = metadata.GetString(type.Namespace);

                // Nested and compiler-generated types carry no namespace of their own.
                if (ns.Length == 0) continue;

                bool ours = OwnNamespacePrefixes.Any(prefix => ns == prefix || ns.StartsWith(prefix + "."));
                bool generated = CompilerNamespaces.Any(prefix => ns == prefix || ns.StartsWith(prefix + "."));

                if (!ours && !generated) foreign.Add($"{ns}.{metadata.GetString(type.Name)}");
            }
        }

        if (foreign.Count > 0)
        {
            throw new Exception(
                "ConfigKit.dll declares types outside its own namespaces:" + Environment.NewLine
              + string.Join(Environment.NewLine, foreign.Distinct().OrderBy(name => name)) + Environment.NewLine
              + "Dependencies must ship as their own files, never merged in - see the comment "
              + "on VerifyDependenciesTask.");
        }

        context.Log.Information("ConfigKit.dll declares only ConfigKit/SimpleExpressionEngine types.");
    }

    private static void VerifyThirdPartyAssemblies(BuildContext context, string publishDir)
    {
        foreach (string dll in Directory.GetFiles(publishDir, "*.dll").OrderBy(path => path))
        {
            string name = Path.GetFileName(dll);
            if (name == "ConfigKit.dll") continue;

            if (!PinnedHashes.TryGetValue(name, out string expected))
            {
                throw new Exception(
                    $"'{name}' ships in the mod but has no pinned hash. Add its sha256 and its "
                  + "licence, or stop shipping it.");
            }

            string actual = Sha256(dll);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception(
                    $"'{name}' does not match its pinned hash." + Environment.NewLine
                  + $"  expected {expected}" + Environment.NewLine
                  + $"  actual   {actual}" + Environment.NewLine
                  + "This must be the unmodified assembly its publisher released.");
            }

            string licence = $"../{BuildContext.ProjectName}/licenses/{Path.GetFileNameWithoutExtension(name)}-LICENSE.txt";
            if (!File.Exists(licence))
            {
                throw new Exception($"'{name}' ships without a licence file; expected {licence}.");
            }

            context.Log.Information($"{name}: hash pinned, licence present.");
        }
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(VerifyDependenciesTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.EnsureDirectoryExists("../Releases");
        context.CleanDirectory("../Releases");
        context.EnsureDirectoryExists($"../Releases/{context.Name}");
        context.CopyFiles($"../{BuildContext.ProjectName}/bin/{context.BuildConfiguration}/Mods/mod/publish/*",
            $"../Releases/{context.Name}");
        if (context.DirectoryExists($"../{BuildContext.ProjectName}/assets"))
        {
            context.CopyDirectory($"../{BuildContext.ProjectName}/assets", $"../Releases/{context.Name}/assets");
        }

        context.CopyDirectory($"../{BuildContext.ProjectName}/licenses", $"../Releases/{context.Name}/licenses");
        context.CopyFile("../LICENSE", $"../Releases/{context.Name}/LICENSE");

        context.CopyFile($"../{BuildContext.ProjectName}/modinfo.json", $"../Releases/{context.Name}/modinfo.json");
        if (context.FileExists($"../{BuildContext.ProjectName}/modicon.png"))
        {
            context.CopyFile($"../{BuildContext.ProjectName}/modicon.png", $"../Releases/{context.Name}/modicon.png");
        }

        context.Zip($"../Releases/{context.Name}", $"../Releases/{context.Name}_{context.Version}.zip");
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask
{
}
