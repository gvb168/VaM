# CharacterRouter — Technical Handoff for Claude

## 0. Purpose of this file

This file is the persistent technical handoff for the Virt-A-Mate 1.x project **CharacterRouter**.

Use it as:
- architectural context;
- a regression checklist;
- a map of already-solved problems;
- a description of the current unfinished task;
- a working protocol for future changes.

**Important:** this file is not a substitute for inspecting the actual `.var` that the user uploads.  
When the code and this handoff disagree, first determine whether the handoff is stale or the uploaded build is from another branch. Do not silently rewrite the architecture to match assumptions.

---

# 1. Project goal

CharacterRouter is a VaM plugin for loading and switching a large number of character Looks while preserving model-specific state that VaM does not always restore correctly by default.

The plugin may need to preserve or reconstruct, depending on the model:

- appearance;
- morphs;
- hair;
- clothing;
- external hair / CUA hair;
- intimate hair;
- CUA accessories;
- model-specific plugins;
- gaze/head-follow behavior;
- feet / heel state;
- mouth / expression state;
- other per-Look quirks.

The project has accumulated many model-specific compatibility fixes, so **regression avoidance is more important than refactoring elegance**.

---

# 2. Core engineering rules

These are hard constraints unless the user explicitly changes them.

## 2.1 Never edit source model packages unless there is no viable alternative

Preferred order:

1. fix in CharacterRouter;
2. fix in a model-specific adapter/module;
3. reuse a mature existing plugin/approach;
4. only as a last resort patch the source model package.

The user explicitly prefers fixes to live in CharacterRouter or dedicated adapters rather than mutating third-party Looks.

## 2.2 Prefer isolated adapters over global heuristics

If a problem is:
- model-specific;
- author-specific;
- asset-type-specific;
- tied to a known plugin family;

then isolate the fix.

Do **not** introduce a global resolver merely because one Look fails.

## 2.3 Preserve exact source/package context

A recurring historical source of bugs was losing the original package/source context of assets.

When cloning or reloading assets:
- preserve exact source package identity;
- preserve the correct internal asset path;
- preserve activation order;
- respect VaM readiness timing.

## 2.4 Diagnose native state vs routed state before changing code

For a difficult Look:

1. measure it in its original/native scene;
2. measure the same Look after loading through CharacterRouter;
3. compare;
4. identify the exact lost/altered state;
5. apply the smallest possible fix.

Do not infer values visually if they can be extracted from the native scene.

## 2.5 Do not combine unrelated fixes into one speculative patch

Feet, mouth, hair, skirt physics, plugin state, CUA parenting, etc. may have different failure mechanisms.

Prefer:
- one proven mechanism;
- one minimal change;
- one regression test.

## 2.6 Avoid restricted VaM APIs/namespaces

A prior build failed compilation because of VaM runtime namespace/security restrictions.

In particular:
- avoid `System.Reflection` / reflection-based designs unless proven safe in VaM;
- do not assume ordinary desktop .NET code is permitted by VaM's runtime sandbox.

## 2.7 Reuse mature third-party solutions when sensible

If another VaM author already solved a hard technical problem well:
- inspect that solution;
- reuse the technique;
- integrate or adapt it if appropriate.

Do not reinvent a complex subsystem simply to keep everything inside one file.

## 2.8 Keep the main CharacterRouter from becoming a monolith

If a capability is cleaner and safer as:
- an adapter;
- helper;
- dedicated module;
- separate plugin;

prefer that separation.

---

# 3. Logging and diagnostics

Historical diagnostic log location:

`Saves/PluginData/Gleb/CharacterRouter/diagnostic.log`

Guidelines:
- diagnostic builds may be verbose;
- stable builds should not spam logs;
- logs should identify atom/look/model, stage, source package, internal path, and relevant state values;
- for comparisons, log native and routed values in a form that can be diffed.

Preferred diagnostic style:

`FEATURE | stage=... | look=... | atom=... | key=value | key=value`

