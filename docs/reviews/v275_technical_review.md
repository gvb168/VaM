# CharacterRouter v275 — technical review (migration baseline)

Status: **analysis only. No build was produced.**
Source: `releases/Gleb.CharacterRouter.275.var` (sha256 `e62a3ef9…a9f1`), unpacked verbatim into `package/`.

---

## 1. Actual version

| Evidence | Value |
|---|---|
| `meta.json` → `version` | `"275"` |
| `meta.json` → `description` | "CharacterRouter v275: keep 2B-SC6 and 2B-Kaine external hair CUAs at their measured head-local anchors when the Person pose changes; retains the v274 skirt material restoration." |
| `meta.json` → `instructions` | Add 2B-SC6, load another Pose Preset, verify hair alignment; remove and re-add. |
| Newest file timestamps | `CharacterRouter.cs`, `SceneCuaAccessoryAdapter.cs` — 2026-10-01 00:56 (same minute as meta.json) |
| Code | `SceneCuaAccessoryAdapter.Sync2BSceneHairAnchors()` + `CharacterRouter.LateUpdate()` hook — exactly the v275 feature described |

There is no version constant in code. Log prefixes (`v258`, `v261`, `v263`, `v270`, `v272`, `v273`, `v274`) record the version in which each line was introduced; they are not the build number.

**Verdict: confirmed v275, matches the handoff's "latest development branch".**

---

## 2. Package / compile map

`CharacterRouter.cslist` (compiled, in order):

```
CharacterRouter.cs            28 341 lines — catalog/UI/spawn/look lifecycle (monolith)
ExternalHairAdapter.cs           223 — Clothing+CUAClothingAlt hairstyles (Nico-type)
DecalMakerAdapter.cs           1 259 — Chokaphi DecalMaker scene-look overlays
SceneCuaAccessoryAdapter.cs      838 — scene CUA atoms outside the Person (Elowen tiara, 2B-SC6/Kaine hair)
SceneMorphParityAdapter.cs       319 — Sakuri morph parity + genital diffuse/decal replay
ModelPluginAdapter.cs          2 482 — model plugins, HeelAdjust, 2B family feet/tongue/skirt
HeadTrackingAdapter.cs           262 — Follow Camera With Head policy (MacGruber.Gaze, BodyLanguage client)
NativeFutanariAdapter.cs         199 — v141 native-X recognition/ownership only
NativeFutanariHairAdapter.cs     450 — v181 opposite-bank CustomHairItem clone lifecycle
AltFutaAdapter.cs                135 — v140 logical FUTA tag; never touches gender/banks
LookSexTransitionAdapter.cs      102 — v232 cross-profile morph-bank flag cleanup
AltFutaHairAdapter.cs             53 — v190 AltFuta INTIMATE lifecycle
QdaroProbe.cs                    321 — v128 observer-only (Pirate Queen)
BodyMorphSplit.cs                456 — Skynet head/body split data
BodyPresetAdapter.cs             729 — body preset apply/rollback
```

Present in the package but **not** in `.cslist` (standalone probe, by design):

```
KaineAnchorProbe.cs               98 — add as plugin on Person#3 in the author scene
```

Restricted APIs: no `System.Reflection`, no `System.IO`; file access via `FileManagerSecure`. OK.

---

## 3. Where the requested subsystems actually live

