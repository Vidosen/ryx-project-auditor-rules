// SPDX-FileCopyrightText: 2026 Ryx Interactive
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RyxInteractive.ProjectAuditorRules
{

#if RYX_ANALYZER
internal
#else
public
#endif
static class RulesJson
{
    public static bool TryRead<T>(string path, out T value, out string error) where T : class
    {
        value = null;
        error = string.Empty;
        try
        {
            using (var stream = File.OpenRead(path))
            {
                value = (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
                return value != null;
            }
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static string Serialize<T>(T value)
    {
        using (var stream = new MemoryStream())
        {
            new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    public static void WriteAtomic<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, Serialize(value), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (File.Exists(path))
            File.Replace(temporaryPath, path, null);
        else
            File.Move(temporaryPath, path);
    }
}
}
