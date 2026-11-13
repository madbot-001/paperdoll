// The rules follow Space Station 14's client visualizer systems, each cited below, Copyright (c)
// 2017-2026 Space Wizards Federation, MIT licence. See THIRD-PARTY-NOTICES.md.

using Paperdoll.Core.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Paperdoll.Core.Rendering;

/// <summary>
/// How the game's visualizer systems leave an entity's layers at round start, on a thing that is
/// powered, unbolted, unwelded and untouched, and a door open or closed as its prototype starts it:
/// one rule per layer map key, which applies when the entity has the component that drives the
/// layer. A rule may hide or show the layer and set its state and colour; anything a rule leaves
/// out stays as the prototype writes it.
/// <para>
/// A <c>GenericVisualizer</c> is not followed: it changes a layer only once the server has set
/// that appearance key, and which keys each server system sets at start is not in the prototype.
/// </para>
/// </summary>
public static class RoundStartLook
{
    // What a rule does to a layer: shown or hidden, its state and its colour (null leaves each).
    private readonly record struct Look(bool? Visible, string? State, Rgba? Color = null);

    private static readonly Look Hidden = new(false, null);

    // Map key, the component that drives it, and what it shows given the entity and the fork's prototypes.
    private static readonly (string MapKey, string Component, Func<YamlMappingNode, PrototypeIndex, Look> Rule)[] Rules =
    [
        // Content.Client/Wires/Visualizers/WiresVisualizerSystem.cs: with the panel closed, shown
        // only when WiresVisuals.visibleWhenClosed (default false, WiresVisualsComponent.cs).
        ("enum.WiresVisualLayers.MaintenancePanel", "WiresVisuals", (e, _) => new(Field(e, "WiresVisuals", "visibleWhenClosed") is "true" or "True", null)),
        // Content.Client/Tools/Visualizers/WeldableVisualizerSystem.cs: shown only when welded.
        ("enum.WeldableLayers.BaseWelded", "Weldable", (_, _) => Hidden),
        // Content.Client/Doors/DoorSystem.cs, UpdateAppearanceForDoorState: a closed door shows
        // Door.closedSpriteState and an open one Door.openSpriteState (defaults "closed" and "open",
        // Content.Shared/Doors/Components/DoorComponent.cs), and no emagging sparks.
        ("enum.DoorVisualLayers.Base", "Door", (e, _) => new(null, DoorOpen(e)
            ? Field(e, "Door", "openSpriteState") ?? "open"
            : Field(e, "Door", "closedSpriteState") ?? "closed")),
        ("enum.DoorVisualLayers.BaseEmagging", "Door", (_, _) => Hidden),
        // Content.Client/Doors/AirlockSystem.cs: a powered airlock without bolt or emergency lights
        // shows its unlit layer while closed, and while open only when openUnlitVisible; with
        // openUnlitVisible the layer takes Airlock.closedSpriteState or openSpriteState (defaults
        // "closed_unlit" and "open_unlit", Content.Shared/Doors/Components/AirlockComponent.cs).
        ("enum.DoorVisualLayers.BaseUnlit", "Airlock", (e, _) =>
        {
            var openUnlit = Field(e, "Airlock", "openUnlitVisible") is "true" or "True";
            return DoorOpen(e)
                ? new(openUnlit, openUnlit ? Field(e, "Airlock", "openSpriteState") ?? "open_unlit" : null)
                : new(true, openUnlit ? Field(e, "Airlock", "closedSpriteState") ?? "closed_unlit" : null);
        }),
        ("enum.DoorVisualLayers.BaseBolted", "Airlock", (_, _) => Hidden),
        ("enum.DoorVisualLayers.BaseEmergencyAccess", "Airlock", (_, _) => Hidden),
        // Content.Client/VendingMachines/VendingMachineSystem.cs, UpdateAppearance: a powered machine
        // shows offState under normalState and screenState (VendingMachineVisualsComponent.cs); a
        // state the component does not give leaves its layer alone.
        ("enum.VendingMachineVisualLayers.Base", "VendingMachineVisuals", (e, _) => Shown(Field(e, "VendingMachineVisuals", "offState"))),
        ("enum.VendingMachineVisualLayers.BaseUnshaded", "VendingMachineVisuals", (e, _) => Shown(Field(e, "VendingMachineVisuals", "normalState"))),
        ("enum.VendingMachineVisualLayers.Screen", "VendingMachineVisuals", (e, _) => Shown(Field(e, "VendingMachineVisuals", "screenState"))),
        // Content.Client/Storage/Visualizers/EntityStorageVisualizerSystem.cs: a closed locker or
        // crate shows EntityStorageVisuals.stateBaseClosed on its base layer, and its door layer in
        // stateDoorClosed, hidden when the component gives none (EntityStorageVisualsComponent.cs).
        ("enum.StorageVisualLayers.Base", "EntityStorageVisuals", (e, _) => Shown(Field(e, "EntityStorageVisuals", "stateBaseClosed"))),
        ("enum.StorageVisualLayers.Door", "EntityStorageVisuals", (e, _) =>
            Field(e, "EntityStorageVisuals", "stateDoorClosed") is { Length: > 0 } door ? new(true, door) : Hidden),
        // Content.Client/Audio/Jukebox/JukeboxSystem.cs: a powered jukebox is On (as
        // Content.Server/Audio/Jukebox/JukeboxSystem.cs sets it), showing Jukebox.onState.
        ("enum.JukeboxVisualLayers.Base", "Jukebox", (e, _) => Shown(Field(e, "Jukebox", "onState"))),
        // Content.Client/Light/Visualizers/PoweredLightVisualizerSystem.cs: the base layer shows the
        // spriteStateMap entry of the bulb state, and the glow only when on, in the colour of the
        // entity's PointLight, which a lit fixture's bulb sets (SharedPoweredLightSystem.SetLight).
        ("enum.PoweredLightLayers.Base", "PoweredLightVisuals", (e, p) => new(null, LightStateMap(e).GetValueOrDefault(BulbState(e, p)))),
        ("enum.PoweredLightLayers.Glow", "PoweredLightVisuals", (e, p) => new(BulbState(e, p) == "On", null, GlowColor(e, p))),
    ];

