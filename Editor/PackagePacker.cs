using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PixelArtSplats.EditorTools
{
    /// <summary>
    /// Packs this package into a tarball that can be installed with
    /// <c>Package Manager → + → Add package from tarball</c>.
    /// <para>Checks the one thing that silently breaks a packed package: an asset without a
    /// <c>.meta</c> file. Immutable packages (tarball, git, registry) ignore those assets, and a
    /// missing <c>.asmdef.meta</c> takes the whole assembly definition with it.</para>
    /// </summary>
    public static class PackagePacker
    {
        const string OutFolder = "Dist";

        [MenuItem("Pixel Art Splats/Package/Pack Tarball", priority = 80)]
        public static void Pack()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackagePacker).Assembly);
            if (info == null)
            {
                Debug.LogError("Pixel art splats: cannot tell which package this is — is it installed as a package?");
                return;
            }

            List<string> missing = MissingMetas(info.resolvedPath);
            if (missing.Count > 0)
            {
                Debug.LogError($"Pixel art splats: {missing.Count} asset(s) without a .meta file. A packed package " +
                               "would ignore them (and a missing .asmdef.meta breaks compilation). Let the editor " +
                               "import the package, then pack again:\n  " + string.Join("\n  ", missing));
                return;
            }

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outDir = Path.Combine(root, OutFolder);
            Directory.CreateDirectory(outDir);
            string name = $"{info.name}-{info.version}.tgz";
            string output = Path.Combine(outDir, name);

            // a tarball for UPM has to hold exactly one top level folder called "package"
            string stage = Path.Combine(Path.GetTempPath(), "pixelart-splats-pack");
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            string staged = Path.Combine(stage, "package");
            CopyTree(info.resolvedPath, staged);

            Run("tar", $"czf \"{output}\" -C \"{stage}\" package");
            Directory.Delete(stage, true);

            if (!File.Exists(output))
            {
                Debug.LogError($"Pixel art splats: packing failed, no file at {output}");
                return;
            }
            Debug.Log($"Pixel art splats: packed {info.name} {info.version} → {output} " +
                      $"({new FileInfo(output).Length / 1048576f:0.0} MB)");
            EditorUtility.RevealInFinder(output);
        }

        /// <summary>Every file and folder Unity would import in the package, minus anything already carrying a meta.</summary>
        static List<string> MissingMetas(string packagePath)
        {
            var missing = new List<string>();
            Walk(new DirectoryInfo(packagePath), packagePath, missing);
            return missing;
        }

        static void Walk(DirectoryInfo dir, string packagePath, List<string> missing)
        {
            foreach (FileSystemInfo entry in dir.EnumerateFileSystemInfos())
            {
                if (entry.Extension == ".meta") continue;
                if (entry.Name.EndsWith("~")) continue; // Documentation~, Samples~: Unity never imports these
                if (!File.Exists(entry.FullName + ".meta"))
                    missing.Add(Path.GetRelativePath(packagePath, entry.FullName));
                if (entry is DirectoryInfo sub) Walk(sub, packagePath, missing);
            }
        }

        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string file in Directory.GetFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(from))
                CopyTree(dir, Path.Combine(to, Path.GetFileName(dir)));
        }

        static void Run(string exe, string args)
        {
            var start = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using Process p = Process.Start(start);
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) Debug.LogError($"Pixel art splats: {exe} exited {p.ExitCode}: {err}");
        }
    }
}