---

# 4. Architectural subsystems already established

The exact class/file names must be verified against the uploaded build, but these concepts are established and should not be collapsed back into one global handler.

## 4.1 Native Futanari handling

There is a dedicated architecture for native Futanari.

Important historical conclusions:
- native Futanari must not be treated as ordinary Female by a blanket gender switch;
- exact-source hair preservation was necessary;
- activation/readiness ordering mattered;
- female head/facial/body hair and male intimate hair may need different handling.

Known confirmed milestone:
- **v115** was a confirmed working point for cross-gender/Futanari hair after introducing a readiness barrier around exact-source clone activation.

Later architecture:
- `NativeFutanariHairAdapter` for cross-gender hair;
- `NativeFutanariAdapter` for native-Futanari behavior as a type.

Do not resurrect old blanket gender mutation approaches.

## 4.2 External hair handling

There is a separate `ExternalHairAdapter` concept for models whose hair is not ordinary VaM hair.

Known use cases include:
- Nico;
- later CUA/external-hair models;
- 2B/SC6-family work.

This adapter should remain separate if possible.

## 4.3 AltFuta

AltFuta must remain architecturally distinct from native Futanari.

Key facts:
- internally the Person remains **Female**;
- AltFuta needs its own adapter / internal type;
- female hair remains native female hair;
- special handling is needed only where male-compatible/intimate assets must map onto AltFuta.

Important diagnostic conclusion:
- `DAZSkinWrap + CustomScalpWrapControl` is **not** a reliable unique detector for AltFuta;
- many ordinary Female pubic hairs share the same structural characteristics;
- structural auto-detection created many false positives.

Therefore:
- do not reintroduce broad structural auto-detection;
- only explicitly known/compatible AltFuta assets should enter the special conversion path.

Historical milestones:
- **v179** confirmed native Futanari intimate hair behavior;
- **v189** proved structural AltFuta auto-detection was too broad;
- **v190** moved toward explicit AltFuta catalog behavior;
- **v192** was a deeper proof-of-concept with synthetic Female hair built from AltFuta-compatible data;
- **v214** continued AltFuta skinwrap/component experimentation.

Treat old experimental conversion code carefully. Do not generalize it without new evidence.

## 4.4 Clothing / CUA / plugin preservation

There is dedicated logic for restoring Look-specific clothing and CUA state.

Important principle:
when extracting/loading a Look from a scene, preserve only the plugins and objects that genuinely belong to the character/look.

Do not blindly copy:
- scene-wide animation plugins;
- environment plugins;
- behavior systems unrelated to the model.

A past confirmed case:
- Pirate Queen required a patched `qdaro.Piercings.3.var` / `CUAClothing.cs` interaction.
- CharacterRouter **v128** plus the patched piercing package was a confirmed stable solution.
- On return to Pirate Queen, the CUA could transiently degrade to `assetName=None / choices=1`, after which the patched CUAClothing logic restarted/recovered it.

Do not remove lifecycle/recovery logic simply because the first load works.

## 4.5 Caelryn-specific handling

A broad Caelryn appearance adapter was considered and rejected because the clothing issue was not common to all Caelryn models.

Confirmed stable point:
- **v131** remained the working baseline instead of adding a global Caelryn fix.

Lesson:
- keep Caelryn exceptions model-specific unless a shared mechanism is demonstrated.

## 4.6 Native Futanari regression baseline

A critical regression period existed around versions 136–139.

Confirmed:
- **v140** restored correct behavior for ordinary male models and native Futanari after isolating `NativeFutanariAdapter + AltFutaAdapter`.

Versions 136–139 should not be treated as known-good references.

---

# 5. Head / gaze architecture

Desired default behavior:

- **head does NOT follow the camera**
- **eyes may continue tracking the camera**

UI concept already established:

`Follow Camera With Head`

Default: `OFF`

Preferred implementation:
a central post-load normalization layer rather than a huge per-Look list.

Known plugin-specific behavior:

## MacGruber.Gaze
Disable the head-following gaze behavior/plugin when the user wants head-follow OFF.

Known Looks:
- Nyakim
- Sienna
- Zara

Zara was useful evidence because `MacGruber.Gaze` was already stored disabled there.

## CheesyFX.BodyLanguage / PoseMe
Do **not** disable the whole BodyLanguage plugin.

For Kimi, only the relevant gaze client should be disabled while preserving the rest of BodyLanguage functionality.

Important:
do not kill unrelated eye/body features merely to stop head tracking.

---

# 6. Known model-specific fixes

## 6.1 Black Widow

Historical issue:
- hair / extra atoms / delete / re-add lifecycle problems.

Confirmed:
- **v239–v240** fixed the lifecycle problem;
- **v240** was specifically confirmed working for load → remove → re-add.

Do not regress this lifecycle behavior.

## 6.2 Elowen

Issue:
- tiara/CUA restoration.

Dependency:
- `Crowns_and_Tiaras.3`

Historical work:
- v241–v244 contained experiments around Elowen tiara placement.

Warning:
old manually guessed offsets should not be treated as a universal solution.
If revisiting Elowen, measure the native scene state.

## 6.3 Sakuri

Historical issue:
- neutral-face work;
- genital decal broke after re-add in **v248**.

If code touching appearance reset/re-add is modified, Sakuri should be included in regression testing.

## 6.4 Victoria

Known issue:
- `Stopper.CUAClothing` exceptions.

Do not assume all CUA failures share the Pirate Queen mechanism.

## 6.5 2B / Kaine / SC6 family

This family is currently especially important.

Recent development included:
- feet / heel state;
- mouth state;
- skirt/material/physics state;
- external/CUA hair;
- hair anchoring across pose changes.

A key recent result:
- **v275** was confirmed to keep the external/CUA hair of **2B-SC6 / 2B-Kaine** attached correctly when changing pose.

This anchoring fix was based on measured native-scene anchors/state rather than arbitrary visual offsets.

Do not replace it with a generic static offset.

---

# 7. Version landmarks / regression guardrails

The exact uploaded branch must always be verified, but the following historical points are useful.

| Version | Status / significance |
|---|---|
| v115 | Confirmed working cross-gender/native-Futanari hair with readiness barrier |
| v128 | Pirate Queen working with patched CUAClothing dependency |
| v131 | Stable point before rejecting overly broad Caelryn fix |
| v136–v139 | Do **not** treat as working baseline; regressions existed |
| v140 | Confirmed ordinary male + native Futanari recovery |
| v179 | Native Futanari intimate hair behavior confirmed |
| v189 | Proved structural AltFuta detection produced false positives |
| v190 | Moved toward explicitly allowed AltFuta assets |
| v192 | Experimental synthetic Female hair for Male→AltFuta proof-of-concept |
| v214 | Later AltFuta skinwrap/component experiment |
| v233 | Important historical base branch |
| v237 | Head-tracking / Kimi diagnostics era |
| v239–v240 | Black Widow lifecycle fix; v240 confirmed |
| v241–v244 | Elowen tiara experiments |
| v248 | Sakuri-related work; genital decal re-add regression was observed |
| v251–v258 | Heel / feet / mouth-related work |
| v261–v270 | Kaine hair/feet and model-adapter work |
| v271 | External-hair switching work |
| v274–v275 | SC6 external/CUA hair anchoring |
| v275 | Latest development branch known from the previous work session |

**Do not assume the uploaded file is really v275 simply because the user says it is. Inspect the package metadata/source and confirm.**

---

# 8. Current unfinished task: 2P-SC6

This is where development stopped.

The model **2P-SC6** currently has four visible problem classes when routed through CharacterRouter:

1. feet / legs state is wrong;
2. skirt state is wrong;
3. hair is missing / baldness occurs;
4. mouth is open when it should not be.

A related model, **2B-SC6**, previously had similar symptoms, but the fixes must not be copied blindly.

