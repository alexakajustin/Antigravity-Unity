using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.CodeEditor;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class AntigravityScriptEditor : IExternalCodeEditor
{
    const string EditorName = "Antigravity";
    static readonly string[] KnownPaths =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Antigravity", "Antigravity.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Antigravity.exe"),
        "/Applications/Antigravity.app/Contents/MacOS/Antigravity"
    };

    /// <summary>
    /// File extensions that Antigravity should open. Everything else (prefabs, scenes,
    /// materials, animations, etc.) is left to Unity's native asset handling.
    /// </summary>
    static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Code
        ".cs", ".js", ".ts", ".boo",
        // Shaders
        ".shader", ".compute", ".cginc", ".hlsl", ".glsl", ".cg",
        // Shader graphs / sub-graphs (text-based)
        ".shadergraph", ".shadersubgraph",
        // Data / config
        ".json", ".xml", ".yaml", ".yml", ".txt", ".md", ".csv", ".tsv",
        // USS / UXML (UI Toolkit)
        ".uss", ".uxml",
        // Assembly definitions
        ".asmdef", ".asmref",
        // Miscellaneous text
        ".cfg", ".ini", ".log", ".rsp", ".editorconfig",
    };

    static AntigravityScriptEditor()
    {
        CodeEditor.Register(new AntigravityScriptEditor());
    }

    private static bool IsAntigravityInstalled()
    {
        return KnownPaths.Any(p => File.Exists(p) || Directory.Exists(p));
    }

    /// <summary>
    /// Resolves the actual executable inside a macOS .app bundle.
    /// </summary>
    private static string GetExecutablePath(string path)
    {
        if (path.EndsWith(".app"))
        {
            string executable = Path.Combine(path, "Contents", "MacOS", "Antigravity");
            return File.Exists(executable) ? executable : path;
        }
        return path;
    }

    /// <summary>
    /// Returns true if the given file path is a code/text file that Antigravity should handle.
    /// </summary>
    private static bool IsSupportedFile(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        string ext = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
    }

    public CodeEditor.Installation[] Installations
    {
        get
        {
            var installations = new List<CodeEditor.Installation>();
            foreach (var path in KnownPaths)
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    installations.Add(new CodeEditor.Installation
                    {
                        Name = EditorName,
                        Path = path
                    });
                }
            }
            return installations.ToArray();
        }
    }

    public void Initialize(string editorInstallationPath)
    {
        // Perform any initialization here
    }

    public void OnGUI()
    {
        // Custom GUI for Preferences > External Tools
        GUILayout.Label("Antigravity IDE Settings", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Regenerate Project Files"))
        {
            SyncAll();
        }
    }

    public bool OpenProject(string filePath, int line, int column)
    {
        // Only handle code / text files. Return false for Unity-native assets
        // (.prefab, .unity, .asset, .mat, .anim, .controller, .png, etc.)
        // so Unity opens them with its own editors.
        if (!string.IsNullOrEmpty(filePath) && !Directory.Exists(filePath) && !IsSupportedFile(filePath))
        {
            return false;
        }

        string installation = CodeEditor.CurrentEditorInstallation;
        
        string arguments;
        if (Directory.Exists(filePath))
        {
            arguments = $"\"{filePath}\"";
        }
        else
        {
            // Open the project root directory alongside the specific file
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            arguments = $"\"{projectRoot}\" \"{filePath}\"";
        }

        try
        {
            Process process = new Process();
            
            // Handle macOS .app bundles specifically
            if (installation.EndsWith(".app") && Application.platform == RuntimePlatform.OSXEditor)
            {
                process.StartInfo.FileName = "/usr/bin/open";
                process.StartInfo.Arguments = $"-a \"{installation}\" -n --args {arguments}";
            }
            else
            {
                process.StartInfo.FileName = GetExecutablePath(installation);
                process.StartInfo.Arguments = arguments;
            }

            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;
            process.Start();
            return true;
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"Failed to open Antigravity: {e.Message}");
            return false;
        }
    }

    public void SyncAll()
    {
        ProjectGeneration.Sync();
    }

    public void SyncIfNeeded(string[] addedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths, string[] importedAssets)
    {
        ProjectGeneration.SyncIfNeeded(addedAssets, deletedAssets, movedAssets, movedFromAssetPaths, importedAssets);
    }

    public bool TryGetInstallationForPath(string editorPath, out CodeEditor.Installation installation)
    {
        if (editorPath.Contains("Antigravity"))
        {
            installation = new CodeEditor.Installation
            {
                Name = EditorName,
                Path = editorPath
            };
            return true;
        }

        installation = default;
        return false;
    }
}