    /// <summary>
    /// The draw depth at round start: a door's is <c>Door.openDrawDepth</c> while open and
    /// <c>Door.closedDrawDepth</c> while closed, both by default Doors, whatever its sprite says
    /// (Content.Client/Doors/DoorSystem.cs, UpdateAppearanceForDoorState; Content.Shared/Doors/Components/DoorComponent.cs).
    /// </summary>
    public static int DrawDepth(YamlMappingNode entity, int spriteDepth) =>
        PrototypeYaml.Component(entity, "Door") == null ? spriteDepth
            : DrawDepths.Parse(Field(entity, "Door", DoorOpen(entity) ? "openDrawDepth" : "closedDrawDepth")) ?? DrawDepths.ByName["Doors"];

    /// <summary>
    /// Whether a door starts open: its <c>Door.state</c> is Open, or Opening, which the game finishes
    /// at once when nothing is under way (Content.Shared/Doors/Systems/SharedDoorSystem.cs,
    /// OnComponentInit). Closing finishes closed; the default is Closed.
    /// </summary>
    public static bool DoorOpen(YamlMappingNode entity) =>
        Field(entity, "Door", "state") is "Open" or "Opening";

    /// <summary>
    /// The <c>LightBulb</c> component of the lamp a fixture spawns with (<c>PoweredLight.hasLampOnSpawn</c>),
    /// or null when it spawns with none or the lamp is not a bulb, which the game counts as empty
    /// (Content.Shared/Light/EntitySystems/SharedPoweredLightSystem.cs, UpdateLight).
    /// </summary>
    public static YamlMappingNode? Bulb(YamlMappingNode entity, PrototypeIndex prototypes) =>
        Field(entity, "PoweredLight", "hasLampOnSpawn") is { Length: > 0 } id && prototypes.Resolve("entity", id) is { } lamp
            ? PrototypeYaml.Component(lamp, "LightBulb")
            : null;

    /// <summary>Changes the layers of an entity, its prototype with parents applied, to how they look at round start.</summary>
    public static void Apply(YamlMappingNode entity, IEnumerable<LayerSpec> layers, PrototypeIndex prototypes)
    {
        foreach (var layer in layers)
        {
            foreach (var (mapKey, component, rule) in Rules)
            {
                if (!layer.MapKeys.Contains(mapKey) || PrototypeYaml.Component(entity, component) == null)
                    continue;
                var look = rule(entity, prototypes);
                if (look.Visible is { } visible)
                    layer.Visible = visible;
                if (look.State is { } state)
                    layer.State = state;
                if (look.Color is { } color)
                    layer.Color = color;
            }
        }
    }

    // SetLayerState shows the layer in the state, when there is one.
    private static Look Shown(string? state) => string.IsNullOrEmpty(state) ? default : new(true, state);

    private static string? Field(YamlMappingNode entity, string component, string field) =>
        PrototypeYaml.Scalar(PrototypeYaml.Component(entity, component), field);

    // PoweredLightState by name, as SharedPoweredLightSystem.UpdateLight sets it: Empty without a
    // bulb, Broken or Burned as the bulb's startingState says, else On when switched on, Off when
    // not. PoweredLight.on defaults to true, LightBulb.startingState to Normal
    // (Content.Shared/Light/Components/PoweredLightComponent.cs, LightBulbComponent.cs).
    private static string BulbState(YamlMappingNode entity, PrototypeIndex prototypes) =>
        Bulb(entity, prototypes) is not { } bulb ? "Empty"
        : PrototypeYaml.Scalar(bulb, "startingState") is { } state and ("Broken" or "Burned") ? state
        : Field(entity, "PoweredLight", "on") is "false" or "False" ? "Off"
        : "On";

    // The PointLight's colour: a lit fixture's bulb's (LightBulb.color, default white), else the
    // prototype's own; null without a PointLight, which leaves the glow as written.
    private static Rgba? GlowColor(YamlMappingNode entity, PrototypeIndex prototypes)
    {
        if (PrototypeYaml.Component(entity, "PointLight") is not { } light)
            return null;
        var written = BulbState(entity, prototypes) == "On" ? PrototypeYaml.Scalar(Bulb(entity, prototypes), "color") : PrototypeYaml.Scalar(light, "color");
        return Rgba.TryParse(written, out var color) ? color : Rgba.White;
    }

    // PoweredLightVisualsComponent.SpriteStateMap: its defaults, with the prototype's entries over them.
    private static Dictionary<string, string> LightStateMap(YamlMappingNode entity)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Empty"] = "empty",
            ["Off"] = "off",
            ["On"] = "on",
            ["Broken"] = "broken",
            ["Burned"] = "burn",
        };
        if (PrototypeYaml.Mapping(PrototypeYaml.Component(entity, "PoweredLightVisuals"), "spriteStateMap") is not { } written)
            return map;
        foreach (var (key, value) in written.Children)
        {
            if (key is YamlScalarNode { Value: { } name } && value is YamlScalarNode { Value: { Length: > 0 } state })
                map[name] = state;
        }
        return map;
    }
}