| Handoff concept | Actual location in v275 |
|---|---|
| NativeFutanariAdapter | `NativeFutanariAdapter.cs` (v141) |
| NativeFutanariHairAdapter | `NativeFutanariHairAdapter.cs` (v181) |
| AltFutaAdapter | `AltFutaAdapter.cs` (v140) + `AltFutaHairAdapter.cs` (v190) |
| ExternalHairAdapter | `ExternalHairAdapter.cs` — **only** Clothing-item hair with `Stopper.ClothingPluginManager` + `CUAClothingAlt`. **Not** used by 2B/SC6. |
| 2B-SC6 / 2B-Kaine CUA hair | `SceneCuaAccessoryAdapter.cs` (`Apply2BSceneHair`, `Sync2BSceneHairAnchors`, `Set2BSceneHairVisible`) |
| 2B family feet | `ModelPluginAdapter.cs` `RestoreExact2BFeetToVariant` (SC6/Kaine), `RestoreAuthored2BFootControls` (2B, 2B Destroyed), `Finalize2BVariantFeetAfterSpawn` |
| 2B family mouth | `ModelPluginAdapter.cs` `RestoreAuthored2BTongueCollision` (TongueControl.tongueCollision) |
| 2B-SC6 skirt | `ModelPluginAdapter.cs` `RestoreAuthored2BSC6Skirt` + `RestoreSC6SkirtMaterial` |
| Head/Gaze | `HeadTrackingAdapter.cs` + `CharacterRouter.ApplyHeadTrackingPolicy` |

### Model-specific gates (all exact string matches)

- `ModelPluginAdapter.cs:1279-1313` — `lookLabel == "2B-Kaine" | "2B-SC6"` **and** source package contains `immyneedscake.Nier-2B-Compendium`. This branch `yield break`s, so generic plugin restore never runs for these two Looks.
- `ModelPluginAdapter.cs:1840-1903` — `"2B"`, `"2B_Destroyed"/"2B Destroyed"`, `"2B_DLC"/"2B DLC"`.
- `SceneCuaAccessoryAdapter.cs:326-346` — `"2B-Kaine"/"2B Kaine"`, `"2B-SC6"/"2B SC6"`, same package gate.
- `CharacterRouter.cs:8478-8495` — post-collision feet finalize for `"2B-Kaine"/"2B-SC6"` on fresh spawn.

Look label = `.vap` file name without `Preset_` and `.vap` (`GetPresetLabel`, `CharacterRouter.cs:10543`).
**There is no `2P` reference anywhere in the code.**

---

## 4–5. The v275 hair-anchor fix

### Load (`Apply2BSceneHair`, `SceneCuaAccessoryAdapter.cs:636-836`)

1. Reads author scene `<package>:/Saves/scene/immyneedscake/2b showcase single.json`.
2. Source atoms:
   - SC6: Person `Person#2`, CUA `CustomUnityAsset`, `linkTo == "Person#2:head"`.
   - Kaine: Person `Person#3`, CUA `CustomUnityAsset_Person#3`, `linkTo == "Person#3:head"`.
3. `assetUrl` (SELF:/ → package-qualified) and `assetName` taken from the author CUA.
4. Creates new CUA atom `<personUid>#CharacterRouter_2B_SC6_Hair` / `…_2B_Kaine_Hair`, tracked in `_managed[personUid]`.
5. Restores author `control` JSON with `linkTo = <personUid>:head`, position/rotation replaced by `head.TransformPoint(localPos)` / `head.rotation * localRot`; copies `scale`; sets `assetUrl`; waits ≤20 s for `assetName` in choices; waits ≤10 s for renderers; re-applies transform; logs `READY` with `headLocalPos/headLocalRot`.

Measured head-local constants (CUA pivot in `head` bone space; the prefab pivot sits near the Person root, hence y ≈ −1.618):

| | localPos | localRot (euler) |
|---|---|---|
| SC6 | (0.00035, −1.61829, 0.00438) | (0.17861, −0.00154, 0.01053) |
| Kaine | (0.00035, −1.61829, 0.00436) | (0.17848, −0.00135, 0.01050) |

### Pose survival (v275 itself)

