// Funky Launcher — Copyright (C) 2026 Funky. Licensed under GPL-3.0-or-later.
using System.Reflection;
using System.Runtime.Loader;
using FunkyLauncher.Core;

namespace FunkyLauncher.Services;

/// <summary>Loads the Funky Pro module (%LOCALAPPDATA%\FunkyLauncher\pro\FunkyLauncher.Pro.dll) when a valid key is saved.</summary>
public static class ProModules
{
    public static string Dir => Path.Combine(AppConfig.DataDir, "pro");
    public static string DllPath => Path.Combine(Dir, "FunkyLauncher.Pro.dll");

    public static IProModule? Current { get; private set; }
    public static string? LoadError { get; private set; }
    public static bool Installed => File.Exists(DllPath);

    private static bool _hooked;

    /// <summary>(Re)checks the key and loads the module once. A module can't be unloaded: removing the key just stops using it.</summary>
    public static void Refresh(Settings s)
    {
        LoadError = null;
        if (!ProLicense.Active(s)) { Active = false; return; }
        Active = true;
        if (Current != null || !Installed) return;
        try
        {
            var asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(DllPath);
            var type = asm.GetTypes().FirstOrDefault(t => typeof(IProModule).IsAssignableFrom(t) && !t.IsAbstract)
                       ?? throw new InvalidOperationException("no IProModule in FunkyLauncher.Pro.dll");
            var m = (IProModule)Activator.CreateInstance(type)!;
            m.Start(new Host(s));
            Current = m;
            if (!_hooked)
            {
                _hooked = true;
                BackupService.Created += (id, title, dir) =>
                {
                    if (!Active) return;
                    try { Current?.OnBackupCreated(id, title, dir); } catch (Exception ex) { Log("backup hook: " + ex.Message); }
                };
            }
        }
        catch (Exception ex) { LoadError = ex.Message; Log("load: " + ex); }
    }

    /// <summary>Key valid right now (the module, if loaded, is only used while this is true).</summary>
    public static bool Active { get; private set; }

    public static IProModule? Use => Active ? Current : null;

    private static void Log(string msg)
    {
        try { Directory.CreateDirectory(Dir); File.AppendAllText(Path.Combine(Dir, "pro.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {msg}\n"); } catch { }
    }

    private sealed class Host(Settings s) : IProHost
    {
        public Settings Settings => s;
        public string DataDir => Dir;
        public string Language => Loc.Lang;
        public void Log(string message) => ProModules.Log(message);
    }
}
