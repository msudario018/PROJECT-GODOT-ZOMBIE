using Godot;
using System;

namespace ZombieApocalypse.Core.Components;

/// <summary>
/// Opt-in interaction contract for world objects.
///
/// Instead of hard-coding a type switch for every new interactable inside
/// PlayerInteraction, a node adds this component (and stays in the
/// "interactables" group) and describes its own prompt + behaviour:
///
///   var comp = new InteractableComponent { Prompt = "[E] Open Crate" };
///   comp.Interacted += who => DoTheThing(who);
///   AddChild(comp);
///
/// <see cref="PromptProvider"/> lets the owner return a dynamic prompt
/// (state-dependent, inventory-aware) instead of a static string.
/// </summary>
public partial class InteractableComponent : Node
{
    [Export] public string Prompt = "[E] Interact";

    /// <summary>Optional dynamic prompt override; return null to fall back to <see cref="Prompt"/>.</summary>
    public Func<string?>? PromptProvider;

    /// <summary>Raised when a character interacts with the owner.</summary>
    public event Action<Node3D>? Interacted;

    /// <summary>Optional veto: return false to make the interaction unavailable.</summary>
    public Func<Node3D, bool>? CanInteract;

    public override void _Ready()
    {
        var owner = GetParent();
        if (owner != null && !owner.IsInGroup("interactables"))
            owner.AddToGroup("interactables");
    }

    /// <summary>Display text for the hover prompt.</summary>
    public string GetPrompt()
    {
        string? dynamicPrompt = PromptProvider?.Invoke();
        return string.IsNullOrEmpty(dynamicPrompt) ? Prompt : dynamicPrompt!;
    }

    /// <summary>True when the interactor is currently allowed to use this object.</summary>
    public bool IsAvailableTo(Node3D who) => CanInteract?.Invoke(who) ?? true;

    /// <summary>Runs the interaction. Returns true when it was handled.</summary>
    public bool Interact(Node3D who)
    {
        if (!IsAvailableTo(who)) return false;
        Interacted?.Invoke(who);
        return true;
    }
}
