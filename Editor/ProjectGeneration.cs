using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

/// <summary>
/// Generates .csproj and .sln files for the Antigravity IDE.
/// This implementation brute-forces references to ensure all Unity modules are available.
/// </summary>
public static class ProjectGeneration
{
    private const string k_Properties = 
@"  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <OutputType>Library</OutputType>
    <OutputPath>Temp\bin\Debug\</OutputPath>
    <LangVersion>latest</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <EnableDefaultEmbeddedResourceItems>false</EnableDefaultEmbeddedResourceItems>
    <ProjectTypeGuids>{E097FAD1-6243-4DAD-9C02-E9B9EFC3FFC1};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>
  </PropertyGroup>";

    public static void Sync()
    {
        var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
        
        // Scan for all available Unity DLLs once to be efficient
        var allUnityDlls = GetAllUnityDllPaths();

        foreach (var assembly in assemblies)
        {
            GenerateCsproj(assembly, allUnityDlls);
        }
        
        GenerateSolution(assemblies);
        Debug.Log($"[Antigravity] Generated project files for {assemblies.Length} assemblies.");
    }

    public static void SyncIfNeeded(string[] addedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, string[] importedAssets)
    {
        Sync();
    }

    /// <summary>
    /// Scans the Unity Editor installation for all Managed DLLs.
    /// This ensures we catch every module (Physics, UI, Core, etc.) without relying on assembly metadata.
    /// </summary>
    private static HashSet<string> GetAllUnityDllPaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string appContents = EditorApplication.applicationContentsPath;
        
        // Handle potential macOS structure issues if Path.Combine behaves oddly, but applicationContentsPath is usually correct.
        // Windows: .../Editor/Data
        // macOS: .../Unity.app/Contents

        // 1. Managed Root: Contains UnityEngine.dll, UnityEditor.dll
        string managedDir = Path.Combine(appContents, "Managed");
        if (Directory.Exists(managedDir))
        {
            // Get all Unity*.dll (UnityEditor.dll, UnityEngine.dll)
            foreach (var file in Directory.GetFiles(managedDir, "Unity*.dll"))
            {
                paths.Add(file);
            }

            // 2. UnityEngine Modules: Contains UnityEngine.CoreModule.dll, UnityEngine.PhysicsModule.dll, etc.
            string engineDir = Path.Combine(managedDir, "UnityEngine");
            if (Directory.Exists(engineDir))
            {
                foreach (var file in Directory.GetFiles(engineDir, "UnityEngine.*.dll"))
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
        
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        sb.AppendLine(k_Properties);
        sb.AppendLine($"  <PropertyGroup><AssemblyName>{assembly.name}</AssemblyName></PropertyGroup>");
        
        // Defines
        sb.AppendLine($"  <PropertyGroup><DefineConstants>{string.Join(";", assembly.defines)}</DefineConstants></PropertyGroup>");

        sb.AppendLine("  <ItemGroup>");
        
        // 1. Add Explicit Assembly References from Unity's compilation pipeline (Plugins, etc.)
        // We use a HashSet to avoid duplicates if our brute-force list overlaps
        var references = new HashSet<string>(assembly.compiledAssemblyReferences, StringComparer.OrdinalIgnoreCase);
        
        // 2. Merge in ALL Unity DLLs found in the installation folder
        foreach (var dll in allUnityDlls)
        {
            references.Add(dll);
        }

        foreach (var reference in references)
        {
            sb.AppendLine($"    <Reference Include=\"{Path.GetFileNameWithoutExtension(reference)}\">");
            sb.AppendLine($"      <HintPath>{reference.Replace("/", "\\")}</HintPath>");
            sb.AppendLine("    </Reference>");
        }
        sb.AppendLine("  </ItemGroup>");

        // Source Files
        sb.AppendLine("  <ItemGroup>");
        foreach (var sourceFile in assembly.sourceFiles)
        {
            // Ensure path separators are correct for xml
            string safePath = sourceFile.Replace("/", "\\");
            sb.AppendLine($"    <Compile Include=\"{sourceFile}\" />");
        }
        sb.AppendLine("  </ItemGroup>");
        
        // Project References
        sb.AppendLine("  <ItemGroup>");
        foreach (var refAssembly in assembly.assemblyReferences)
        {
             sb.AppendLine($"    <ProjectReference Include=\"{refAssembly.name}.csproj\" />");
        }
        sb.AppendLine("  </ItemGroup>");

        sb.AppendLine("</Project>");

        File.WriteAllText(projectPath, sb.ToString());
    }

    private static void GenerateSolution(Assembly[] assemblies)
    {
        // Standard Solution generation
        // We stick to a standard naming convention based on the directory to avoid "Antigravity.sln" locking issues if preferred,
        // but can be changed if needed. For now generating [FolderName].sln is standard.
        // Update: User requested generic solution, but previous attempt at forced naming failed. 
        // We will stick to dynamic naming which is safest for Unity.
        
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
        
        // Global section to ensure configurations exist can be added here if needed
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
        using (var md5 = MD5.Create())
        {
            byte[] hash = md5.ComputeHash(Encoding.Default.GetBytes(input));
            return new Guid(hash).ToString().ToUpper();
        }
    }
}
