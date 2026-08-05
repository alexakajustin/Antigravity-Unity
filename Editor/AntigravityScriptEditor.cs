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
    const string EditorName = "Antigravity IDE";

    /// <summary>
    /// Relative paths to check under each drive root and special folder.
    /// Covers both old ("Antigravity") and new ("Antigravity IDE") naming.
    /// </summary>
    static readonly string[] RelativeInstallPaths =
    {
        Path.Combine("Antigravity IDE", "Antigravity IDE.exe"),
        Path.Combine("Antigravity", "Antigravity.exe"),
        Path.Combine("Antigravity", "Antigravity IDE.exe"),
        Path.Combine("Programs", "Antigravity IDE", "Antigravity IDE.exe"),
        Path.Combine("Programs", "Antigravity", "Antigravity.exe"),
    };

    /// <summary>
    /// Absolute paths for macOS and Linux.
    /// </summary>
    static readonly string[] PlatformPaths =
    {
        "/Applications/Antigravity IDE.app",
        "/Applications/Antigravity.app",
        "/usr/local/bin/antigravity-ide",
        "/usr/local/bin/antigravity",
        "/usr/bin/antigravity-ide",
        "/usr/bin/antigravity"
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

    // Cache discovered installations so we don't scan drives on every access
    static string[] s_cachedPaths;
    static double s_lastDiscoveryTime = -1;
    const double CacheLifetimeSeconds = 60.0;

    static AntigravityScriptEditor()
    {
        CodeEditor.Register(new AntigravityScriptEditor());
    }

    /// <summary>
    /// Discovers all Antigravity IDE installations by scanning special folders,
    /// all fixed drives (Windows), and platform-specific paths (macOS/Linux).
    /// Results are cached for 60 seconds to avoid repeated disk I/O.
    /// </summary>
    private static string[] DiscoverInstallationPaths()
    {
        double now = EditorApplication.timeSinceStartup;
        if (s_cachedPaths != null && (now - s_lastDiscoveryTime) < CacheLifetimeSeconds)
        {
            return s_cachedPaths;
        }

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // --- Windows: check special folders ---
        var specialFolders = new[]
        {
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ProgramFiles,
#if !UNITY_EDITOR_OSX && !UNITY_EDITOR_LINUX
            Environment.SpecialFolder.ProgramFilesX86,
#endif
        };

        foreach (var folder in specialFolders)
        {
            string folderPath = Environment.GetFolderPath(folder);
            if (string.IsNullOrEmpty(folderPath)) continue;

            foreach (var relative in RelativeInstallPaths)
            {
                string fullPath = Path.Combine(folderPath, relative);
                if (File.Exists(fullPath))
                {
                    found.Add(fullPath);
                }
            }
        }

        // --- Windows: scan ALL fixed drive roots (covers D:\, G:\, etc.) ---
        if (Application.platform == RuntimePlatform.WindowsEditor)
        {
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                    foreach (var relative in RelativeInstallPaths)
                    {
                        string fullPath = Path.Combine(drive.RootDirectory.FullName, relative);
                        if (File.Exists(fullPath))
                        {
                            found.Add(fullPath);
                        }
                    }
                }
            }
            catch (Exception) { /* DriveInfo can throw on inaccessible drives */ }
        }

        // --- macOS / Linux absolute paths ---
        foreach (var path in PlatformPaths)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                found.Add(path);
            }
        }

        s_cachedPaths = found.ToArray();
        s_lastDiscoveryTime = now;
        return s_cachedPaths;
    }

    /// <summary>
    /// Resolves the actual executable inside a macOS .app bundle.
    /// </summary>
    private static string GetExecutablePath(string path)
    {
        if (path.EndsWith(".app"))
        {
            string executable = Path.Combine(path, "Contents", "MacOS", "Antigravity IDE");
            if (File.Exists(executable)) return executable;
            executable = Path.Combine(path, "Contents", "MacOS", "Antigravity");
            if (File.Exists(executable)) return executable;
            return path;
        }
        return path;
    }

    /// <summary>
    /// Validates the installation path and falls back to a discovered path if it is invalid.
    /// </summary>
    private static string GetExistingInstallationPath(string path)
    {
        if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
        {
            return path;
        }

        var discovered = DiscoverInstallationPaths();
        if (discovered.Length > 0)
        {
            return discovered[0];
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
            var paths = DiscoverInstallationPaths();
            var installations = new CodeEditor.Installation[paths.Length];
            for (int i = 0; i < paths.Length; i++)
            {
                installations[i] = new CodeEditor.Installation
                {
                    Name = EditorName,
                    Path = paths[i]
                };
            }
            return installations;
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
        installation = GetExistingInstallationPath(installation);
        
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
            if (Application.platform == RuntimePlatform.OSXEditor && (installation.EndsWith(".app") || installation.Contains(".app/")))
            {
                string appPath = installation;
                if (installation.Contains(".app/"))
                {
                    appPath = installation.Substring(0, installation.IndexOf(".app/") + 4);
                }
                process.StartInfo.FileName = "/usr/bin/open";
                process.StartInfo.Arguments = $"-a \"{appPath}\" -n --args {arguments}";
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
            UnityEngine.Debug.LogError($"Failed to open Antigravity IDE: {e.Message}");
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
        if (editorPath.IndexOf("antigravity", StringComparison.OrdinalIgnoreCase) >= 0)
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