## What is already known from 2B-SC6

- feet state had to be restored;
- mouth state had to be restored;
- hair eventually worked;
- v275 keeps the external/CUA hair attached during pose changes;
- skirt material was brought to the correct appearance;
- a physical skirt overlap/penetration problem remained resistant to simple `Reset Simulation`;
- loading a pose from the native scene did not automatically remove the skirt overlap;
- before the proper anchor fix, loading pose could make the hair drift sideways.

Therefore 2P-SC6 may reuse mechanisms, but only after native-state comparison.

---

# 9. Next required diagnostic step

The next action is **diagnosis, not patching**.

A probe concept already existed:

`2PSC6Probe.cs`

If that script exists in the uploaded project, inspect it.

If it does not exist or is incomplete, recreate only the smallest diagnostic tool necessary.

## Required comparison

Collect two comparable snapshots:

### A. Native 2P-SC6 scene
The original scene / source configuration where 2P-SC6 is known to look correct.

### B. CharacterRouter-loaded 2P-SC6
The same model loaded through the current CharacterRouter branch.

The probe should log enough state to explain:

### Hair / baldness
- hair items and exact asset URLs;
- active/enabled state;
- external/CUA atoms;
- CUA asset bundle / asset name;
- parenting/anchor/receiver;
- transforms;
- controller states if relevant;
- whether source atoms/assets exist but are hidden;
- plugin-owned hair state if applicable.

### Mouth
- relevant mouth/jaw morphs;
- mouth/open expression morph values;
- jaw control if applicable;
- relevant pose/expression storables;
- any plugin that modifies mouth state.

### Feet / legs
- heel/foot morphs;
- joint/control rotations if relevant;
- pose state;
- footwear/heel adjustment plugin state;
- toe/foot controls;
- saved/restored storables that differ from native.

### Skirt
- exact clothing item URL;
- enabled state;
- material parameters;
- physics/simulation parameters;
- joint spring/damper settings;
- anatomy / real clothing flags;
- any custom plugin/storage controlling the skirt;
- transform/cloth simulation state if accessible.

The output should make this format possible:

`native value -> routed value -> likely loss mechanism -> minimal fix`

---

# 10. Diagnostic decision rules for 2P-SC6

Do not implement a fix until one of these is demonstrated:

## Hair
Possible classes of failure:
- wrong package/path;
- external atom not restored;
- CUA asset not activated;
- anchor not applied;
- source atom lifecycle problem;
- readiness/timing problem;
- hair exists but hidden/disabled;
- wrong adapter classification.

## Mouth
Possible classes of failure:
- missing morph restore;
- expression/pose overwrite;
- plugin restore ordering;
- morph reset during appearance switching.

## Feet
Possible classes of failure:
- heel state not restored;
- pose overwrites feet;
- model-specific morphs are filtered/reset;
- footwear helper/plugin state lost.

## Skirt
Possible classes of failure:
- wrong clothing item state;
- material mismatch;
- plugin-controlled state lost;
- physics settings differ;
- cloth simulation starts from a bad initial state;
- CharacterRouter restores appearance but not clothing physics storables.

Do not assume all four share a common cause.

---

# 11. Test matrix after any 2P-SC6 fix

At minimum test:

1. load 2P-SC6 from a clean Person;
2. switch away and back;
3. change pose;
4. reset simulation;
5. remove Person and re-add if CharacterRouter supports that workflow;
6. confirm hair remains attached;
7. confirm feet remain correct;
8. confirm mouth remains correct;
9. confirm skirt material remains correct;
10. confirm skirt physics does not regress;
11. confirm no new errors in VaM Error Log;
12. confirm no regression in 2B-SC6 / 2B-Kaine.

If code touches shared lifecycle/appearance logic, additionally spot-check:
- Black Widow;
- Sakuri;
- native Futanari;
- one normal Female;
- one normal Male.

---

# 12. Release discipline

For every new CharacterRouter build:

