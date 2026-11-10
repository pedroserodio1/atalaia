// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json.Nodes;

namespace Atalaia.Agent.Host.Settings;

/// <summary>
/// Máquina de estados do onboarding (primeira execução): passo atual, o que foi feito ou pulado, e se a janela abre sozinha.
/// Imutável e sem I/O: cada ação devolve um estado novo. A gravação fica em <see cref="OnboardingStore"/>.
/// Passos: 0 boas-vindas · 1 rede · 2 usb · 3 app · 4 código · 5 fontes · 6 pronto. Os passos 1 a 5 podem ser feitos ou pulados.
/// </summary>
internal sealed record OnboardingState(bool Completed, bool Dismissed, int Current, IReadOnlyDictionary<int, StepStatus> Steps)
{
    public const int Welcome = 0;
    public const int FirstStep = 1;
    public const int LastStep = 5;
    public const int Done = 6;

    public static readonly string[] StepKeys = ["welcome", "network", "usb", "app", "code", "sources", "done"];

    public static OnboardingState Initial { get; } = new(false, false, Welcome, new Dictionary<int, StepStatus>());

    /// <summary>Abre sozinha na primeira execução, até o usuário concluir ou sair da configuração. Depois, só pelas configurações.</summary>
    public bool AutoOpen => !Completed && !Dismissed;

    public StepStatus StatusOf(int step) => Steps.TryGetValue(step, out var s) ? s : StepStatus.Pending;

    public int Count(StepStatus status) => Enumerable.Range(FirstStep, LastStep).Count(i => StatusOf(i) == status);

    /// <summary>Vai para o passo pedido (fora de 0..6 é ajustado para o limite).</summary>
    public OnboardingState Go(int step) => this with { Current = Math.Clamp(step, Welcome, Done) };

    /// <summary>Marca o passo como feito e avança.</summary>
    public OnboardingState Complete(int step) => Mark(step, StepStatus.Done);

    /// <summary>Marca o passo como pulado e avança. Pular nunca impede de voltar depois: o passo fica "pendente de retomar".</summary>
    public OnboardingState Skip(int step) => Mark(step, StepStatus.Skipped);

    public OnboardingState Back() => Go(Current - 1);

    /// <summary>Concluir pelo "Concluir" do último passo.</summary>
    public OnboardingState Finish() => this with { Completed = true, Dismissed = false, Current = Done };

    /// <summary>Sair da configuração no meio: não abre mais sozinha, mas dá para reabrir (e retomar de onde parou) pelas configurações.</summary>
    public OnboardingState Dismiss() => this with { Dismissed = true };

    /// <summary>Refazer a primeira configuração do zero (botão nas configurações).</summary>
    public OnboardingState Restart() => Initial;

    private OnboardingState Mark(int step, StepStatus status)
    {
        if (step is < FirstStep or > LastStep) return this;   // boas-vindas e "pronto" não têm estado
        var steps = new Dictionary<int, StepStatus>(Steps) { [step] = status };
        return this with { Steps = steps, Current = Math.Min(step + 1, Done) };
    }

    // ---------------------------------------------------------------- persistência (seção "Onboarding" do settings.json)

    public const string Section = "Onboarding";

    /// <summary>Lê a seção <c>Onboarding</c>; valor ausente, de tipo errado ou desconhecido cai no padrão (nunca lança).</summary>
    public static OnboardingState FromSettings(JsonObject root)
    {
        if (root[Section] is not JsonObject o) return Initial;
        var steps = new Dictionary<int, StepStatus>();
        if (o["steps"] is JsonObject s)
            for (var i = FirstStep; i <= LastStep; i++)
                if (s[StepKeys[i]] is JsonValue v && v.TryGetValue<string>(out var text) && Enum.TryParse<StepStatus>(text, ignoreCase: true, out var st) && st != StepStatus.Pending)
                    steps[i] = st;
        var current = o["current"] is JsonValue c && c.TryGetValue<int>(out var n) ? Math.Clamp(n, Welcome, Done) : Welcome;
        return new OnboardingState(Flag(o, "completed"), Flag(o, "dismissed"), current, steps);
    }

    public JsonObject ToSettings()
    {
        var steps = new JsonObject();
        foreach (var (i, status) in Steps.OrderBy(p => p.Key))
            if (status != StepStatus.Pending) steps[StepKeys[i]] = status.ToString().ToLowerInvariant();
        return new JsonObject { ["completed"] = Completed, ["dismissed"] = Dismissed, ["current"] = Current, ["steps"] = steps };
    }

    private static bool Flag(JsonObject o, string name) => o[name] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

internal enum StepStatus { Pending, Done, Skipped }

/// <summary>Liga o <see cref="OnboardingState"/> ao <see cref="SettingsStore"/>.</summary>
internal sealed class OnboardingStore(SettingsStore settings)
{
    public OnboardingState State => OnboardingState.FromSettings(settings.Read());

    /// <summary>Lê o estado, aplica a transição e grava; devolve o estado novo.</summary>
    public OnboardingState Apply(Func<OnboardingState, OnboardingState> transition)
    {
        OnboardingState result = OnboardingState.Initial;
        settings.Update(root =>
        {
            result = transition(OnboardingState.FromSettings(root));
            root[OnboardingState.Section] = result.ToSettings();
        });
        return result;
    }
}
