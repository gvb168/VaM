using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace GlebVAM
{
    // Isolated compatibility adapter for Scene Looks that store appearance overlays
    // in Chokaphi DecalMaker instead of native Appearance storables.
    //
    // CharacterRouter owns catalog/spawn/look selection. This adapter owns only the
    // lifecycle of DecalMaker instances that it creates itself. It deliberately does
    // not copy Timeline, Naturalis, Embody, expression plugins, etc.
    public class DecalMakerAdapter
    {
        private class PluginSpec
        {
            public string sourceSlot;
            public string pluginPath;
            public string sourcePackage;
            public readonly List<JSONClass> states = new List<JSONClass>();
        }

        private class ManagedPlugin
        {
            public string slot;
            public string path;
        }

        private readonly Dictionary<string, List<ManagedPlugin>> _managedByPerson =
            new Dictionary<string, List<ManagedPlugin>>(StringComparer.Ordinal);

        private const string DiagnosticDirectory =
            "Saves/PluginData/Gleb/CharacterRouter";
        private const string DiagnosticPath =
            "Saves/PluginData/Gleb/CharacterRouter/diagnostic.log";
        private readonly List<string> _diagnosticLines = new List<string>();

        public void ResetDiagnostic()
        {
            _diagnosticLines.Clear();
            _diagnosticLines.Add(
                "CharacterRouter v128 CLEAN CACHE + QDARO OBSERVER TEST\n" +
                "Purpose: exact v122 appearance/decal lifecycle; avoid repeated SceneLook SaveJSON and observe qdaro runtime only.\n" +
                "Based on v121 diagnostics: fresh instances store 2/2/2 correctly but only render after a later persistent reuse pass.\n"
            );
            FlushDiagnostic();
        }

        public void DiagnosticNote(string text)
        {
            string stamp = Time.realtimeSinceStartup.ToString("F3");
            _diagnosticLines.Add(
                "[t=" + stamp + "] " + (text ?? "")
            );
        }

        public void FlushDiagnostic()
        {
            try
            {
                FileManagerSecure.CreateDirectory(DiagnosticDirectory);
                FileManagerSecure.WriteAllText(
                    DiagnosticPath,
                    string.Join("\n", _diagnosticLines.ToArray()) + "\n"
                );
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "DecalMakerAdapter v122: diagnostic.log write failed: " +
                    e.Message
                );
            }
        }

        private int CountArray(JSONNode node, string key)
        {
            if (node == null || string.IsNullOrEmpty(key))
                return -1;

            try
            {
                JSONArray arr = node[key].AsArray;
                return arr == null ? 0 : arr.Count;
            }
            catch
            {
                return -1;
            }
        }

        private string DescribeDecalNode(JSONNode node)
        {
            if (node == null)
                return "state=<null>";

            return
                "DecalTorso=" + CountArray(node, "DecalTorso") +
                " DecalHead=" + CountArray(node, "DecalHead") +
                " DecalLimbs=" + CountArray(node, "DecalLimbs");
        }

        private string DescribeTargetState(JSONStorable target)
        {
            if (target == null)
                return "target=<null>";

            try
            {
                JSONClass state = target.GetJSON(true, true, true);
                return DescribeDecalNode(state);
            }
            catch (Exception e)
            {
                return "targetGetJSONError=" + e.Message;
            }
        }

        public IEnumerator ObserveVaMLoading(
            Atom person,
            string context,
            float durationSeconds)
        {
            float start = Time.realtimeSinceStartup;
            float end = start + durationSeconds;
            bool lastLoading =
                SuperController.singleton != null &&
                SuperController.singleton.isLoading;

            DiagnosticNote(
                "LOAD OBSERVER BEGIN | context=" + (context ?? "") +
                " | person=" + (person == null ? "<null>" : person.uid) +
                " | isLoading=" + lastLoading
            );

            float[] checkpoints = new float[] { 0.05f, 0.10f, 0.25f, 0.50f, 1.00f };
            int checkpoint = 0;

            while (Time.realtimeSinceStartup < end)
            {
                bool current =
                    SuperController.singleton != null &&
                    SuperController.singleton.isLoading;

                if (current != lastLoading)
                {
                    DiagnosticNote(
                        "LOAD TRANSITION | context=" + (context ?? "") +
                        " | dt=" +
                        (Time.realtimeSinceStartup - start).ToString("F3") +
                        " | isLoading=" + current
                    );
                    lastLoading = current;
                }

                while (checkpoint < checkpoints.Length &&
                       Time.realtimeSinceStartup - start >= checkpoints[checkpoint])
                {
                    DiagnosticNote(
                        "LOAD CHECKPOINT | context=" + (context ?? "") +
                        " | dt=" + checkpoints[checkpoint].ToString("F2") +
                        " | isLoading=" + current
                    );
                    checkpoint++;
                }

                yield return null;
            }

            DiagnosticNote(
                "LOAD OBSERVER END | context=" + (context ?? "") +
                " | isLoading=" +
                (SuperController.singleton != null &&
                 SuperController.singleton.isLoading)
            );
            FlushDiagnostic();
        }

        public IEnumerator ObserveManagedState(
            Atom person,
            string context,
            float durationSeconds)
        {
            float start = Time.realtimeSinceStartup;
            float[] checkpoints = new float[] { 0.00f, 0.10f, 0.25f, 0.50f, 1.00f };

            for (int c = 0; c < checkpoints.Length; c++)
            {
                float targetTime = start + checkpoints[c];
                while (Time.realtimeSinceStartup < targetTime &&
                       Time.realtimeSinceStartup < start + durationSeconds)
                {
                    yield return null;
                }

                if (person == null)
                {
                    DiagnosticNote(
                        "DECAL SNAPSHOT | context=" + (context ?? "") +
                        " | dt=" + checkpoints[c].ToString("F2") +
                        " | person=<null>"
                    );
                    continue;
                }

                MVRPluginManager manager =
                    person.GetStorableByID("PluginManager") as MVRPluginManager;
                List<ManagedPlugin> managed;
                bool haveManaged =
                    _managedByPerson.TryGetValue(person.uid, out managed) &&
                    managed != null && managed.Count > 0;

                DiagnosticNote(
                    "DECAL SNAPSHOT | context=" + (context ?? "") +
                    " | dt=" + checkpoints[c].ToString("F2") +
                    " | person=" + person.uid +
                    " | managedCount=" +
                    (haveManaged ? managed.Count.ToString() : "0") +
                    " | isLoading=" +
                    (SuperController.singleton != null &&
                     SuperController.singleton.isLoading)
                );

                if (haveManaged)
                {
                    for (int i = 0; i < managed.Count; i++)
                    {
                        ManagedPlugin item = managed[i];
                        if (item == null)
                            continue;

                        string livePath = GetPluginPath(manager, item.slot);
                        string prefix = item.slot + "_";
                        JSONStorable target = null;
                        try
                        {
                            List<string> ids = person.GetStorableIDs();
                            if (ids != null)
                            {
                                for (int j = 0; j < ids.Count; j++)
                                {
                                    string id = ids[j];
                                    if (id != null &&
                                        id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                        id.EndsWith(
                                            "_VAM_Decal_Maker_2.Core",
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        target = person.GetStorableByID(id);
                                        break;
                                    }
                                }
                            }
                        }
                        catch { }

                        DiagnosticNote(
                            "  managed slot=" + (item.slot ?? "") +
                            " | trackedPath=" + (item.path ?? "") +
                            " | livePath=" + (livePath ?? "") +
                            " | slotExists=" + (!string.IsNullOrEmpty(livePath)) +
                            " | " + DescribeTargetState(target)
                        );
                    }
                }
            }

            FlushDiagnostic();
        }

        private bool IsDecalMakerPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return path.IndexOf(
                       "Chokaphi.DecalMaker",
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   path.IndexOf(
                       "/VAM_Decal_Maker/",
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string ResolvePackagePath(string value, string sourcePackage)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(sourcePackage) ||
                    string.Equals(sourcePackage, "Local", StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring("SELF:/".Length);
                }

                return sourcePackage + ":/" + value.Substring("SELF:/".Length);
            }

            bool barePackagePath =
                value.StartsWith("Custom/", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Saves/", StringComparison.OrdinalIgnoreCase);

            if (barePackagePath &&
                !string.IsNullOrEmpty(sourcePackage) &&
                !string.Equals(sourcePackage, "Local", StringComparison.OrdinalIgnoreCase))
            {
                string qualified = sourcePackage + ":/" + value;
                try
                {
                    if (FileManagerSecure.FileExists(qualified))
                        return qualified;
                }
                catch { }
            }

            return value;
        }

        private JSONClass RewriteStatePaths(JSONClass state, string sourcePackage)
        {
            if (state == null)
                return null;

            try
            {
                string raw = state.ToString();

                if (!string.IsNullOrEmpty(sourcePackage) &&
                    !string.Equals(sourcePackage, "Local", StringComparison.OrdinalIgnoreCase))
                {
                    raw = raw.Replace(
                        "\"SELF:/",
                        "\"" + sourcePackage + ":/"
                    );
                }
                else
                {
                    raw = raw.Replace("\"SELF:/", "\"");
                }

                JSONNode parsed = JSON.Parse(raw);
                return parsed == null ? null : parsed.AsObject;
            }
            catch
            {
                return null;
            }
        }

        private JSONNode FindPerson(JSONNode root, string atomId)
        {
            if (root == null || string.IsNullOrEmpty(atomId))
                return null;

            JSONArray atoms = root["atoms"].AsArray;
            if (atoms == null)
                return null;

            for (int i = 0; i < atoms.Count; i++)
            {
                JSONNode atom = atoms[i];
                if (atom == null)
                    continue;

                if (!string.Equals(
                        atom["type"].Value,
                        "Person",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(
                        atom["id"].Value,
                        atomId,
                        StringComparison.Ordinal))
                {
                    return atom;
                }
            }

            return null;
        }

        private List<PluginSpec> ReadSpecs(
            string scenePath,
            string sceneAtomId,
            string sourcePackage)
        {
            List<PluginSpec> result = new List<PluginSpec>();

            if (string.IsNullOrEmpty(scenePath) ||
                string.IsNullOrEmpty(sceneAtomId))
            {
                return result;
            }

            try
            {
                string content = FileManagerSecure.ReadAllText(scenePath);
                if (string.IsNullOrEmpty(content))
                    return result;

                // Scene JSON commonly contains SELF:/ paths that only make sense while
                // the scene remains inside its source VAR.
                if (!string.IsNullOrEmpty(sourcePackage) &&
                    !string.Equals(sourcePackage, "Local", StringComparison.OrdinalIgnoreCase))
                {
                    content = content.Replace(
                        "SELF:/",
                        sourcePackage + ":/"
                    );
                }

                JSONNode root = JSON.Parse(content);
                JSONNode person = FindPerson(root, sceneAtomId);
                if (person == null)
                    return result;

                JSONArray storables = person["storables"].AsArray;
                if (storables == null)
                    return result;

                JSONNode pluginManagerNode = null;
                for (int i = 0; i < storables.Count; i++)
                {
                    JSONNode s = storables[i];
                    if (s != null &&
                        string.Equals(
                            s["id"].Value,
                            "PluginManager",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        pluginManagerNode = s;
                        break;
                    }
                }

                JSONClass plugins = pluginManagerNode == null
                    ? null
                    : pluginManagerNode["plugins"].AsObject;

                if (plugins == null)
                    return result;

                foreach (string slot in plugins.Keys)
                {
                    string rawPath = plugins[slot].Value;
                    if (!IsDecalMakerPath(rawPath))
                        continue;

                    PluginSpec spec = new PluginSpec();
                    spec.sourceSlot = slot;
                    spec.sourcePackage = sourcePackage;
                    spec.pluginPath = ResolvePackagePath(rawPath, sourcePackage);

                    string prefix = slot + "_";
                    for (int i = 0; i < storables.Count; i++)
                    {
                        JSONNode state = storables[i];
                        if (state == null)
                            continue;

                        string id = state["id"].Value;
                        if (string.IsNullOrEmpty(id) ||
                            !id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        JSONNode clonedNode = JSON.Parse(state.ToString());
                        JSONClass cloned = clonedNode == null
                            ? null
                            : clonedNode.AsObject;

                        if (cloned != null)
                            spec.states.Add(cloned);
                    }

                    if (!string.IsNullOrEmpty(spec.pluginPath) &&
                        spec.states.Count > 0)
                    {
                        result.Add(spec);
                    }
                }
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "DecalMakerAdapter: could not inspect source Scene Look: " + e
                );
            }

            return result;
        }

        private HashSet<string> GetSlots(MVRPluginManager manager)
        {
            HashSet<string> result = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

            if (manager == null)
                return result;

            try
            {
                JSONClass state = manager.GetJSON(true, true, true);
                JSONClass plugins = state == null
                    ? null
                    : state["plugins"].AsObject;

                if (plugins != null)
                {
                    foreach (string key in plugins.Keys)
                        result.Add(key);
                }
            }
            catch { }

            return result;
        }

        private string GetPluginPath(MVRPluginManager manager, string slot)
        {
            if (manager == null || string.IsNullOrEmpty(slot))
                return "";

            try
            {
                JSONClass state = manager.GetJSON(true, true, true);
                JSONClass plugins = state == null
                    ? null
                    : state["plugins"].AsObject;

                return plugins == null ? "" : plugins[slot].Value;
            }
            catch
            {
                return "";
            }
        }

        private string FindNewSlot(
            MVRPluginManager manager,
            HashSet<string> before,
            string expectedPath)
        {
            if (manager == null)
                return "";

            try
            {
                JSONClass state = manager.GetJSON(true, true, true);
                JSONClass plugins = state == null
                    ? null
                    : state["plugins"].AsObject;

                if (plugins == null)
                    return "";

                foreach (string key in plugins.Keys)
                {
                    if (before != null && before.Contains(key))
                        continue;

                    string livePath = plugins[key].Value;
                    if (string.IsNullOrEmpty(expectedPath) ||
                        string.IsNullOrEmpty(livePath) ||
                        string.Equals(
                            livePath,
                            expectedPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return key;
                    }
                }
            }
            catch { }

            return "";
        }

        private void Track(string personUid, string slot, string path)
        {
            if (string.IsNullOrEmpty(personUid) ||
                string.IsNullOrEmpty(slot) ||
                string.IsNullOrEmpty(path))
            {
                return;
            }

            List<ManagedPlugin> list;
            if (!_managedByPerson.TryGetValue(personUid, out list))
            {
                list = new List<ManagedPlugin>();
                _managedByPerson[personUid] = list;
            }

            for (int i = 0; i < list.Count; i++)
            {
                ManagedPlugin existing = list[i];
                if (existing != null &&
                    string.Equals(existing.slot, slot, StringComparison.OrdinalIgnoreCase))
                {
                    existing.path = path;
                    return;
                }
            }

            ManagedPlugin item = new ManagedPlugin();
            item.slot = slot;
            item.path = path;
            list.Add(item);
        }

        public bool HasManagedVisuals(Atom person)
        {
            if (person == null || string.IsNullOrEmpty(person.uid))
                return false;

            List<ManagedPlugin> list;
            if (!_managedByPerson.TryGetValue(person.uid, out list) ||
                list == null || list.Count == 0)
            {
                return false;
            }

            MVRPluginManager manager =
                person.GetStorableByID("PluginManager") as MVRPluginManager;
            if (manager == null)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                ManagedPlugin item = list[i];
                if (item == null || string.IsNullOrEmpty(item.slot))
                    continue;

                string livePath = GetPluginPath(manager, item.slot);
                if (!string.IsNullOrEmpty(livePath) &&
                    string.Equals(
                        livePath,
                        item.path,
                        StringComparison.OrdinalIgnoreCase) &&
                    FindManagedCoreStorable(person, item.slot) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private JSONStorable FindManagedCoreStorable(Atom person, string slot)
        {
            if (person == null || string.IsNullOrEmpty(slot))
                return null;

            string prefix = slot + "_";
            try
            {
                List<string> ids = person.GetStorableIDs();
                if (ids == null)
                    return null;

                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];
                    if (id != null &&
                        id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        id.EndsWith(
                            "_VAM_Decal_Maker_2.Core",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return person.GetStorableByID(id);
                    }
                }
            }
            catch { }

            return null;
        }

        private ManagedPlugin FindReusableManaged(
            Atom person,
            MVRPluginManager manager,
            string expectedPath,
            HashSet<string> reservedSlots)
        {
            if (person == null ||
                manager == null ||
                string.IsNullOrEmpty(person.uid) ||
                string.IsNullOrEmpty(expectedPath))
            {
                return null;
            }

            List<ManagedPlugin> list;
            if (!_managedByPerson.TryGetValue(person.uid, out list) || list == null)
                return null;

            for (int i = 0; i < list.Count; i++)
            {
                ManagedPlugin item = list[i];
                if (item == null || string.IsNullOrEmpty(item.slot))
                    continue;

                if (reservedSlots != null && reservedSlots.Contains(item.slot))
                    continue;

                string livePath = GetPluginPath(manager, item.slot);
                if (string.IsNullOrEmpty(livePath))
                    continue;

                if (!string.Equals(
                        livePath,
                        expectedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return item;
            }

            return null;
        }

        // Hide adapter-owned overlays without destroying DecalMaker. DecalMaker 57
        // owns GPU/render state that is expensive and lifecycle-sensitive to rebuild.
        // Keeping one instance alive lets a later Scene Look restore frames into the
        // same initialized renderer graph.
        public void ClearVisualState(Atom person)
        {
            if (person == null || string.IsNullOrEmpty(person.uid))
                return;

            List<ManagedPlugin> list;
            if (!_managedByPerson.TryGetValue(person.uid, out list) ||
                list == null || list.Count == 0)
            {
                return;
            }

            MVRPluginManager manager =
                person.GetStorableByID("PluginManager") as MVRPluginManager;
            if (manager == null)
                return;

            for (int i = 0; i < list.Count; i++)
            {
                ManagedPlugin item = list[i];
                if (item == null || string.IsNullOrEmpty(item.slot))
                    continue;

                string livePath = GetPluginPath(manager, item.slot);
                if (string.IsNullOrEmpty(livePath) ||
                    !string.Equals(
                        livePath,
                        item.path,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                JSONStorable target = FindManagedCoreStorable(person, item.slot);
                if (target == null)
                    continue;

                try
                {
                    if (target.GetAction("Clear All Frames") != null)
                    {
                        target.CallAction("Clear All Frames");
                        DiagnosticNote(
                            "DECAL PERSIST CLEAR | person=" + person.uid +
                            " | slot=" + item.slot +
                            " | " + DescribeTargetState(target)
                        );
                    }
                }
                catch (Exception e)
                {
                    SuperController.LogError(
                        "DecalMakerAdapter: could not clear persistent DecalMaker frames: " + e
                    );
                }
            }

            FlushDiagnostic();
        }

        public void ClearManaged(Atom person)
        {
            if (person == null || string.IsNullOrEmpty(person.uid))
                return;

            DiagnosticNote(
                "CLEAR MANAGED BEGIN | person=" + person.uid
            );

            List<ManagedPlugin> list;
            if (!_managedByPerson.TryGetValue(person.uid, out list) ||
                list == null)
            {
                return;
            }

            try
            {
                MVRPluginManager manager =
                    person.GetStorableByID("PluginManager") as MVRPluginManager;

                if (manager != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        ManagedPlugin item = list[i];
                        if (item == null || string.IsNullOrEmpty(item.slot))
                            continue;

                        string livePath = GetPluginPath(manager, item.slot);
                        if (!string.IsNullOrEmpty(livePath) &&
                            string.Equals(
                                livePath,
                                item.path,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            try { manager.RemovePluginWithUID(item.slot); }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "DecalMakerAdapter: managed plugin cleanup failed: " + e
                );
            }

            _managedByPerson.Remove(person.uid);
            DiagnosticNote(
                "CLEAR MANAGED END | person=" + person.uid
            );
        }

        public void ForgetPerson(string personUid)
        {
            if (!string.IsNullOrEmpty(personUid))
                _managedByPerson.Remove(personUid);
        }

        private IEnumerator WaitForVaMIdle(float timeoutSeconds)
        {
            // AppearancePresets.LoadPreset can raise SuperController.isLoading
            // asynchronously after the CallAction returns. If we inspect isLoading
            // in the same frame, a Look switch can look idle and DecalMaker can
            // restore its overlays before the native Appearance has finished, after
            // which the Appearance load overwrites the rendered skin. Cross at least
            // one render boundary first, then wait for the actual VaM load cycle.
            yield return new WaitForEndOfFrame();

            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (SuperController.singleton != null &&
                   SuperController.singleton.isLoading &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }

        public IEnumerator ApplyFromScene(
            Atom person,
            string scenePath,
            string sceneAtomId,
            string sourcePackage)
        {
            if (person == null)
                yield break;

            DiagnosticNote(
                "DECAL APPLY BEGIN | person=" + person.uid +
                " | sceneAtomId=" + (sceneAtomId ?? "") +
                " | sourcePackage=" + (sourcePackage ?? "") +
                " | isLoading=" +
                (SuperController.singleton != null &&
                 SuperController.singleton.isLoading)
            );

            List<PluginSpec> specs = ReadSpecs(
                scenePath,
                sceneAtomId,
                sourcePackage
            );

            if (specs == null || specs.Count == 0)
            {
                DiagnosticNote("DECAL APPLY ABORT | specs=0");
                FlushDiagnostic();
                yield break;
            }

            DiagnosticNote(
                "DECAL SPECS | count=" + specs.Count
            );
            for (int di = 0; di < specs.Count; di++)
            {
                PluginSpec ds = specs[di];
                if (ds == null)
                    continue;
                DiagnosticNote(
                    "  spec#" + di +
                    " sourceSlot=" + (ds.sourceSlot ?? "") +
                    " path=" + (ds.pluginPath ?? "") +
                    " states=" + ds.states.Count
                );
                for (int dj = 0; dj < ds.states.Count; dj++)
                {
                    JSONClass state = ds.states[dj];
                    DiagnosticNote(
                        "    sourceState id=" +
                        (state == null ? "<null>" : state["id"].Value) +
                        " | " + DescribeDecalNode(state)
                    );
                }
            }

            // Do not destroy an adapter-owned DecalMaker here. v120 proved that the
            // restored 2/2/2 state survives, while the visual failure happens after a
            // teardown/recreation cycle. v122 retains the v121 persistent live matching slot.
            HashSet<string> reservedManagedSlots = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

            DiagnosticNote(
                "DECAL BEFORE WAIT IDLE | isLoading=" +
                (SuperController.singleton != null &&
                 SuperController.singleton.isLoading)
            );
            yield return WaitForVaMIdle(20f);
            DiagnosticNote(
                "DECAL AFTER WAIT IDLE | isLoading=" +
                (SuperController.singleton != null &&
                 SuperController.singleton.isLoading)
            );
            yield return null;
            yield return new WaitForEndOfFrame();
            DiagnosticNote(
                "DECAL AFTER EXTRA FRAME+EOF | isLoading=" +
                (SuperController.singleton != null &&
                 SuperController.singleton.isLoading)
            );

            MVRPluginManager manager =
                person.GetStorableByID("PluginManager") as MVRPluginManager;

            if (manager == null)
            {
                SuperController.LogError(
                    "DecalMakerAdapter: selected Person has no PluginManager."
                );
                yield break;
            }

            for (int i = 0; i < specs.Count; i++)
            {
                PluginSpec spec = specs[i];
                if (spec == null ||
                    string.IsNullOrEmpty(spec.pluginPath) ||
                    spec.states.Count == 0)
                {
                    continue;
                }

                try
                {
                    if (!FileManagerSecure.FileExists(spec.pluginPath))
                    {
                        SuperController.LogError(
                            "DecalMakerAdapter: required plugin is missing: " +
                            spec.pluginPath
                        );
                        continue;
                    }
                }
                catch { }

                ManagedPlugin reusable = FindReusableManaged(
                    person,
                    manager,
                    spec.pluginPath,
                    reservedManagedSlots
                );

                string targetSlot = reusable == null ? "" : reusable.slot;
                bool createdNewSlot = false;

                if (!string.IsNullOrEmpty(targetSlot))
                {
                    reservedManagedSlots.Add(targetSlot);
                    DiagnosticNote(
                        "DECAL PERSIST REUSE | person=" + person.uid +
                        " | slot=" + targetSlot +
                        " | path=" + spec.pluginPath
                    );
                }
                else
                {
                    HashSet<string> beforeSlots = GetSlots(manager);
                    var createdPlugin = manager.CreatePlugin();
                    if (createdPlugin == null)
                    {
                        SuperController.LogError(
                            "DecalMakerAdapter: could not create a plugin slot."
                        );
                        continue;
                    }

                    targetSlot = FindNewSlot(manager, beforeSlots, "");
                    DiagnosticNote(
                        "DECAL CREATE SLOT | initialTargetSlot=" +
                        (targetSlot ?? "") +
                        " | expectedPath=" + (spec.pluginPath ?? "")
                    );

                    try
                    {
                        if (createdPlugin.pluginURLJSON != null)
                            createdPlugin.pluginURLJSON.val = spec.pluginPath;
                    }
                    catch (Exception e)
                    {
                        if (!string.IsNullOrEmpty(targetSlot))
                        {
                            try { manager.RemovePluginWithUID(targetSlot); }
                            catch { }
                        }

                        SuperController.LogError(
                            "DecalMakerAdapter: could not assign plugin URL: " + e
                        );
                        continue;
                    }

                    float slotDeadline = Time.realtimeSinceStartup + 5f;
                    while (string.IsNullOrEmpty(targetSlot) &&
                           Time.realtimeSinceStartup < slotDeadline)
                    {
                        targetSlot = FindNewSlot(
                            manager,
                            beforeSlots,
                            spec.pluginPath
                        );

                        if (string.IsNullOrEmpty(targetSlot))
                            yield return null;
                    }

                    if (string.IsNullOrEmpty(targetSlot))
                    {
                        SuperController.LogError(
                            "DecalMakerAdapter: could not identify created plugin slot."
                        );
                        continue;
                    }

                    createdNewSlot = true;
                    reservedManagedSlots.Add(targetSlot);
                }

                bool restoredAny = false;

                for (int s = 0; s < spec.states.Count; s++)
                {
                    JSONClass sourceState = spec.states[s];
                    if (sourceState == null)
                        continue;

                    string sourceId = sourceState["id"].Value;
                    if (string.IsNullOrEmpty(sourceId) ||
                        string.IsNullOrEmpty(spec.sourceSlot) ||
                        !sourceId.StartsWith(
                            spec.sourceSlot,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string targetId =
                        targetSlot + sourceId.Substring(spec.sourceSlot.Length);

                    JSONStorable target = null;
                    float storableDeadline = Time.realtimeSinceStartup + 12f;
                    while (target == null &&
                           Time.realtimeSinceStartup < storableDeadline)
                    {
                        target = person.GetStorableByID(targetId);
                        if (target == null)
                            yield return null;
                    }

                    if (target == null)
                    {
                        DiagnosticNote(
                            "DECAL TARGET MISSING | id=" + targetId
                        );
                        SuperController.LogError(
                            "DecalMakerAdapter: plugin storable did not appear: " +
                            targetId
                        );
                        continue;
                    }

                    DiagnosticNote(
                        "DECAL TARGET APPEARED | id=" + targetId +
                        " | beforeRestore " + DescribeTargetState(target)
                    );

                    // DecalMaker 57 does NOT expose a "PerformLoad" action.
                    // Its Core.Init registers "Load Preset File" and "Clear All Frames",
                    // while Core.RestoreFromJSON directly calls DMFramesManager.DeSerialize.
                    // Use one of those real public storables as the initialization barrier.
                    float readyDeadline = Time.realtimeSinceStartup + 12f;
                    bool ready = false;
                    while (!ready && Time.realtimeSinceStartup < readyDeadline)
                    {
                        try
                        {
                            ready =
                                target.GetUrlJSONParam("Load Preset File") != null &&
                                target.GetAction("Clear All Frames") != null;
                        }
                        catch { ready = false; }

                        if (!ready)
                            yield return null;
                    }

                    if (!ready)
                    {
                        DiagnosticNote(
                            "DECAL READY TIMEOUT | id=" + targetId
                        );
                        SuperController.LogError(
                            "DecalMakerAdapter: DecalMaker 57 did not become ready: " +
                            targetId
                        );
                        continue;
                    }

                    DiagnosticNote(
                        "DECAL READY | id=" + targetId +
                        " | state " + DescribeTargetState(target)
                    );

                    JSONClass rewritten = RewriteStatePaths(
                        sourceState,
                        spec.sourcePackage
                    );
                    if (rewritten == null)
                        continue;

                    rewritten["id"] = targetId;
                    DiagnosticNote(
                        "DECAL REWRITE | id=" + targetId +
                        " | desired " + DescribeDecalNode(rewritten)
                    );

                    bool restored = false;
                    try
                    {
                        // Fresh adapter-owned instances normally contain no frames, but
                        // clearing through DecalMaker's real public action makes repeated
                        // restores deterministic without touching any unrelated plugin.
                        target.CallAction("Clear All Frames");
                        DiagnosticNote(
                            "DECAL AFTER CLEAR | id=" + targetId +
                            " | " + DescribeTargetState(target)
                        );

                        // In DecalMaker 57 this is the actual load path: Core.RestoreFromJSON
                        // immediately forwards the stored DecalTorso/Head/Limbs arrays to
                        // DMFramesManager.DeSerialize. BaseRender.Init explicitly supports
                        // data being deserialized before its renderers finish initializing.
                        target.RestoreFromJSON(
                            rewritten,
                            true,
                            true,
                            null,
                            false
                        );
                        DiagnosticNote(
                            "DECAL AFTER RESTORE | id=" + targetId +
                            " | " + DescribeTargetState(target)
                        );
                        restored = true;
                    }
                    catch (Exception e)
                    {
                        SuperController.LogError(
                            "DecalMakerAdapter: could not restore state " +
                            targetId + ": " + e
                        );
                    }

                    if (!restored)
                        continue;

                    restoredAny = true;

                    // Texture decoding/rendering is asynchronous inside DecalMaker. Do not
                    // fake a nonexistent completion action; simply yield a couple of render
                    // boundaries and let DecalMaker's own DMImageLoader/BaseRender finish.
                    yield return null;
                    yield return new WaitForEndOfFrame();
                }

                if (restoredAny)
                {
                    string livePath = GetPluginPath(manager, targetSlot);
                    if (string.IsNullOrEmpty(livePath))
                        livePath = spec.pluginPath;

                    Track(person.uid, targetSlot, livePath);
                    DiagnosticNote(
                        "DECAL TRACKED | person=" + person.uid +
                        " | slot=" + targetSlot +
                        " | livePath=" + (livePath ?? "")
                    );

                    // Give DecalMaker a few render/update boundaries to rebuild the
                    // final merged textures before CharacterRouter unhides or continues.
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    yield return null;
                }
                else if (createdNewSlot)
                {
                    try { manager.RemovePluginWithUID(targetSlot); }
                    catch { }
                }
            }

            DiagnosticNote(
                "DECAL APPLY END | person=" + person.uid +
                " | isLoading=" +
                (SuperController.singleton != null &&
                 SuperController.singleton.isLoading)
            );
            FlushDiagnostic();
        }
    }
}