- `CharacterRouter.LateUpdate()` (`CharacterRouter.cs:27995`) → `Sync2BSceneHairAnchors()` (`SceneCuaAccessoryAdapter.cs:32-72`) **every frame**.
- For each managed atom whose uid is exactly `<personUid>#CharacterRouter_2B_SC6_Hair|…_Kaine_Hair`, it recomputes the target from the live `head` DAZBone and writes `mainController.transform.position/rotation` when drift > 1 mm or > 0.1°.
- Rationale in code: VaM `linkTo` can keep a stale world offset after a Pose Preset restores Person controls; the per-frame head-local override makes the CUA follow the head regardless of linkTo.

### Other lifecycle

- `Set2BSceneHairVisible` (`:78-123`): hides/restores the CUA's renderers when another Head hair is chosen (RESET HEAD keeps it alive).
- `ClearManaged` (`:220-255`): removes the CUA atom on Look change / Person removal (called at `CharacterRouter.cs:2309, 22849, 27577`).
- Callers of `ApplyFromScene`: fresh spawn (`:8453`), reset-to-creation (`:2393`), look switch (`:22980`).

**Classification: model-specific (two hard-coded Looks, one hard-coded scene, hard-coded atom ids and constants). It is NOT shared ExternalHairAdapter logic.**

Provenance note: `KaineAnchorProbe.cs` is hard-coded to `Person#3`/`CustomUnityAsset_Person#3`. The SC6 constants (`Person#2`/`CustomUnityAsset`) were therefore measured with a modified copy that is not in the package.

---

## 6. What 2B-SC6 does besides hair (for comparison with 2P-SC6)