- increment version;
- document exact changes relative to the previous build;
- preserve all confirmed fixes;
- keep VaM package structure correct;
- validate JSON;
- validate ZIP/VAR integrity;
- avoid restricted APIs;
- avoid unnecessary diagnostic spam;
- do not modify third-party model packages unless explicitly agreed.

When a build is diagnostic-only, say so clearly.

When the user confirms a build works, record that version as a new known-good milestone.

---

# 13. How Claude should behave when continuing this project

After receiving the latest `.var`:

1. inspect it before proposing code changes;
2. identify the actual version/branch;
3. map the existing adapters/classes;
4. locate the 2B-SC6 / Kaine external-hair anchoring code;
5. determine whether `2PSC6Probe.cs` already exists;
6. explain what can be reused for 2P-SC6 and what cannot yet be assumed;
7. request only the minimum additional files needed;
8. produce a diagnostic build only if current logs cannot answer the question;
9. wait for native-vs-routed evidence;
10. implement one proven mechanism at a time.

Do not:
- rewrite the whole plugin;
- merge adapters without need;
- reintroduce rejected heuristics;
- treat historical experimental branches as proven stable;
- guess offsets;
- patch all four 2P-SC6 symptoms in one large speculative change.

---

# 14. First expected output from Claude

After inspecting the uploaded CharacterRouter `.var`, Claude should answer with:

1. the actual version/branch found;
2. the current adapter/module map;
3. the location of the working 2B-SC6 / 2B-Kaine hair-anchor logic;
4. the existing 2P-SC6-specific code, if any;
5. whether `2PSC6Probe.cs` exists and whether it is sufficient;
6. a short list of native-vs-routed measurements still needed;
7. exact user steps in VaM to collect those two diagnostic datasets;
8. **no new build yet**, unless the existing package genuinely lacks the required diagnostics.

---

# 15. Final reminder

This project has a long regression history.

The default development posture is:

**measure → compare → isolate → patch minimally → regression-test**

not:

**observe symptom → invent heuristic → patch globally**.

---

# 16. HANDOFF UPDATE PROTOCOL

This section defines **when and how `CHARACTERROUTER_HANDOFF.md` should be updated**.

The purpose of the handoff is to preserve **confirmed technical knowledge**, not to become a transcript of every experiment.

## 16.1 When to update this file

Update the handoff when at least one of the following becomes true:

1. The user confirms that a new CharacterRouter build works for a specific problem.
2. A previously uncertain mechanism is proven by diagnostics.
3. A new regression is confirmed and must become part of the regression checklist.
4. A new adapter/module becomes part of the accepted architecture.
5. A previously accepted approach is disproven and should no longer be used.
6. The current unfinished task materially changes because one or more subproblems are solved.
7. A new version becomes a meaningful known-good or known-bad landmark.

Do **not** update the handoff merely because:
- a diagnostic build was created;
- an experiment was attempted;
- a hypothesis was suggested;
- a fix appears visually plausible but has not been tested;
- a build compiles but has not been validated in VaM.

## 16.2 Confirmed vs provisional information

Only write something as **confirmed** when the user has actually tested it or the evidence is otherwise conclusive.

Use explicit wording:

- `Confirmed:` for user-validated working behavior.
- `Diagnostic finding:` for a mechanism proven by logs/native-vs-routed comparison.
- `Provisional:` for useful but not yet fully tested conclusions.
- `Rejected:` for approaches shown to be wrong, unsafe, or regression-prone.

Never silently upgrade a hypothesis into a confirmed fact.

## 16.3 What to update after a confirmed milestone

After a successful milestone, check these sections:

### Version landmarks
Add the new version if it is meaningful.

Record:
- version number;
- what changed;
- what the user explicitly confirmed;
- important regressions that were checked.

Example:

`v276 — Confirmed: 2P-SC6 external/CUA hair restores from the native anchor state and remains attached during pose changes. 2B-SC6 regression check passed.`

