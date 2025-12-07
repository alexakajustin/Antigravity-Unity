using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// Generates .csproj and .sln files for the Antigravity IDE.
/// Uses legacy csproj format for maximum compatibility with C# language servers.
/// Brute-force scans Unity installation for all DLLs.
/// </summary>
public static class ProjectGeneration
{
    public static void Sync()
    {
        var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
        
        // Scan for all available Unity DLLs
        var allUnityDlls = GetAllUnityDllPaths();

        foreach (var assembly in assemblies)
        {
            GenerateCsproj(assembly, allUnityDlls);
        }
        
        GenerateSolution(assemblies);
        Debug.Log($"[Antigravity] Generated project files for {assemblies.Length} assemblies with {allUnityDlls.Count} Unity DLLs.");
    }

    public static void SyncIfNeeded(string[] addedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, string[] importedAssets)
    {
        Sync();
    }

    /// <summary>
    /// Scans the Unity Editor installation for all Managed DLLs.
    /// </summary>
    private static HashSet<string> GetAllUnityDllPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string appContents = EditorApplication.applicationContentsPath;
        
        string managedDir = Path.Combine(appContents, "Managed");
        if (Directory.Exists(managedDir))
        {
            // Get UnityEngine.dll, UnityEditor.dll from root Managed folder
            foreach (var file in Directory.GetFiles(managedDir, "Unity*.dll"))
            {
                paths.Add(file);
            }

            // Get all UnityEngine modules
            string engineDir = Path.Combine(managedDir, "UnityEngine");
            if (Directory.Exists(engineDir))
            {
                foreach (var file in Directory.GetFiles(engineDir, "*.dll"))
                {
                    paths.Add(file);
                }
            }
        }
        else
        {
            Debug.LogError($"[Antigravity] Could not find Managed directory at: {managedDir}");
        }

        return paths;
    }

    private static void GenerateCsproj(Assembly assembly, HashSet<string> allUnityDlls)
    {
        string projectPath = Path.Combine(Directory.GetCurrentDirectory(), $"{assembly.name}.csproj");
        string guid = GenerateGuid(assembly.name);
        
        var sb = new StringBuilder();
        
        // Use LEGACY format - NOT SDK style. This is critical for OmniSharp/language server compatibility.
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<Project ToolsVersion=\"4.0\" DefaultTargets=\"Build\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
        
        // Property Group
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <Configuration Condition=\" '$(Configuration)' == '' \">Debug</Configuration>");
        sb.AppendLine("    <Platform Condition=\" '$(Platform)' == '' \">AnyCPU</Platform>");
        sb.AppendLine("    <ProductVersion>10.0.20506</ProductVersion>");
        sb.AppendLine("    <SchemaVersion>2.0</SchemaVersion>");
        sb.AppendLine($"    <ProjectGuid>{{{guid}}}</ProjectGuid>");
        sb.AppendLine("    <OutputType>Library</OutputType>");
        sb.AppendLine($"    <AssemblyName>{assembly.name}</AssemblyName>");
        sb.AppendLine("    <TargetFrameworkVersion>v4.7.1</TargetFrameworkVersion>");
        sb.AppendLine("    <FileAlignment>512</FileAlignment>");
        sb.AppendLine("    <LangVersion>latest</LangVersion>");
        sb.AppendLine($"    <DefineConstants>{string.Join(";", assembly.defines)}</DefineConstants>");
        sb.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
        sb.AppendLine("  </PropertyGroup>");

        // References - Merge Unity's reported references with brute-force scanned DLLs
        sb.AppendLine("  <ItemGroup>");
        
        var references = new HashSet<string>(assembly.compiledAssemblyReferences, StringComparer.OrdinalIgnoreCase);
        foreach (var dll in allUnityDlls)
        {
            references.Add(dll);
        }

        foreach (var reference in references)
        {
            string refName = Path.GetFileNameWithoutExtension(reference);
            string hintPath = reference.Replace("/", "\\");
            sb.AppendLine($"    <Reference Include=\"{refName}\">");
            sb.AppendLine($"      <HintPath>{hintPath}</HintPath>");
            sb.AppendLine("    </Reference>");
        }
        sb.AppendLine("  </ItemGroup>");

        // Source Files
        sb.AppendLine("  <ItemGroup>");
        foreach (var sourceFile in assembly.sourceFiles)
        {
            string safePath = sourceFile.Replace("/", "\\");
            sb.AppendLine($"    <Compile Include=\"{safePath}\" />");
        }
        sb.AppendLine("  </ItemGroup>");
        
        // Project References
        sb.AppendLine("  <ItemGroup>");
        foreach (var refAssembly in assembly.assemblyReferences)
        {
            string refGuid = GenerateGuid(refAssembly.name);
            sb.AppendLine($"    <ProjectReference Include=\"{refAssembly.name}.csproj\">");
            sb.AppendLine($"      <Project>{{{refGuid}}}</Project>");
            sb.AppendLine($"      <Name>{refAssembly.name}</Name>");
            sb.AppendLine("    </ProjectReference>");
        }
        sb.AppendLine("  </ItemGroup>");

        // MSBuild import - CRITICAL for legacy format
        sb.AppendLine("  <Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" />");
        sb.AppendLine("</Project>");

        File.WriteAllText(projectPath, sb.ToString());
    }

    private static void GenerateSolution(Assembly[] assemblies)
    {
        string solutionPath = Path.Combine(Directory.GetCurrentDirectory(), $"{Path.GetFileName(Directory.GetCurrentDirectory())}.sln");
        var sb = new StringBuilder();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Visual Studio 15");
        
        foreach (var assembly in assemblies)
        {
            string guid = GenerateGuid(assembly.name);
            sb.AppendLine($"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"{assembly.name}\", \"{assembly.name}.csproj\", \"{{{guid}}}\"");
            sb.AppendLine("EndProject");
        }
        
        sb.AppendLine("Global");
        sb.AppendLine("	GlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("		Debug|Any CPU = Debug|Any CPU");
        sb.AppendLine("		Release|Any CPU = Release|Any CPU");
        sb.AppendLine("	EndGlobalSection");
        sb.AppendLine("	GlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var assembly in assemblies)
        {
            string guid = GenerateGuid(assembly.name);
            sb.AppendLine($"		{{{guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            sb.AppendLine($"		{{{guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU");
            sb.AppendLine($"		{{{guid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU");
            sb.AppendLine($"		{{{guid}}}.Release|Any CPU.Build.0 = Release|Any CPU");
        }
        sb.AppendLine("	EndGlobalSection");
        sb.AppendLine("EndGlobal");

        File.WriteAllText(solutionPath, sb.ToString());
    }

    private static string GenerateGuid(string input)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.Default.GetBytes(input));
            return new Guid(hash).ToString().ToUpper();
        }
    }
}
