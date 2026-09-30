using Godot;
using ZombieApocalypse.Core.Components;
using ZombieApocalypse.Core.Data;
using ZombieApocalypse.Core.Utilities;
using ZombieApocalypse.Systems.Loot;
using ZombieApocalypse.World.Defenses;
using ZombieApocalypse.World.Environment;
using ZombieApocalypse.World.Power.Generation;

namespace ZombieApocalypse.Entities.Player;

/// <summary>
/// Handles player interaction with world entities:
///   - Doors (Open / Close)
///   - Loot Containers (Searchable crates, cabinets, lockers)
///   - Corpses (Scavenge remains, bury grave, incinerate miasma)
/// </summary>
public partial class PlayerInteraction : Node3D
{
    [Export] public float InteractionRange = 2.8f;

    private PlayerController _player = null!;
    private InventoryComponent? _inventory;
    private Node3D? _currentHoveredInteractable;

    public override void _Ready()
    {
        _player = GetOwner<PlayerController>() ?? (GetParent() as PlayerController)!;
        _inventory = _player.GetNodeOrNull<InventoryComponent>("InventoryComponent");
    }

    public override void _Process(double delta)
    {
        UpdateHoverPrompt();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed("interact") || 
           (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.E))
        {
            TryInteract();
        }
    }

    private void UpdateHoverPrompt()
    {
        var closest = FindClosestInteractable();

        if (closest != _currentHoveredInteractable)
        {
            // Hide previous prompt
            HidePrompt(_currentHoveredInteractable);

            _currentHoveredInteractable = closest;

            // Show new prompt
            ShowPrompt(_currentHoveredInteractable);
        }
    }

    private Node3D? FindClosestInteractable()
    {
        var interactables = GetTree().GetNodesInGroup("interactables");
        Vector3 playerPos = _player.GlobalPosition;
        float rangeSq = InteractionRange * InteractionRange;

        Node3D? closest = null;
        float closestDistSq = float.MaxValue;

        foreach (var node in interactables)
        {
            if (node is Node3D node3D && GodotObject.IsInstanceValid(node3D))
            {
                float distSq = MathUtils.DistanceSquaredXZ(playerPos, node3D.GlobalPosition);
                if (distSq <= rangeSq && distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closest = node3D;
                }
            }
        }

        return closest;
    }

    private void ShowPrompt(Node3D? interactable)
    {
        if (interactable == null || !GodotObject.IsInstanceValid(interactable)) return;

        // Preferred path: self-describing InteractableComponent.
        var interactableComponent = interactable.GetNodeOrNull<InteractableComponent>("InteractableComponent");
        if (interactableComponent != null)
        {
            var label = GetPromptLabel(interactable);
            if (label != null)
            {
                label.Text = interactableComponent.GetPrompt();
                label.Visible = interactableComponent.IsAvailableTo(_player);
            }
            return;
        }

        if (interactable is LootContainer container)
        {
            container.ShowPrompt(true);
        }
        else if (interactable is Corpse corpse)
        {
            string prompt = corpse.GetDefaultPrompt();
            if (_inventory != null && _inventory.Has("fuel_can") && corpse.CurrentStage == CorpseStage.RottingMiasma)
                prompt += " | [Hold E] Burn Miasma";
            else if (_inventory != null && _inventory.Has("shovel"))
                prompt += " | [E] Bury Corpse";
            corpse.ShowPrompt(true, prompt);
        }
        else if (interactable is CombustionGenerator generator)
        {
            generator.ShowPrompt(true);
        }
    }

    private void HidePrompt(Node3D? interactable)
    {
        if (interactable == null || !GodotObject.IsInstanceValid(interactable)) return;

        if (interactable.GetNodeOrNull<InteractableComponent>("InteractableComponent") != null)
        {
            var label = GetPromptLabel(interactable);
            if (label != null) label.Visible = false;
            return;
        }

        if (interactable is LootContainer container)
        {
            container.ShowPrompt(false);
        }
        else if (interactable is Corpse corpse)
        {
            corpse.ShowPrompt(false);
        }
        else if (interactable is CombustionGenerator generator)
        {
            generator.ShowPrompt(false);
        }
    }

    private void TryInteract()
    {
        var closest = FindClosestInteractable();
        if (closest == null) return;

        // 0. Component-driven interactables describe themselves.
        var interactableComponent = closest.GetNodeOrNull<InteractableComponent>("InteractableComponent");
        if (interactableComponent != null)
        {
            interactableComponent.Interact(_player);
            return;
        }

        // 1. Loot Container
        if (closest is LootContainer container)
        {
            if (_inventory != null)
            {
                container.TrySearch(_inventory);
            }
            return;
        }

        // 2. Corpse
        if (closest is Corpse corpse)
        {
            HandleCorpseInteraction(corpse);
            return;
        }

        // 3. Power generator (refuel with a canister)
        if (closest is CombustionGenerator generator)
        {
            if (_inventory != null && generator.RefuelFromInventory(_inventory))
                GD.Print($"[PlayerInteraction] Generator refuelled to {generator.FuelCanisters:F1} canisters.");
            else
                GD.Print("[PlayerInteraction] Need a fuel canister to refuel the generator.");
            return;
        }

        // 4. Door
        if (closest is DoorBase door)
        {
            door.ToggleDoor();
            return;
        }
    }

    /// <summary>Prompt label used by component-driven interactables.</summary>
    private static Label3D? GetPromptLabel(Node3D node)
    {
        var label = node.GetNodeOrNull<Label3D>("PromptLabel") ?? node.GetNodeOrNull<Label3D>("Label");
        if (label != null && !label.Visible && string.IsNullOrEmpty(label.Text))
        {
            label.Text = "[E] Interact";
            label.Position = new Vector3(0f, 1.4f, 0f);
            label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
            label.FontSize = 22;
        }
        return label;
    }

    private void HandleCorpseInteraction(Corpse corpse)
    {
        if (_inventory == null) return;

        // If corpse has active miasma and player has fuel, burn it
        if (corpse.CurrentStage == CorpseStage.RottingMiasma && _inventory.Has("fuel_can"))
        {
            _inventory.TryRemove("fuel_can", 1);
            corpse.Burn();
            return;
        }

        // If player has a shovel or it's bloated/rotting, bury it
        if (_inventory.Has("shovel"))
        {
            corpse.Bury();
            return;
        }

        // Otherwise scavenge fresh/bloated corpse
        if (!corpse.IsSearched)
        {
            corpse.Scavenge(_inventory);
        }
        else
        {
            // Already searched, bury or clear away
            corpse.Bury();
        }
    }
}
