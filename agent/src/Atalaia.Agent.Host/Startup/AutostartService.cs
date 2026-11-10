// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using Microsoft.Win32;

namespace Atalaia.Agent.Host.Startup;

/// <summary>Costura do registro do Windows (a chave Run do usuário) para testar sem mexer nele.</summary>
internal interface IRunKey
{
    string? Get(string name);
    void Set(string name, string value);
    void Delete(string name);
}

/// <summary><c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>: sem administrador, na sessão do usuário (o agente não pode ser serviço).</summary>
internal sealed class RegistryRunKey : IRunKey
{
    private const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

internal enum AutostartState
{
    Off,
    On,
    /// <summary>Está ligado, mas aponta para outro programa (outra pasta de instalação, por exemplo).</summary>
    OtherPath,
}

/// <summary>"Iniciar com o Windows": um valor na chave Run do usuário que abre este programa.</summary>
internal sealed class AutostartService(IRunKey key, Func<string?> exePath)
{
    public const string ValueName = "Atalaia";

    public AutostartState State
    {
        get
        {
            var value = key.Get(ValueName);
            if (string.IsNullOrWhiteSpace(value)) return AutostartState.Off;
            var self = exePath();
            return self is not null && string.Equals(value.Trim('"'), self, StringComparison.OrdinalIgnoreCase) ? AutostartState.On : AutostartState.OtherPath;
        }
    }

    /// <summary>Liga. Só vale para o programa do Atalaia (<c>AtalaiaAgent.exe</c>): rodando por um host genérico (<c>dotnet.exe</c>) não há o que registrar.</summary>
    public void Enable()
    {
        var self = exePath();
        if (self is null || !Path.GetFileName(self).Equals("AtalaiaAgent.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Só dá para iniciar com o Windows a partir do programa instalado (AtalaiaAgent.exe).");
        key.Set(ValueName, Quote(self));
    }

    public void Disable() => key.Delete(ValueName);

    private static string Quote(string path) => "\"" + path + "\"";
}
