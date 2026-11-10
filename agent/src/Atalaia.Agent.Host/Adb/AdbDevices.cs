// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

namespace Atalaia.Agent.Host.Adb;

/// <summary>O estado de um aparelho segundo o <c>adb devices</c>.</summary>
internal enum AdbDeviceState
{
    /// <summary>Pronto: depuração USB autorizada.</summary>
    Authorized,
    /// <summary>Plugado, mas o tablet ainda não aceitou a depuração USB (o aviso aparece na tela dele).</summary>
    Unauthorized,
    /// <summary>O <c>adb</c> o vê, mas ele não responde (cabo ruim, tablet travado).</summary>
    Offline,
    /// <summary>Qualquer outro estado (<c>recovery</c>, <c>bootloader</c>, <c>no permissions</c>...).</summary>
    Other,
}

internal sealed record AdbDevice(string Serial, AdbDeviceState State, string RawState, string? Model, string? Product);

internal static class AdbDevices
{
    /// <summary>Os estados que o <c>adb</c> imprime. Uma linha com outra palavra no lugar do estado é mensagem de erro, não aparelho.</summary>
    private static readonly HashSet<string> KnownStates =
        ["device", "unauthorized", "offline", "recovery", "bootloader", "sideload", "rescue", "authorizing", "connecting", "no permissions"];

    /// <summary>
    /// A saída de <c>adb devices -l</c>: <c>SERIAL   device product:x model:T68 device:y transport_id:1</c> (separado por espaços; sem <c>-l</c>
    /// o separador é TAB, e este leitor aceita os dois). Ignora o cabeçalho, linhas vazias e os avisos do servidor (<c>* daemon ...</c>).
    /// </summary>
    public static IReadOnlyList<AdbDevice> Parse(string output)
    {
        var list = new List<AdbDevice>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('*') || line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            // "no permissions" tem duas palavras: junta tudo até o primeiro par chave:valor.
            var i = 1;
            var state = new List<string>();
            while (i < parts.Length && !parts[i].Contains(':')) state.Add(parts[i++]);
            if (state.Count == 0) continue;

            string? model = null, product = null;
            for (; i < parts.Length; i++)
            {
                var colon = parts[i].IndexOf(':');
                if (colon <= 0) continue;
                var key = parts[i][..colon];
                var value = parts[i][(colon + 1)..];
                if (key == "model") model = value.Replace('_', ' ');
                else if (key == "product") product = value;
            }
            var rawState = string.Join(' ', state);
            if (!KnownStates.Contains(rawState)) continue;
            list.Add(new AdbDevice(parts[0], Classify(rawState), rawState, model, product));
        }
        return list;
    }

    public static AdbDeviceState Classify(string rawState) => rawState switch
    {
        "device" => AdbDeviceState.Authorized,
        "unauthorized" => AdbDeviceState.Unauthorized,
        "offline" => AdbDeviceState.Offline,
        _ => AdbDeviceState.Other,
    };
}