- **Feet** (`RestoreExact2BFeetToVariant`, `ModelPluginAdapter.cs:2232`): hard-coded r/l Foot+Toe `localPosition`, `localRotation`, `jointDriveXTarget`. Comment: values were saved on the regular `"2B"` Person in `2b showcase.json` (with Kaine's lateral X). For SC6 only, toes additionally get `rotationState=On`, `holdRotationSpring` 2000 (R) / 200 (L) from the author scene. **Not measured from SC6's own Person#2 for position/rotation.** Applied after spawn collision restore (`CharacterRouter.cs:8478-8495`).
- **Mouth** (`RestoreAuthored2BTongueCollision`, `:1906`): Appearance presets omit `TongueControl`; author scene stores `tongueCollision=false`. That is the whole 2B mouth fix — no morphs, no jaw.
- **Skirt** (`RestoreAuthored2BSC6Skirt`, `:1975`; `RestoreSC6SkirtMaterial`, `:2028`): replays the author `immyneedscake:2BSkirtPreset` storable from Person#2, then writes hard-coded material values (textures under `2BBodySuit/Textures/`, Gloss 6.471056, Diffuse v 0.7547402, etc.) to `immyneedscake:2BSkirtMaterial2B_Default_Body`. No physics/sim parameters, no fix for the physical overlap.

## 6b. What exists specifically for 2P-SC6

**Nothing.** No `2P` string anywhere. If the Look is a `.vap` named `Preset_2P-SC6.vap`, its label is `2P-SC6` and it bypasses every 2B-family branch:

| Path | 2P-SC6 outcome (code reading) |
|---|---|
| `SceneCuaAccessoryAdapter.ApplyFromScene` | not 2B-Kaine/2B-SC6, not Elowen → no-op |
| `ModelPluginAdapter.ApplyForLook` 2B branch | skipped |
| generic plugin restore | plugin preset match in package, else `FindMatchingScene` (needs score ≥ 100 from scene-stem/atom-id name match; "2psc6" vs "2bshowcasesingle"/"Person#N" is unlikely to match) |
| TongueControl / skirt preset / skirt material / feet | not applied |

This is consistent with all four symptoms, but it only explains why *no repair runs*; it does not prove what the native 2P-SC6 state is. Status: **provisional**.

---

## 7–8. Probe status

- `2PSC6Probe.cs` — **does not exist** in the package.
- `KaineAnchorProbe.cs` — exists; logs head/CUA world+head-local transforms and four foot/toe controls (localRotation, jointDriveXTarget, rotationState) for hard-coded `Person#3`. **Insufficient for 2P-SC6**: hard-coded ids, no mouth, no skirt, no Person hair, no morphs, no plugin state, no routed mode.
- `DumpSceneLookRuntimeState` in CharacterRouter is stubbed (`return;`) and `diagnosticSceneLook` is gated to Nico only.
- 2B-family log lines go to VaM **Message Log / Error Log** via `SuperController.LogMessage/LogError`, not to `diagnostic.log`. `diagnostic.log` (`Saves/PluginData/Gleb/CharacterRouter/diagnostic.log`) is still the path used by DecalMaker/Nico diagnostics.

### Recommendation: no diagnostic build yet

Almost everything that must be compared is serializable state, which VaM already writes:

- **Native**: the author scene JSON inside the source `.var` (readable offline).
- **Routed**: VaM *Save Scene As* after loading 2P-SC6 through CharacterRouter.

Diffing the two Person nodes + CUA atoms covers morphs, TongueControl, foot/toe controls, clothing items, clothing sim/material storables, hair items, plugins, CUA atoms/linkTo/assetUrl/assetName.

A runtime probe is needed **only** if native 2P-SC6 hair turns out to be a scene CUA (to measure the live head-local pivot). That would be a standalone, parameterized copy of the KaineAnchorProbe pattern (not in `.cslist`, no CharacterRouter build).

---

## 9. Handoff vs code discrepancies

| # | Handoff | Code (v275) | What is stale |
|---|---|---|---|
| D1 | 2B/SC6 external hair handled by the `ExternalHairAdapter` concept | Handled by `SceneCuaAccessoryAdapter`; `ExternalHairAdapter` is unrelated (Clothing+CUAClothingAlt) | Handoff imprecise |
| D2 | 2B-SC6 "feet state had to be restored" (implies measured) | Positions/rotations copied from regular "2B" Person + Kaine X; only toe hold settings from SC6 author scene | Handoff incomplete; reuse risk |
| D3 | 2B "mouth state restored" | Mechanism is `TongueControl.tongueCollision=false`, not morphs | Handoff vague |
| D4 | diagnostic.log as the diagnostic channel | 2B/SC6 diagnostics go to VaM Message/Error Log | Handoff partly stale |
| D5 | Probe concept `2PSC6Probe.cs` | Not in package | Never shipped or lost |
| D6 | v275 anchor "measured" | True for hair; SC6 measurement probe variant not shipped | Minor provenance gap |

None of these indicate a wrong build. They are handoff precision issues.

---

## 10. Reuse assessment for 2P-SC6

Potentially reusable (only after the native scene confirms the same mechanism):

- Scene-CUA hair pipeline (`Apply2BSceneHair` + per-frame head-local sync + visibility toggle + `ClearManaged`) — if 2P hair is a separate CUA linked to `<person>:head`.
- `RestoreAuthored2BTongueCollision` — generic "copy one storable from author Person"; reusable if 2P's author Person saves `tongueCollision=false`.
- Skirt *preset-storable* replay pattern — if 2P uses the same `immyneedscake:2BSkirtPreset` storable.

Must NOT be transferred without evidence:

- SC6/Kaine head-local constants (CUA pivot differs per prefab).
- Foot/toe hard-coded values (they are not even SC6's own).
- SC6 skirt material constants (2P textures/colors almost certainly differ).
- Toe holdRotationSpring 2000/200.
- Author atom ids `Person#2` / `CustomUnityAsset` and the scene file name.

## 11. Observed structural limits (not bugs, but relevant for a 2P extension)

- `_managed` holds **one** accessory per Person uid; a Look needing both a CUA hair and another CUA would collide.
- Anchor constants are duplicated in two places (`Sync2BSceneHairAnchors` and `Apply2BSceneHair`).
- Label gates are exact strings; a renamed `.vap` silently falls out of the branch.
