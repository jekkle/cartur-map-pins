# WorldAtlas integration foundation (design only, Stage A)

WorldAtlas (terrain map mod) does not exist yet, so this is a contract proposal, not a verified integration. Nothing here is implemented. Cartur's Map Pins stays authoritative for the markers it owns; WorldAtlas reads, never writes.

## 1. Principles

- Map Pins has no reference to WorldAtlas, at build time or run time. WorldAtlas may reference Map Pins' DLL, or reflect, and soft-depends on it.
- Read-only. No method returns a live `PinData`, a mutable list, or a handle that changes a pin.
- No new information. The snapshot contains only pins already in `Minimap.m_pins`: pins the player placed, the mod auto-placed after discovery, or other players shared. Fog of war is untouched because no pin is created or revealed by the API.
- No save-format change. Identity is derived, not stored.

## 2. Contract (version 1)

```csharp
namespace CarturMapPins
{
    public static class MapPinsApi
    {
        public const int ApiVersion = 1;                // bump on breaking change
        public static bool Ready { get; }               // false until a world is loaded
        public static int Revision { get; }             // increases when the marker set changes
        public static IReadOnlyList<MarkerInfo> GetMarkers();  // fresh immutable copy per call (cached per Revision)
        public static bool TryGetMarker(string id, out MarkerInfo marker);
        public static event Action<int> Changed;        // argument = new Revision; raised on the main thread
        public static event Action Reset;               // world/character change: drop everything
    }

    public readonly struct MarkerInfo
    {
        public string Id { get; }          // stable, see section 3
        public float X { get; }            // world metres
        public float Z { get; }
        public string Name { get; }        // as drawn (localised)
        public string Category { get; }    // PinCategory name for mod pins ("Ore"), "Custom"/"Shared"/"Vanilla" otherwise
        public int IconIndex { get; }      // the pin's PinType value (custom icons keep their registered index)
        public bool Visible { get; }       // false when hidden by hide list / hide key / range / priority zoom
        public bool Checked { get; }
        public MarkerSource Source { get; }
    }

    public enum MarkerSource { Auto = 0, Player = 1, Shared = 2, Vanilla = 3 }
}
```
Selection information (the inspector's selected marker) is a separate `MapPinsApi.SelectedMarkerId` (nullable string) with a `SelectionChanged` event, added only when WorldAtlas needs it (ladder: no speculative members). Y is omitted: the map is 2D and Valheim pin Y is not stored reliably.

## 3. Identity

- Mod pins (have a `PinRecord` entry): `"r:" + record key + "@" + round(x) + "," + round(z)`, e.g. `r:Ore:Copper@1204,-388`. Position is rounded to the metre because that is exactly what the mod already uses to match pins to records (`PinRecord.PositionKey`, which also states that `RepairPositions` keeps pins on their record positions). Stable across sessions and renames; changes only if the pin's record changes.
- Player pins without a record: `"u:" + PinType + "@" + x + "," + z`. Stable until moved (the editor does not move pins) or re-icon'd.
- Shared pins (`m_ownerID != 0`): `"s:" + ownerId + ":" + x + "," + z`.
- IDs are opaque strings to consumers. Collisions (two pins on one metre with the same type) are possible; the mod already treats that as one record. Documented limitation.

## 4. Change events

- One coalesced `Changed(revision)` per `UpdatePins` pass in which the content checksum differs (the same checksum Phase 2 uses for clusters, over all pins: identity hash, rounded position, type, name hash, checked, owner, visibility). Consumers diff by `Id` if they need adds/removes/updates; the diff is cheap and avoids the mod guessing what the consumer cares about.
- `Reset` raised on `Game.Shutdown` and when a different world/character loads.
- Lifecycle-safe: events are plain C# events; consumers unsubscribe in `OnDestroy`. Map Pins does not hold strong references to subscribers beyond the delegate list, and it catches and logs exceptions per subscriber so one bad handler cannot break the pass.
- No polling needed. **Limitation:** `UpdatePins` only runs when the large or small map is active and something requested a pass; a pin changed while no map is shown is observed at the next pass. A consumer that renders the terrain only while the map is open sees every change before it draws.

## 5. Fog and discovery safeguards

- API returns only entries that exist in `m_pins`. It never calls `AddPin`, `DiscoverLocation`, `Explore` or the discovery scanner.
- `Visible` is presentation state (our own hide flags), not discovery state. Discovery status is not exposed: a pin on the map is by definition discovered; consumers decide terrain visibility themselves from the game's explored mask.
- Auto-pin creation keeps its own `DiscoveryRadius` rule; the API cannot trigger or widen it.
- Snapshots are copies, so a consumer cannot mutate `Name`/`Checked`/position.

## 6. Versioning and compatibility

- `ApiVersion` integer; additive changes (new fields on `MarkerInfo`) keep the number and are safe for consumers compiled against an older struct only if they construct nothing; since the struct is read-only and consumer-constructed never, that holds. Removing or retyping a member bumps `ApiVersion` and is announced in the changelog.
- A consumer checks `Chainloader.PluginInfos.ContainsKey("<our GUID>")`, then reads `ApiVersion` by reflection, then uses the API (or just reflects throughout). The same soft-dependency rule that the repo's CLAUDE.md prescribes.
- The type lives in `CarturMapPins` DLL as `public`; the rest of the mod is `internal`, keeping the surface to this file.

## 7. Example consumer (WorldAtlas side, illustrative, not compiled)

```csharp
static class PinsBridge
{
    static Func<IReadOnlyList<MarkerInfoLike>> _get;
    public static void TryHook(BaseUnityPlugin host)
    {
        if (!Chainloader.PluginInfos.TryGetValue("com.jekkle.valheim.carturmappins", out var info)) return; // Plugin.PluginGuid, Plugin.cs:17
        var api = info.Instance.GetType().Assembly.GetType("CarturMapPins.MapPinsApi");
        if (api == null || (int)api.GetField("ApiVersion").GetValue(null) != 1) return;
        var evt = api.GetEvent("Changed");
        evt.AddEventHandler(null, (Action<int>)(rev => host.StartCoroutine(Redraw())));
        // read GetMarkers() through a cached delegate; draw icons over the terrain tiles, skip !Visible
    }
}
```

## 8. How a future renderer uses it
Draw terrain; for each `MarkerInfo` with `Visible`, convert `(X,Z)` to its own map coordinates and draw its icon sprite by `IconIndex` (the sprite is not part of the API: `Minimap.GetSprite(PinType)` is private in the current game, and the mod's custom icons live in its own sheet; sprite delivery is a separate question, D10). On `Changed`, diff by `Id`, redraw only changed markers. On `Reset`, clear. Clustering is a Map Pins presentation feature and is **not** in the API; if WorldAtlas wants clusters it computes its own from the snapshots.

## 9. Known limitations
- No per-marker events (coalesced revision only).
- Not thread-safe; main thread only.
- ID collisions at identical metre and type.
- Icon sprite delivery not designed (needs the owner's call, D10).
- Nothing in the API has been compiled or tested.