### Known model-specific fixes
Add or update the relevant model section.

Record:
- the actual failure mechanism;
- the minimal fix;
- whether the logic is model-specific or shared;
- any dependency on a particular adapter.

### Current unfinished task
Remove only the parts that are actually solved.

If 2P-SC6 hair is fixed but skirt physics is not, change:

- hair — solved;
- mouth — unresolved;
- feet — unresolved;
- skirt — unresolved.

Do not replace the whole current-task section with a vague statement such as “2P-SC6 mostly fixed.”

### Regression guardrails
If the new fix touched shared code, add the models/features that must be checked in future builds.

Example:
if a fix changes `ExternalHairAdapter`, add 2B-SC6, 2B-Kaine, and any other confirmed ExternalHair users to the relevant regression checks.

### Rejected approaches
If an experiment proves an approach unreliable, preserve the architectural lesson briefly so it is not rediscovered later.

Example:

`Rejected: static hard-coded CUA hair offsets for SC6-family models. Native anchor state must be measured/restored instead.`

## 16.4 What NOT to record

Do not turn the handoff into a chronological experiment log.

Avoid entries like:

- `v276a tried offset 0.03, failed`
- `v276b tried -0.02, looked better`
- `v276c added two Debug.Log calls`
- every compiler error from every intermediate build

Instead preserve the conclusion:

`Rejected: guessed static offsets did not survive pose changes; use measured native anchors.`

Intermediate experiments belong in:
- source control history;
- working notes;
- conversation context;
- diagnostic logs;

not in the persistent handoff.

## 16.5 Required update workflow

When the user confirms a meaningful milestone, Claude should:

1. summarize what was actually confirmed;
2. identify which handoff sections need modification;
3. edit `CHARACTERROUTER_HANDOFF.md`;
4. preserve all unrelated confirmed history;
5. avoid broad rewriting unless the architecture genuinely changed;
6. show the user a short human-readable diff summary;
7. save the updated handoff alongside the project.

Preferred user-facing summary format:

`HANDOFF UPDATED`
- Added: v276 as confirmed 2P-SC6 hair milestone.
- Updated: 2P-SC6 current task — hair marked solved.
- Added: native-anchor restoration mechanism under 2B/2P SC6 family.
- Added regression check: 2B-SC6 + 2B-Kaine after ExternalHairAdapter changes.
- Unchanged: mouth, feet, skirt remain unresolved.

## 16.6 Safety rule for historical facts

Never delete an old confirmed milestone merely because a newer version exists.

Older known-good versions are valuable regression anchors.

If a later version supersedes an earlier mechanism, write the relationship explicitly rather than erasing the old record.

Example:

`v240 remains the confirmed Black Widow lifecycle baseline; later builds inherit this fix unless a regression is observed.`

## 16.7 Version numbering and confirmation status

A newly created build is **not** automatically a milestone.

Use this distinction:

- `built` = package was generated;
- `loads` = VaM loads it;
- `diagnostic` = it produced the intended evidence;
- `confirmed` = the user tested the target behavior successfully;
- `known-good` = confirmed and sufficiently regression-tested to serve as a baseline.

Only `confirmed` / `known-good` builds should normally be promoted into the main Version landmarks table.

## 16.8 End-of-session handoff maintenance

At the end of a productive development session, Claude should ask itself:

- Did we confirm a new fix?
- Did we disprove an old hypothesis?
- Did the current unresolved task change?
- Did we add a shared-code regression risk?
- Did a new adapter become architecturally accepted?

If yes, update the handoff before ending the session.

If no, leave the handoff unchanged.

## 16.9 User command shortcut

The user may simply say:

`Update the CharacterRouter handoff.`

Interpret this as:

- incorporate only the confirmed conclusions from the current session;
- do not add untested hypotheses;
- do not delete unrelated historical context;
- update version landmarks, model-specific fixes, current unfinished task, and regression guardrails where applicable;
- provide a concise diff summary after editing.


