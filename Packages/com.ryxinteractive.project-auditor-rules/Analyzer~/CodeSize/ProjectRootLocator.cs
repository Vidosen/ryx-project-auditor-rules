// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.IO;
using Microsoft.CodeAnalysis;

namespace RyxInteractive.ProjectAuditorRules;

internal static class ProjectRootLocator
{
    public static string Find(Compilation compilation)
    {
        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = FindFromPath(tree.FilePath);
            if (!string.IsNullOrEmpty(root))
                return root;
        }

        return FindFromPath(Directory.GetCurrentDirectory());
    }

    public static string ToProjectRelativePath(string projectRoot, string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return PathGlobMatcher.Normalize(fullPath.Substring(root.Length));

        return PathGlobMatcher.Normalize(path);
    }

    private static string FindFromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        var fullPath = Path.GetFullPath(path);
        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        while (!string.IsNullOrEmpty(directory))
        {
            if (Directory.Exists(Path.Combine(directory, "Assets")) &&
                Directory.Exists(Path.Combine(directory, "ProjectSettings")))
                return directory;

            var parent = Directory.GetParent(directory)?.FullName;
            if (string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase))
                break;
            directory = parent;
        }

        return string.Empty;
    }
}
