using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

public static class ProjectGeneration
{
    public static void Sync()
    {
        var assemblies = CompilationPipeline.GetAssemblies();
        foreach (var assembly in assemblies)
        {
            GenerateCsproj(assembly);
        }
        GenerateSolution(assemblies);
        Debug.Log($"[Antigravity] Project generation complete. Assets path: {EditorApplication.applicationContentsPath}");
    }

    public static void SyncIfNeeded(string[] addedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, string[] importedAssets)
    {
        Sync();
        // Force sync logic
    }

    private static void GenerateCsproj(Assembly assembly)
    {
        string projectPath = Path.Combine(Directory.GetCurrentDirectory(), $"{assembly.name}.csproj");
        
        var sb = new StringBuilder();
        sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        
        sb.AppendLine("  <PropertyGroup>");
        sb.AppendLine("    <TargetFramework>netstandard2.1</TargetFramework>");
        sb.AppendLine("    <OutputType>Library</OutputType>");
        sb.AppendLine($"    <AssemblyName>{assembly.name}</AssemblyName>");
        sb.AppendLine("    <OutputPath>Temp\\bin\\Debug\\</OutputPath>");
        sb.AppendLine("    <LangVersion>latest</LangVersion>");
        sb.AppendLine($"    <DefineConstants>{string.Join(";", assembly.defines)}</DefineConstants>");
        sb.AppendLine("    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>");
        sb.AppendLine("    <ProjectTypeGuids>{E097FAD1-6243-4DAD-9C02-E9B9EFC3FFC1};{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}</ProjectTypeGuids>");
        sb.AppendLine("  </PropertyGroup>");

        sb.AppendLine("  <ItemGroup>");
        var references = new HashSet<string>(assembly.compiledAssemblyReferences);
        
        string unityEnginePath = Path.Combine(EditorApplication.applicationContentsPath, "Managed", "UnityEngine.dll");
        string unityEditorPath = Path.Combine(EditorApplication.applicationContentsPath, "Managed", "UnityEditor.dll");
        string coreModulePath = Path.Combine(EditorApplication.applicationContentsPath, "Managed", "UnityEngine", "UnityEngine.CoreModule.dll");

        if (!references.Any(r => r.EndsWith("UnityEngine.dll"))) 
        {
             references.Add(unityEnginePath);
        }
        if (!references.Any(r => r.EndsWith("UnityEditor.dll"))) 
        {
             references.Add(unityEditorPath);
        }
        if (!references.Any(r => r.EndsWith("UnityEngine.CoreModule.dll")) && File.Exists(coreModulePath)) 
        {
             references.Add(coreModulePath);
        }

        foreach (var reference in references)
        {
            // Debug Log specific to UnityEngine/CoreModule to verify what path is being used
            if (reference.Contains("UnityEngine") || reference.Contains("UnityEditor"))
            {
                Debug.Log($"[Antigravity] Adding Reference: {reference}");
            }

            sb.AppendLine($"    <Reference Include=\"{Path.GetFileNameWithoutExtension(reference)}\">");
            sb.AppendLine($"      <HintPath>{reference}</HintPath>");
            sb.AppendLine("    </Reference>");
        }
        sb.AppendLine("  </ItemGroup>");

        sb.AppendLine("  <ItemGroup>");
        foreach (var sourceFile in assembly.sourceFiles)
        {
            sb.AppendLine($"    <Compile Include=\"{sourceFile}\" />");
        }
        sb.AppendLine("  </ItemGroup>");
        
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
