using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace GlebVAM
{
    // Isolated v129 adapter for Person plugins that belong to a model/Look.
    //
    // Ownership rule:
    // - creates its own plugin slots;
    // - removes only slots it created;
    // - never overwrites or removes a user's pre-existing plugin.
    //
    // Source rule:
    // - Scene Look: use the exact source scene + Person;
    // - standalone Appearance: search the same VAR for a confidently name-matched
    //   showcase scene (scene filename or Person id must match the Look name).
    //
    // DecalMaker is excluded because DecalMakerAdapter owns its special persistent
    // renderer lifecycle. Common scene-control utilities are excluded as well.
    public class ModelPluginAdapter
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

        private class SceneMatch
        {
            public string scenePath;
            public string atomId;
            public int score;
        }

        private readonly Dictionary<string, List<ManagedPlugin>> _managedByPerson =
            new Dictionary<string, List<ManagedPlugin>>(StringComparer.Ordinal);

        private readonly Dictionary<string, SceneMatch> _sceneMatchCache =
            new Dictionary<string, SceneMatch>(StringComparer.OrdinalIgnoreCase);

        // v133: matching Person Plugin Presets are stronger evidence than a
        // showcase scene. They are author-saved model configuration and may
        // legitimately contain utilities (e.g. ColliderEditor) that are filtered
        // when discovered only incidentally inside a scene.
        private readonly Dictionary<string, string> _pluginPresetCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private string NormalizeName(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            System.Text.StringBuilder b = new System.Text.StringBuilder();
            string lower = value.ToLowerInvariant();

            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                if (char.IsLetterOrDigit(c))
                    b.Append(c);
            }

            return b.ToString();
        }

        private string GetStem(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            string p = path.Replace('\\', '/');
            int slash = p.LastIndexOf('/');
            string name = slash >= 0 ? p.Substring(slash + 1) : p;
            int dot = name.LastIndexOf('.');
            if (dot > 0)
                name = name.Substring(0, dot);
            return name;
        }

        // VaM accepts some old scene JSON that contains trailing commas.
        private string RemoveTrailingCommas(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            System.Text.StringBuilder result =
                new System.Text.StringBuilder(text.Length);

            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inString)
                {
                    result.Append(c);

                    if (escaped)
                        escaped = false;
                    else if (c == '\\')
                        escaped = true;
                    else if (c == '"')
                        inString = false;

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    result.Append(c);
                    continue;
                }

                if (c == ',')
                {
                    int j = i + 1;
                    while (j < text.Length && char.IsWhiteSpace(text[j]))
                        j++;

                    if (j < text.Length &&
                        (text[j] == ']' || text[j] == '}'))
                    {
                        continue;
                    }
                }

                result.Append(c);
            }

            return result.ToString();
        }

        private JSONNode ReadScene(string scenePath, string sourcePackage)
        {
            if (string.IsNullOrEmpty(scenePath))
                return null;

            try
            {
                string content = FileManagerSecure.ReadAllText(scenePath);
                if (string.IsNullOrEmpty(content))
                    return null;

                if (!string.IsNullOrEmpty(sourcePackage) &&
                    !string.Equals(
                        sourcePackage,
                        "Local",
                        StringComparison.OrdinalIgnoreCase))
                {
                    content = content.Replace(
                        "SELF:/",
                        sourcePackage + ":/"
                    );
                }

                content = RemoveTrailingCommas(content);
                return JSON.Parse(content);
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "ModelPluginAdapter: could not read " +
                    scenePath + ": " + e.Message
                );
                return null;
            }
        }

        private List<JSONNode> GetPersons(JSONNode root)
        {
            List<JSONNode> result = new List<JSONNode>();
            if (root == null)
                return result;

            JSONArray atoms = root["atoms"].AsArray;
            if (atoms == null)
                return result;

            for (int i = 0; i < atoms.Count; i++)
            {
                JSONNode atom = atoms[i];
                if (atom == null)
                    continue;

                if (string.Equals(
                        atom["type"].Value,
                        "Person",
                        StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(atom);
                }
            }

            return result;
        }

        private JSONNode FindPerson(JSONNode root, string atomId)
        {
            if (root == null || string.IsNullOrEmpty(atomId))
                return null;

            List<JSONNode> persons = GetPersons(root);

            for (int i = 0; i < persons.Count; i++)
            {
                JSONNode person = persons[i];
                if (string.Equals(
                        person["id"].Value,
                        atomId,
                        StringComparison.Ordinal))
                {
                    return person;
                }
            }

            return null;
        }

        private void CollectSceneFiles(
            string root,
            List<string> result,
            int depth)
        {
            if (string.IsNullOrEmpty(root) ||
                result == null ||
                depth > 10 ||
                result.Count >= 250)
            {
                return;
            }

            try
            {
                var files = FileManagerSecure.GetFiles(root, "*.json");
                foreach (string file in files)
                {
                    if (result.Count >= 250)
                        break;

                    result.Add(file);
                }
            }
            catch { }

            if (result.Count >= 250)
                return;

            try
            {
                var dirs = FileManagerSecure.GetDirectories(root, "*");
                foreach (string dir in dirs)
                {
                    if (result.Count >= 250)
                        break;

                    CollectSceneFiles(dir, result, depth + 1);
                }
            }
            catch { }
        }

        private int ScoreMatch(
            string lookLabel,
            string scenePath,
            JSONNode person,
            int personCount)
        {
            string look = NormalizeName(lookLabel);
            if (string.IsNullOrEmpty(look))
                return 0;

            string scene = NormalizeName(GetStem(scenePath));
            string atom = person == null
                ? ""
                : NormalizeName(person["id"].Value);

            int score = 0;

            if (!string.IsNullOrEmpty(scene))
            {
                if (scene == look)
                    score += 220;
                else if (scene.Contains(look) || look.Contains(scene))
                    score += 120;
            }

            if (!string.IsNullOrEmpty(atom))
            {
                if (atom == look)
                    score += 200;
                else if (atom.Contains(look) || look.Contains(atom))
                    score += 100;
            }

            if (personCount == 1)
                score += 20;

            return score;
        }

        private string GetPackageModelStem(string sourcePackage)
        {
            if (string.IsNullOrEmpty(sourcePackage))
                return "";

            string s = sourcePackage;
            int first = s.IndexOf('.');
            if (first >= 0 && first + 1 < s.Length)
                s = s.Substring(first + 1);

            int last = s.LastIndexOf('.');
            if (last > 0)
            {
                int version;
                if (int.TryParse(s.Substring(last + 1), out version))
                    s = s.Substring(0, last);
            }

            return s;
        }

        private string SimplifyModelName(string value)
        {
            string n = NormalizeName(value);
            if (string.IsNullOrEmpty(n))
                return "";

            if (n.StartsWith("preset", StringComparison.Ordinal))
                n = n.Substring("preset".Length);

            string[] suffixes = new string[]
            {
                "pluginspresets",
                "pluginspreset",
                "pluginpreset",
                "plugins",
                "appearance",
                "look"
            };

            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < suffixes.Length; i++)
                {
                    if (n.EndsWith(
                            suffixes[i],
                            StringComparison.Ordinal) &&
                        n.Length > suffixes[i].Length)
                    {
                        n = n.Substring(
                            0,
                            n.Length - suffixes[i].Length
                        );
                        changed = true;
                        break;
                    }
                }
            }

            return n;
        }

        private void CollectPluginPresetFiles(
            string root,
            List<string> result,
            int depth)
        {
            if (string.IsNullOrEmpty(root) ||
                result == null ||
                depth > 8 ||
                result.Count >= 100)
            {
                return;
            }

            try
            {
                var files = FileManagerSecure.GetFiles(root, "*.vap");
                foreach (string file in files)
                {
                    if (result.Count >= 100)
                        break;
                    result.Add(file);
                }
            }
            catch { }

            if (result.Count >= 100)
                return;

            try
            {
                var dirs = FileManagerSecure.GetDirectories(root, "*");
                foreach (string dir in dirs)
                {
                    if (result.Count >= 100)
                        break;

                    CollectPluginPresetFiles(
                        dir,
                        result,
                        depth + 1
                    );
                }
            }
            catch { }
        }

        private string FindMatchingPluginPreset(
            string sourcePackage,
            string lookLabel)
        {
            if (string.IsNullOrEmpty(sourcePackage) ||
                string.Equals(
                    sourcePackage,
                    "Local",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            string cacheKey =
                sourcePackage + "|" + SimplifyModelName(lookLabel);

            string cached;
            if (_pluginPresetCache.TryGetValue(cacheKey, out cached))
                return cached ?? "";

            List<string> candidates = new List<string>();
            CollectPluginPresetFiles(
                sourcePackage + ":/Custom/Atom/Person/Plugins",
                candidates,
                0
            );

            if (candidates.Count == 0)
            {
                _pluginPresetCache[cacheKey] = "";
                return "";
            }

            string lookCore = SimplifyModelName(lookLabel);
            string packageCore =
                SimplifyModelName(
                    GetPackageModelStem(sourcePackage)
                );

            int bestScore = 0;
            string best = "";
            bool tie = false;

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = candidates[i];
                string presetCore =
                    SimplifyModelName(
                        GetStem(candidate)
                    );

                int score = 0;

                if (!string.IsNullOrEmpty(lookCore) &&
                    presetCore == lookCore)
                {
                    score += 320;
                }
                else if (!string.IsNullOrEmpty(lookCore) &&
                         (presetCore.Contains(lookCore) ||
                          lookCore.Contains(presetCore)))
                {
                    score += 180;
                }

                if (!string.IsNullOrEmpty(packageCore) &&
                    presetCore == packageCore)
                {
                    score += 280;
                }
                else if (!string.IsNullOrEmpty(packageCore) &&
                         (presetCore.Contains(packageCore) ||
                          packageCore.Contains(presetCore)))
                {
                    score += 140;
                }

                // Johnny's Appearance is deliberately named "no AltFuta":
                // VaM Appearance presets cannot carry his penis plugin. The
                // same author supplies it separately as an AltFuta PluginPreset.
                // The normal substring scorer cannot connect these opposing
                // suffixes, so match this confirmed pair within the same VAR.
                if (string.Equals(sourcePackage,
                        "Geraldo.Johnny_Silverman.3",
                        StringComparison.OrdinalIgnoreCase) &&
                    lookCore == "johnnysilvermannoaltfuta" &&
                    presetCore == "johnnysilvermanaltfuta")
                {
                    score += 500;
                }

                if (candidates.Count == 1)
                    score += 20;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                    tie = false;
                }
                else if (score == bestScore &&
                         score > 0 &&
                         !string.Equals(
                             best,
                             candidate,
                             StringComparison.OrdinalIgnoreCase))
                {
                    tie = true;
                }
            }

            // Require strong model-name/package-name evidence. Do not infer from
            // a generic single plugin preset with no name relationship.
            if (bestScore < 200 || tie)
                best = "";

            _pluginPresetCache[cacheKey] = best;

            if (!string.IsNullOrEmpty(best))
            {
                SuperController.LogMessage(
                    "ModelPluginAdapter v133: matched Person Plugin Preset " +
                    lookLabel + " -> " + best +
                    " (score " + bestScore + ")"
                );
            }

            return best;
        }

        private JSONNode ReadPresetRoot(
            string presetPath,
            string sourcePackage)
        {
            if (string.IsNullOrEmpty(presetPath))
                return null;

            try
            {
                string content =
                    FileManagerSecure.ReadAllText(presetPath);

                if (string.IsNullOrEmpty(content))
                    return null;

                if (!string.IsNullOrEmpty(sourcePackage) &&
                    !string.Equals(
                        sourcePackage,
                        "Local",
                        StringComparison.OrdinalIgnoreCase))
                {
                    content = content.Replace(
                        "SELF:/",
                        sourcePackage + ":/"
                    );
                }

                content = RemoveTrailingCommas(content);
                return JSON.Parse(content);
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "ModelPluginAdapter v133: could not read plugin preset " +
                    presetPath + ": " + e.Message
                );
                return null;
            }
        }

        private List<PluginSpec> ReadPluginPresetSpecs(
            string presetPath,
            string sourcePackage)
        {
            List<PluginSpec> result = new List<PluginSpec>();

            JSONNode root =
                ReadPresetRoot(
                    presetPath,
                    sourcePackage
                );

            if (root == null)
                return result;

            JSONArray storables = root["storables"].AsArray;
            if (storables == null)
                return result;

            JSONNode managerNode = null;

            for (int i = 0; i < storables.Count; i++)
            {
                JSONNode storable = storables[i];
                if (storable != null &&
                    string.Equals(
                        storable["id"].Value,
                        "PluginManager",
                        StringComparison.OrdinalIgnoreCase))
                {
                    managerNode = storable;
                    break;
                }
            }

            JSONClass plugins =
                managerNode == null
                ? null
                : managerNode["plugins"].AsObject;

            if (plugins == null)
                return result;

            foreach (string slot in plugins.Keys)
            {
                string pluginPath =
                    ResolvePackagePath(
                        plugins[slot].Value,
                        sourcePackage
                    );

                if (string.IsNullOrEmpty(pluginPath))
                    continue;

                // DecalMaker still has its dedicated lifecycle adapter.
                if (IsDecalMakerPath(pluginPath))
                    continue;

                // IMPORTANT: do NOT call IsSceneUtilityPath here. A matching
                // author-saved Person Plugin Preset is explicit model intent.
                PluginSpec spec = new PluginSpec();
                spec.sourceSlot = slot;
                spec.pluginPath = pluginPath;
                spec.sourcePackage = sourcePackage;

                string prefix = slot + "_";

                for (int i = 0; i < storables.Count; i++)
                {
                    JSONNode state = storables[i];
                    if (state == null)
                        continue;

                    string id = state["id"].Value;

                    if (string.IsNullOrEmpty(id) ||
                        !id.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    JSONNode clonedNode =
                        JSON.Parse(state.ToString());

                    JSONClass cloned =
                        clonedNode == null
                        ? null
                        : clonedNode.AsObject;

                    if (cloned != null)
                        spec.states.Add(cloned);
                }

                result.Add(spec);
            }

            return result;
        }

        private SceneMatch FindMatchingScene(
            string sourcePackage,
            string lookLabel)
        {
            if (string.IsNullOrEmpty(sourcePackage) ||
                string.Equals(
                    sourcePackage,
                    "Local",
                    StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(lookLabel))
            {
                return null;
            }

            string cacheKey =
                sourcePackage + "|" + NormalizeName(lookLabel);

            SceneMatch cached;
            if (_sceneMatchCache.TryGetValue(cacheKey, out cached))
            {
                return cached != null &&
                       !string.IsNullOrEmpty(cached.scenePath)
                    ? cached
                    : null;
            }

            List<string> sceneFiles = new List<string>();
            CollectSceneFiles(
                sourcePackage + ":/Saves/scene",
                sceneFiles,
                0
            );

            SceneMatch best = null;

            for (int s = 0; s < sceneFiles.Count; s++)
            {
                string scenePath = sceneFiles[s];
                JSONNode root = ReadScene(scenePath, sourcePackage);
                if (root == null)
                    continue;

                List<JSONNode> persons = GetPersons(root);

                for (int p = 0; p < persons.Count; p++)
                {
                    JSONNode person = persons[p];
                    int score = ScoreMatch(
                        lookLabel,
                        scenePath,
                        person,
                        persons.Count
                    );

                    if (best == null || score > best.score)
                    {
                        best = new SceneMatch();
                        best.scenePath = scenePath;
                        best.atomId = person["id"].Value;
                        best.score = score;
                    }
                }
            }

            // Do not infer model plugins from a generic one-Person scene. There
            // must be actual model-name evidence in the scene filename or Person id.
            if (best == null || best.score < 100)
            {
                SceneMatch miss = new SceneMatch();
                miss.scenePath = "";
                miss.atomId = "";
                miss.score = best == null ? 0 : best.score;
                _sceneMatchCache[cacheKey] = miss;
                return null;
            }

            _sceneMatchCache[cacheKey] = best;

            SuperController.LogMessage(
                "ModelPluginAdapter: matched " +
                lookLabel + " -> " +
                best.scenePath + " / " +
                best.atomId + " (score " +
                best.score + ")"
            );

            return best;
        }

        private bool IsDecalMakerPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return
                path.IndexOf(
                    "Chokaphi.DecalMaker",
                    StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(
                    "/VAM_Decal_Maker/",
                    StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsSceneUtilityPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;

            string lower = path.ToLowerInvariant();

            string[] excluded = new string[]
            {
                "acidbubbles.timeline",
                "/timeline",
                "embody",
                "vamstory",
                "vamx",
                "browserassist",
                "uiassist",
                "collidereditor",
                "headaudio",
                "head_audio",
                "scenecontroller",
                "scene_controller",
                "mocap",
                "motioncapture",
                "motion_capture",
                "possession",
                "cameracontrol",
                "camera_control",
                "animationcontroller",
                "animation_controller",

                // Scene/showcase behavior rather than model identity.
                // Arin's showcase scene uses AutoThruster to animate hip/chest/head
                // and VAMMoan for audio. Importing them into a reusable Look makes
                // the Character animate/moan on spawn, so they are not model plugins.
                "vinput.autothruster",
                "/autothruster/",
                "hazmhox.vammoan2teaser",
                "/vammoan2teaser/",

                // MovementHelper is a scene/showcase motion driver. Ashley's showcase
                // uses it to animate head/body controls; transferring it with a reusable
                // Look makes otherwise static models start moving on spawn.
                "dragontales.movement_helper_by_physis",
                "/movementhelper.cslist",
                "movementhelper.movementhelper"
            };

            for (int i = 0; i < excluded.Length; i++)
            {
                if (lower.Contains(excluded[i]))
                    return true;
            }

            return false;
        }

        private string ResolvePackagePath(
            string value,
            string sourcePackage)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            if (value.StartsWith(
                    "SELF:/",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(sourcePackage) ||
                    string.Equals(
                        sourcePackage,
                        "Local",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return value.Substring("SELF:/".Length);
                }

                return
                    sourcePackage + ":/" +
                    value.Substring("SELF:/".Length);
            }

            return value;
        }

        private JSONClass RewriteStatePaths(
            JSONClass state,
            string sourcePackage)
        {
            if (state == null)
                return null;

            try
            {
                string raw = state.ToString();

                if (!string.IsNullOrEmpty(sourcePackage) &&
                    !string.Equals(
                        sourcePackage,
                        "Local",
                        StringComparison.OrdinalIgnoreCase))
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

        private List<PluginSpec> ReadSpecs(
            string scenePath,
            string sceneAtomId,
            string sourcePackage)
        {
            List<PluginSpec> result = new List<PluginSpec>();

            JSONNode root = ReadScene(scenePath, sourcePackage);
            JSONNode person = FindPerson(root, sceneAtomId);

            if (person == null)
                return result;

            JSONArray storables = person["storables"].AsArray;
            if (storables == null)
                return result;

            JSONNode managerNode = null;

            for (int i = 0; i < storables.Count; i++)
            {
                JSONNode storable = storables[i];

                if (storable != null &&
                    string.Equals(
                        storable["id"].Value,
                        "PluginManager",
                        StringComparison.OrdinalIgnoreCase))
                {
                    managerNode = storable;
                    break;
                }
            }

            JSONClass plugins =
                managerNode == null
                ? null
                : managerNode["plugins"].AsObject;

            if (plugins == null)
                return result;

            foreach (string slot in plugins.Keys)
            {
                string pluginPath =
                    ResolvePackagePath(
                        plugins[slot].Value,
                        sourcePackage
                    );

                if (string.IsNullOrEmpty(pluginPath))
                    continue;

                if (IsDecalMakerPath(pluginPath))
                    continue;

                if (IsSceneUtilityPath(pluginPath))
                {
                    SuperController.LogMessage(
                        "ModelPluginAdapter: skipped scene utility " +
                        pluginPath
                    );
                    continue;
                }

                PluginSpec spec = new PluginSpec();
                spec.sourceSlot = slot;
                spec.pluginPath = pluginPath;
                spec.sourcePackage = sourcePackage;

                string prefix = slot + "_";

                for (int i = 0; i < storables.Count; i++)
                {
                    JSONNode state = storables[i];
                    if (state == null)
                        continue;

                    string id = state["id"].Value;

                    if (string.IsNullOrEmpty(id) ||
                        !id.StartsWith(
                            prefix,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    JSONNode clonedNode =
                        JSON.Parse(state.ToString());

                    JSONClass cloned =
                        clonedNode == null
                        ? null
                        : clonedNode.AsObject;

                    if (cloned != null)
                        spec.states.Add(cloned);
                }

                result.Add(spec);
            }

            return result;
        }

        private HashSet<string> GetSlots(MVRPluginManager manager)
        {
            HashSet<string> result =
                new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase
                );

            if (manager == null)
                return result;

            try
            {
                JSONClass state =
                    manager.GetJSON(true, true, true);

                JSONClass plugins =
                    state == null
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

        private string GetPluginPath(
            MVRPluginManager manager,
            string slot)
        {
            if (manager == null || string.IsNullOrEmpty(slot))
                return "";

            try
            {
                JSONClass state =
                    manager.GetJSON(true, true, true);

                JSONClass plugins =
                    state == null
                    ? null
                    : state["plugins"].AsObject;

                return plugins == null
                    ? ""
                    : plugins[slot].Value;
            }
            catch
            {
                return "";
            }
        }

        private string FindSlotByPath(
            MVRPluginManager manager,
            string expectedPath)
        {
            if (manager == null ||
                string.IsNullOrEmpty(expectedPath))
            {
                return "";
            }

            try
            {
                JSONClass state =
                    manager.GetJSON(true, true, true);

                JSONClass plugins =
                    state == null
                    ? null
                    : state["plugins"].AsObject;

                if (plugins == null)
                    return "";

                foreach (string key in plugins.Keys)
                {
                    if (string.Equals(
                            plugins[key].Value,
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

        private string FindNewSlot(
            MVRPluginManager manager,
            HashSet<string> before,
            string expectedPath)
        {
            if (manager == null)
                return "";

            try
            {
                JSONClass state =
                    manager.GetJSON(true, true, true);

                JSONClass plugins =
                    state == null
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

        private void Track(
            string personUid,
            string slot,
            string path)
        {
            if (string.IsNullOrEmpty(personUid) ||
                string.IsNullOrEmpty(slot) ||
                string.IsNullOrEmpty(path))
            {
                return;
            }

            List<ManagedPlugin> list;

            if (!_managedByPerson.TryGetValue(
                    personUid,
                    out list))
            {
                list = new List<ManagedPlugin>();
                _managedByPerson[personUid] = list;
            }

            ManagedPlugin item = new ManagedPlugin();
            item.slot = slot;
            item.path = path;
            list.Add(item);
        }

        private IEnumerator WaitForVaMIdle(float timeoutSeconds)
        {
            yield return new WaitForEndOfFrame();

            float deadline =
                Time.realtimeSinceStartup + timeoutSeconds;

            while (SuperController.singleton != null &&
                   SuperController.singleton.isLoading &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            yield return null;
            yield return new WaitForEndOfFrame();
        }

        public bool NeedsFreshSpawnAppearanceRefresh(
            string sourcePackage,
            string lookLabel)
        {
            string preset =
                FindMatchingPluginPreset(
                    sourcePackage,
                    lookLabel
                );

            if (string.IsNullOrEmpty(preset))
                return false;

            List<PluginSpec> specs =
                ReadPluginPresetSpecs(
                    preset,
                    sourcePackage
                );

            bool hasColliderEditor = false;
            bool hasSupplementaryColliders = false;

            for (int i = 0; i < specs.Count; i++)
            {
                PluginSpec spec = specs[i];
                string path =
                    spec == null || spec.pluginPath == null
                    ? ""
                    : spec.pluginPath.ToLowerInvariant();

                if (path.IndexOf("collidereditor") >= 0)
                    hasColliderEditor = true;

                if (path.IndexOf("supplementarycolliders") >= 0 ||
                    path.IndexOf("/uy_col/") >= 0 ||
                    path.EndsWith("/uycol.cslist"))
                {
                    hasSupplementaryColliders = true;
                }
            }

            // Require both sides of the author-saved collider setup.
            // This avoids doing a second Appearance load for unrelated plugin presets.
            return hasColliderEditor && hasSupplementaryColliders;
        }

        public IEnumerator ApplyForLook(
            Atom person,
            string sourcePackage,
            string lookLabel,
            string exactScenePath,
            string exactSceneAtomId,
            bool disableCameraHeadTracking = false,
            bool deferKaineFeetOnSpawn = false)
        {
            if (person == null ||
                string.IsNullOrEmpty(person.uid))
            {
                yield break;
            }

            // Kaine's author scene calls the Person "Person#3" and stores no
            // Person plugins. Restore its saved tongue setting explicitly.
            // Its showcase feet have asymmetric yaw/roll because one leg is
            // bent. Use regular 2B foot angles and height in the neutral
            // stance, keeping Kaine's wider left/right foot separation.
            if ((string.Equals(lookLabel, "2B-Kaine",
                    StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(lookLabel, "2B-SC6",
                    StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrEmpty(sourcePackage) &&
                sourcePackage.IndexOf("immyneedscake.Nier-2B-Compendium",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                yield return WaitForVaMIdle(20f);
                yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForFixedUpdate();
                bool sc6 = string.Equals(lookLabel, "2B-SC6",
                    StringComparison.OrdinalIgnoreCase);
                RestoreAuthored2BTongueCollision(
                    person, sourcePackage, sc6 ? "Person#2" : "Person#3");
                if (sc6) RestoreAuthored2BSC6Skirt(person, sourcePackage);
                if (deferKaineFeetOnSpawn)
                {
                    SuperController.LogMessage(
                        "CharacterRouter v272 " + lookLabel + " FEET | stage=DEFERRED_2B" +
                        " | person=" + person.uid);
                }
                else
                {
                    int feet = RestoreExact2BFeetToVariant(person, lookLabel);
                    yield return new WaitForFixedUpdate();
                    SuperController.LogMessage(
                        "CharacterRouter v272 " + lookLabel + " FEET | stage=APPLIED_2B" +
                        " | person=" + person.uid +
                        " | controls=" + feet);
                    if (sc6) LogSC6FootState(person);
                    else LogKaineFootState(person);
                }
                yield break;
            }

            List<PluginSpec> specs =
                new List<PluginSpec>();

            // v133 priority:
            // 1) explicit matching Person Plugin Preset in the same VAR;
            // 2) otherwise fall back to the confirmed scene-derived logic.
            string pluginPreset =
                FindMatchingPluginPreset(
                    sourcePackage,
                    lookLabel
                );

            if (!string.IsNullOrEmpty(pluginPreset))
            {
                specs =
                    ReadPluginPresetSpecs(
                        pluginPreset,
                        sourcePackage
                    );
            }
            else
            {
                string scenePath = exactScenePath;
                string atomId = exactSceneAtomId;

                if (string.IsNullOrEmpty(scenePath) ||
                    string.IsNullOrEmpty(atomId))
                {
                    SceneMatch match =
                        FindMatchingScene(
                            sourcePackage,
                            lookLabel
                        );

                    if (match == null)
                        yield break;

                    scenePath = match.scenePath;
                    atomId = match.atomId;
                }

                specs =
                    ReadSpecs(
                        scenePath,
                        atomId,
                        sourcePackage
                    );
            }

            if (specs.Count == 0)
                yield break;

            yield return WaitForVaMIdle(20f);

            MVRPluginManager manager =
                person.GetStorableByID("PluginManager")
                    as MVRPluginManager;

            if (manager == null)
            {
                SuperController.LogError(
                    "ModelPluginAdapter: Person has no PluginManager."
                );
                yield break;
            }

            int applied = 0;

            for (int i = 0; i < specs.Count; i++)
            {
                PluginSpec spec = specs[i];

                if (spec == null ||
                    string.IsNullOrEmpty(spec.pluginPath))
                {
                    continue;
                }

                try
                {
                    if (!FileManagerSecure.FileExists(
                            spec.pluginPath))
                    {
                        SuperController.LogError(
                            "ModelPluginAdapter: required model plugin is missing: " +
                            spec.pluginPath
                        );
                        continue;
                    }
                }
                catch { }

                // Preserve user-owned/pre-existing plugins rather than taking them over.
                string existing =
                    FindSlotByPath(
                        manager,
                        spec.pluginPath
                    );

                if (!string.IsNullOrEmpty(existing))
                {
                    SuperController.LogMessage(
                        "ModelPluginAdapter: existing plugin preserved: " +
                        spec.pluginPath
                    );
                    continue;
                }

                HashSet<string> beforeSlots =
                    GetSlots(manager);

                var createdPlugin = manager.CreatePlugin();

                if (createdPlugin == null)
                {
                    SuperController.LogError(
                        "ModelPluginAdapter: could not create slot for " +
                        spec.pluginPath
                    );
                    continue;
                }

                string targetSlot =
                    FindNewSlot(
                        manager,
                        beforeSlots,
                        ""
                    );

                try
                {
                    if (createdPlugin.pluginURLJSON != null)
                    {
                        createdPlugin.pluginURLJSON.val =
                            spec.pluginPath;
                    }
                }
                catch (Exception e)
                {
                    if (!string.IsNullOrEmpty(targetSlot))
                    {
                        try
                        {
                            manager.RemovePluginWithUID(
                                targetSlot
                            );
                        }
                        catch { }
                    }

                    SuperController.LogError(
                        "ModelPluginAdapter: URL assignment failed: " +
                        e.Message
                    );
                    continue;
                }

                float slotDeadline =
                    Time.realtimeSinceStartup + 5f;

                while (string.IsNullOrEmpty(targetSlot) &&
                       Time.realtimeSinceStartup < slotDeadline)
                {
                    targetSlot =
                        FindNewSlot(
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
                        "ModelPluginAdapter: could not identify created slot for " +
                        spec.pluginPath
                    );
                    continue;
                }

                bool partial = false;

                for (int s = 0; s < spec.states.Count; s++)
                {
                    JSONClass sourceState = spec.states[s];
                    if (sourceState == null)
                        continue;

                    string sourceId =
                        sourceState["id"].Value;

                    if (string.IsNullOrEmpty(sourceId) ||
                        !sourceId.StartsWith(
                            spec.sourceSlot,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string targetId =
                        targetSlot +
                        sourceId.Substring(
                            spec.sourceSlot.Length
                        );

                    JSONStorable target = null;

                    float targetDeadline =
                        Time.realtimeSinceStartup + 10f;

                    while (target == null &&
                           Time.realtimeSinceStartup < targetDeadline)
                    {
                        target =
                            person.GetStorableByID(targetId);

                        if (target == null)
                            yield return null;
                    }

                    if (target == null)
                    {
                        partial = true;
                        SuperController.LogError(
                            "ModelPluginAdapter: target storable missing: " +
                            targetId
                        );
                        continue;
                    }

                    JSONClass rewritten =
                        RewriteStatePaths(
                            sourceState,
                            spec.sourcePackage
                        );

                    if (rewritten == null)
                    {
                        partial = true;
                        continue;
                    }

                    rewritten["id"] = targetId;

                    // PoseMe keeps its per-Person Gaze toggle inside a private
                    // nested JSON object. Normalize the imported state before
                    // BodyLanguage performs its one-time late restoration.
                    bool restorePoseMeGaze =
                        disableCameraHeadTracking &&
                        spec.pluginPath.IndexOf(
                            "CheesyFX.BodyLanguage",
                            StringComparison.OrdinalIgnoreCase) >= 0 &&
                        targetId.EndsWith(
                            "_CheesyFX.PoseMe",
                            StringComparison.OrdinalIgnoreCase) &&
                        HeadTrackingAdapter.NormalizePoseMeRestoreState(
                            rewritten, person.uid);

                    bool heelRestoreFailed = false;
                    bool isPrestigitisHeel =
                        spec.pluginPath.IndexOf(
                            "prestigitis_HeelAdjust.cs",
                            StringComparison.OrdinalIgnoreCase) >= 0 &&
                        targetId.EndsWith(
                            "_prestigitis.HeelAdjust",
                            StringComparison.OrdinalIgnoreCase);

                    if (isPrestigitisHeel)
                    {
                        // On a fresh Person this plugin throws once and its
                        // delayed retry produces the right pose. On a reused
                        // slot it succeeds immediately but produces a different
                        // pose. Give both paths the same initialization window
                        // before restoring the author's saved plugin state.
                        yield return new WaitForEndOfFrame();
                        yield return new WaitForFixedUpdate();
                        yield return WaitForVaMIdle(10f);
                        SuperController.LogMessage(
                            "CharacterRouter v256 HEEL | stage=DEFERRED_RESTORE" +
                            " | person=" + person.uid +
                            " | id=" + targetId);
                    }

                    try
                    {
                        target.RestoreFromJSON(
                            rewritten,
                            true,
                            true,
                            null,
                            false
                        );

                        if (restorePoseMeGaze)
                        {
                            // PoseMe loads Gaze only in LateRestoreFromJSON,
                            // conditional on the "poses" key. Its normal
                            // RestoreFromJSON does not load the Gaze clients.
                            target.LateRestoreFromJSON(
                                rewritten, true, true, false);
                            JSONClass current = target.GetJSON(true, true, true);
                            JSONClass gaze = current == null ? null :
                                current["gaze"].AsObject;
                            string client = person.uid + " Enabled";
                            SuperController.LogMessage(
                                "CharacterRouter v237 HEAD TRACKING | stage=CHEESY_RESTORE" +
                                " | person=" + person.uid + " | id=" + targetId +
                                " | client=" +
                                (gaze == null ? "missing" : gaze[client].Value));
                        }
                    }
                    catch (Exception e)
                    {
                        partial = true;
                        heelRestoreFailed = isPrestigitisHeel;
                        if (heelRestoreFailed)
                            SuperController.LogMessage(
                                "CharacterRouter v251 HEEL | stage=FIRST_FAILED" +
                                " | person=" + person.uid +
                                " | id=" + targetId +
                                " | reason=" + e.Message);
                        else
                            SuperController.LogError(
                                "ModelPluginAdapter: restore failed for " +
                                targetId + ": " + e.Message);
                    }

                    if (heelRestoreFailed)
                    {
                        // The author plugin may receive saved state before
                        // its shoe/foot controls are ready. Retry after settling
                        // and fall back to its public sliders if it still fails.
                        yield return new WaitForEndOfFrame();
                        yield return new WaitForFixedUpdate();
                        yield return WaitForVaMIdle(10f);

                        bool recovered = false;
                        try
                        {
                            target.RestoreFromJSON(
                                rewritten, true, true, null, false);
                            recovered = true;
                        }
                        catch (Exception e)
                        {
                            SuperController.LogMessage(
                                "CharacterRouter v251 HEEL | stage=RETRY_FAILED" +
                                " | person=" + person.uid +
                                " | id=" + targetId +
                                " | reason=" + e.Message);
                        }

                        if (!recovered)
                        {
                            // RestoreFromJSON may fail in a private callback.
                            // The public sliders are still usable after the
                            // plugin has finished initializing.  Toggle only
                            // plugins saved enabled by the author.
                            bool authorEnabled =
                                !string.Equals(
                                    rewritten["enabled"].Value,
                                    "false",
                                    StringComparison.OrdinalIgnoreCase);
                            JSONStorableBool enabled =
                                target.GetBoolJSONParam("enabled");
                            if (enabled != null && authorEnabled)
                            {
                                try { enabled.val = false; }
                                catch (Exception e)
                                {
                                    SuperController.LogMessage(
                                        "CharacterRouter v251 HEEL | stage=TOGGLE_OFF_FAILED" +
                                        " | reason=" + e.Message);
                                }
                                yield return new WaitForEndOfFrame();
                                try { enabled.val = true; }
                                catch (Exception e)
                                {
                                    SuperController.LogMessage(
                                        "CharacterRouter v251 HEEL | stage=TOGGLE_ON_FAILED" +
                                        " | reason=" + e.Message);
                                }
                            }

                            string[] heelKeys = {
                                "Feet X", "Feet Y", "Feet Z",
                                "Toes X", "Toes Y", "Toes Z",
                                "Heel Backward Offset",
                                "Heel Collider Height", "Toe Collider Height"
                            };
                            int restoredFloats = 0;
                            for (int h = 0; h < heelKeys.Length; h++)
                            {
                                string raw = rewritten[heelKeys[h]].Value;
                                if (string.IsNullOrEmpty(raw))
                                    continue;

                                float value;
                                if (!float.TryParse(
                                    raw,
                                    System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    out value))
                                    continue;

                                JSONStorableFloat parameter =
                                    target.GetFloatJSONParam(heelKeys[h]);
                                if (parameter == null)
                                    continue;

                                try
                                {
                                    parameter.val = value;
                                    restoredFloats++;
                                }
                                catch (Exception e)
                                {
                                    SuperController.LogMessage(
                                        "CharacterRouter v251 HEEL | stage=PARAM_FAILED" +
                                        " | key=" + heelKeys[h] +
                                        " | reason=" + e.Message);
                                }
                            }

                            SuperController.LogMessage(
                                "CharacterRouter v251 HEEL | stage=FALLBACK" +
                                " | person=" + person.uid +
                                " | id=" + targetId +
                                " | params=" + restoredFloats +
                                " | enabled=" +
                                (enabled == null ? "unexposed" : enabled.val.ToString()));
                            if (restoredFloats == 0)
                                SuperController.LogError(
                                    "CharacterRouter v251 HEEL: no saved numeric" +
                                    " settings could be restored for " + targetId);
                        }
                        else
                        {
                            partial = false;
                            SuperController.LogMessage(
                                "CharacterRouter v251 HEEL | stage=RETRY_OK" +
                                " | person=" + person.uid +
                                " | id=" + targetId);
                        }
                    }

                    if (isPrestigitisHeel)
                    {
                        // VaM can reuse a Person/plugin slot after removal.
                        // This author scene omits "enabled" when it is true,
                        // and RestoreFromJSON(setMissingToDefault=false) must
                        // not inherit the previous instance's disabled state.
                        bool authorEnabled =
                            !string.Equals(rewritten["enabled"].Value,
                                "false", StringComparison.OrdinalIgnoreCase);
                        JSONStorableBool enabled =
                            target.GetBoolJSONParam("enabled");
                        if (enabled != null)
                        {
                            bool beforeEnabled = enabled.val;
                            try
                            {
                                if (beforeEnabled != authorEnabled)
                                    enabled.val = authorEnabled;
                                SuperController.LogMessage(
                                    "CharacterRouter v255 HEEL | stage=ENABLED_SYNC" +
                                    " | person=" + person.uid +
                                    " | id=" + targetId +
                                    " | before=" + beforeEnabled +
                                    " | expected=" + authorEnabled +
                                    " | after=" + enabled.val);
                            }
                            catch (Exception e)
                            {
                                SuperController.LogError(
                                    "CharacterRouter v255 HEEL | stage=ENABLED_ERROR" +
                                    " | person=" + person.uid +
                                    " | reason=" + e.Message);
                            }
                        }
                        else
                        {
                            SuperController.LogError(
                                "CharacterRouter v255 HEEL: enabled toggle missing for " +
                                targetId);
                        }
                    }

                    yield return null;
                }

                Track(
                    person.uid,
                    targetSlot,
                    spec.pluginPath
                );

                applied++;

                SuperController.LogMessage(
                    "ModelPluginAdapter: restored " +
                    spec.pluginPath +
                    (partial ? " (partial/default state)" : "")
                );

                yield return new WaitForEndOfFrame();
            }

            if (applied > 0)
            {
                SuperController.LogMessage(
                    "ModelPluginAdapter: applied " +
                    applied + " model plugin(s) for " +
                    (lookLabel ?? "")
                );
            }

            // The source scene has separate Persons for the three 2B Looks.
            // 2B and 2B Destroyed need their saved footwear controls; the
            // DLC footwear already looks right without changing its pose.
            string authored2BAtomId = null;
            bool restore2BFootControls = false;
            if (string.Equals(lookLabel, "2B", StringComparison.OrdinalIgnoreCase))
            {
                authored2BAtomId = "2B";
                restore2BFootControls = true;
            }
            else if (string.Equals(lookLabel, "2B_Destroyed",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lookLabel, "2B Destroyed",
                    StringComparison.OrdinalIgnoreCase))
            {
                authored2BAtomId = "2B Destroyed";
                restore2BFootControls = true;
            }
            else if (string.Equals(lookLabel, "2B_DLC",
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lookLabel, "2B DLC",
                    StringComparison.OrdinalIgnoreCase))
                authored2BAtomId = "2B DLC";

            if (authored2BAtomId != null &&
                !string.IsNullOrEmpty(sourcePackage) &&
                sourcePackage.IndexOf("immyneedscake.Nier-2B-Compendium",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                yield return new WaitForFixedUpdate();
                if (restore2BFootControls)
                {
                    int adjusted = RestoreAuthored2BFootControls(
                        person, sourcePackage, authored2BAtomId);
                    yield return new WaitForFixedUpdate();
                    yield return new WaitForFixedUpdate();
                    SuperController.LogMessage(
                        "CharacterRouter v253 2B FEET | stage=APPLIED" +
                        " | person=" + person.uid +
                        " | variant=" + authored2BAtomId +
                        " | controls=" + adjusted);
                    string[] controls = {
                        "rFootControl", "lFootControl", "rToeControl", "lToeControl"
                    };
                    for (int c = 0; c < controls.Length; c++)
                    {
                        JSONStorable control = person.GetStorableByID(controls[c]);
                        JSONClass state = control == null ? null :
                            control.GetJSON(true, true, true);
                        JSONNode rot = state == null ? null : state["localRotation"];
                        SuperController.LogMessage(
                            "CharacterRouter v253 2B FEET | control=" + controls[c] +
                            " | person=" + person.uid +
                            " | localRotation=" +
                            (rot == null ? "missing" :
                                rot["x"].Value + "," +
                                rot["y"].Value + "," + rot["z"].Value) +
                            " | jointDriveXTarget=" +
                            (state == null ? "missing" :
                                state["jointDriveXTarget"].Value));
                    }
                }

                // All three Appearance presets omit TongueControl, while the
                // matching source Persons save tongueCollision=false.
                RestoreAuthored2BTongueCollision(
                    person, sourcePackage, authored2BAtomId);
            }
        }

        private void RestoreAuthored2BTongueCollision(
            Atom person, string package, string authoredAtomId)
        {
            SceneMatch match = FindAuthored2BScene(package, authoredAtomId);
            if (match == null ||
                !string.Equals(match.atomId, authoredAtomId,
                    StringComparison.Ordinal))
                return;

            JSONNode root = ReadScene(match.scenePath, package);
            JSONNode source = FindPerson(root, match.atomId);
            JSONArray storables = source == null ? null :
                source["storables"].AsArray;
            if (storables == null)
                return;

            for (int i = 0; i < storables.Count; i++)
            {
                JSONNode saved = storables[i];
                if (!string.Equals(saved["id"].Value,
                    "TongueControl", StringComparison.Ordinal))
                    continue;

                string authored = saved["tongueCollision"].Value;
                if (!string.Equals(authored, "true",
                        StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(authored, "false",
                        StringComparison.OrdinalIgnoreCase))
                    return;

                JSONStorable live = person.GetStorableByID("TongueControl");
                if (live == null)
                {
                    SuperController.LogError(
                        "CharacterRouter v258 2B MOUTH | stage=TONGUE_UNAVAILABLE" +
                        " | person=" + person.uid);
                    return;
                }

                JSONClass beforeState = live.GetJSON(true, true, true);
                string before = beforeState == null ? "missing" :
                    beforeState["tongueCollision"].Value;
                JSONClass setting = new JSONClass();
                setting["id"] = "TongueControl";
                setting["tongueCollision"] = authored;
                try
                {
                    live.RestoreFromJSON(setting, true, true, null, false);
                    JSONClass afterState = live.GetJSON(true, true, true);
                    SuperController.LogMessage(
                        "CharacterRouter v258 2B MOUTH | stage=TONGUE_SYNC" +
                        " | person=" + person.uid +
                        " | variant=" + authoredAtomId +
                        " | before=" + before +
                        " | expected=" + authored +
                        " | after=" + (afterState == null ? "missing" :
                            afterState["tongueCollision"].Value));
                }
                catch (Exception e)
                {
                    SuperController.LogError(
                        "CharacterRouter v258 2B MOUTH | stage=TONGUE_FAILED" +
                        " | person=" + person.uid +
                        " | reason=" + e.Message);
                }
                return;
            }
        }

        private void RestoreAuthored2BSC6Skirt(Atom person, string package)
        {
            SceneMatch match = FindAuthored2BScene(package, "Person#2");
            JSONNode root = match == null ? null : ReadScene(match.scenePath, package);
            JSONNode source = FindPerson(root, "Person#2");
            JSONArray storables = source == null ? null : source["storables"].AsArray;
            if (storables == null) return;
            for (int i = 0; i < storables.Count; i++)
            {
                JSONNode saved = storables[i];
                if (saved["id"].Value != "immyneedscake:2BSkirtPreset") continue;
                JSONStorable live = person.GetStorableByID(saved["id"].Value);
                if (live == null)
                {
                    SuperController.LogError("CharacterRouter v272 2B-SC6 SKIRT | stage=STORABLE_MISSING");
                    return;
                }
                try
                {
                    JSONClass authored = JSON.Parse(saved.ToString()).AsObject;
                    JSONStorableString name =
                        live.GetStringJSONParam("presetName");
                    string beforeName = name == null ? "unexposed" : name.val;
                    live.RestoreFromJSON(authored, true, true, null, false);
                    if (name != null && name.val != saved["presetName"].Value)
                        name.val = saved["presetName"].Value;
                    JSONStorable material = person.GetStorableByID(
                        "immyneedscake:2BSkirtMaterial2B_Default_Body");
                    RestoreSC6SkirtMaterial(person, package, material);
                    JSONClass mat = material == null ? null :
                        material.GetJSON(true, true, true);
                    JSONNode color = mat == null ? null : mat["Diffuse Color"];
                    SuperController.LogMessage(
                        "CharacterRouter v273 2B-SC6 SKIRT | stage=PRESET_SYNC" +
                        " | person=" + person.uid +
                        " | expected=" + saved["presetName"].Value +
                        " | before=" + beforeName +
                        " | after=" + (name == null ? "unexposed" : name.val) +
                        " | material=" + (mat == null ? "missing" :
                            mat["customTexture_MainTex"].Value) +
                        " | diffuse=" + (color == null ? "missing" :
                            color["v"].Value));
                }
                catch (Exception e)
                {
                    SuperController.LogError(
                        "CharacterRouter v272 2B-SC6 SKIRT | stage=ERROR" +
                        " | detail=" + e.Message);
                }
                return;
            }
        }

        private void RestoreSC6SkirtMaterial(
            Atom person, string package, JSONStorable material)
        {
            if (material == null)
            {
                SuperController.LogError(
                    "CharacterRouter v274 2B-SC6 SKIRT | stage=MATERIAL_MISSING" +
                    " | person=" + person.uid);
                return;
            }

            // Measured from the live original Person#2 in the author's
            // showcase scene. The appearance import leaves Bodysuit textures
            // on the skirt even when its presetName reads SoulCaliber6.
            string root = package +
                ":/Custom/Clothing/Female/immyneedscake/2BBodySuit/Textures/";
            string main = root + "A006408Aalt.png";
            string spec = root + "2B_Cloth_s.png";
            string bump = root + "DB4E3F5B.png";
            string sim = root +
                "2BBodySuitMaterial2B_SkirtTemplate2.png";
            if (!FileManagerSecure.FileExists(main) ||
                !FileManagerSecure.FileExists(spec) ||
                !FileManagerSecure.FileExists(bump) ||
                !FileManagerSecure.FileExists(sim))
            {
                SuperController.LogError(
                    "CharacterRouter v274 2B-SC6 SKIRT | stage=TEXTURE_MISSING" +
                    " | package=" + package);
                return;
            }

            JSONClass state = new JSONClass();
            state["id"] = "immyneedscake:2BSkirtMaterial2B_Default_Body";
            state["Gloss"] = "6.471056";
            state["Gloss Texture Offset"] = "0.9921572";
            state["Global Illumination Filter"] = "0";
            state["Diffuse Bumpiness"] = "1";
            state["Specular Bumpiness"] = "1.007808";
            state["customTexture_MainTex"] = main;
            state["customTexture_SpecTex"] = spec;
            state["customTexture_GlossTex"] = "";
            state["customTexture_BumpMap"] = bump;
            state["simTexture"] = sim;
            JSONClass diffuse = new JSONClass();
            diffuse["h"] = "0";
            diffuse["s"] = "0";
            diffuse["v"] = "0.7547402";
            state["Diffuse Color"] = diffuse;
            JSONClass specular = new JSONClass();
            specular["h"] = "0";
            specular["s"] = "0";
            specular["v"] = "0.646802";
            state["Specular Color"] = specular;
            try
            {
                material.RestoreFromJSON(state, true, true, null, false);
                JSONClass actual = material.GetJSON(true, true, true);
                SuperController.LogMessage(
                    "CharacterRouter v274 2B-SC6 SKIRT | stage=MATERIAL_SYNC" +
                    " | person=" + person.uid +
                    " | main=" + (actual == null ? "missing" :
                        actual["customTexture_MainTex"].Value) +
                    " | sim=" + (actual == null ? "missing" :
                        actual["simTexture"].Value) +
                    " | diffuse=" + (actual == null ? "missing" :
                        actual["Diffuse Color"]["v"].Value));
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v274 2B-SC6 SKIRT | stage=MATERIAL_ERROR" +
                    " | reason=" + e.Message);
            }
        }

        private int RestoreAuthored2BFootControls(
            Atom person, string package, string authoredAtomId)
        {
            SceneMatch match = FindAuthored2BScene(package, authoredAtomId);
            if (match == null ||
                !string.Equals(match.atomId, authoredAtomId,
                    StringComparison.Ordinal))
                return 0;

            JSONNode root = ReadScene(match.scenePath, package);
            JSONNode source = FindPerson(root, match.atomId);
            JSONArray storables = source == null ? null :
                source["storables"].AsArray;
            if (storables == null)
                return 0;

            string[] ids = {
                "rFootControl", "lFootControl", "rToeControl", "lToeControl"
            };
            int changed = 0;
            for (int c = 0; c < ids.Length; c++)
            {
                JSONNode saved = null;
                for (int s = 0; s < storables.Count; s++)
                {
                    if (string.Equals(storables[s]["id"].Value,
                        ids[c], StringComparison.Ordinal))
                    {
                        saved = storables[s];
                        break;
                    }
                }

                JSONStorable live = person.GetStorableByID(ids[c]);
                if (saved == null || live == null ||
                    saved["localRotation"].AsObject == null ||
                    string.IsNullOrEmpty(saved["jointDriveXTarget"].Value))
                    continue;

                JSONClass state = new JSONClass();
                state["id"] = ids[c];
                state["localRotation"] =
                    JSON.Parse(saved["localRotation"].ToString());
                state["jointDriveXTarget"] =
                    saved["jointDriveXTarget"].Value;
                try
                {
                    live.RestoreFromJSON(state, true, true, null, false);
                    changed++;
                }
                catch (Exception e)
                {
                    SuperController.LogError(
                        "CharacterRouter v253 2B FEET: " + ids[c] +
                        " restore failed: " + e.Message);
                }
            }
            return changed;
        }

        private void LogKaineFootState(Atom person)
        {
            string[] ids = {
                "rFootControl", "lFootControl", "rToeControl", "lToeControl"
            };
            for (int i = 0; i < ids.Length; i++)
            {
                JSONStorable control = person.GetStorableByID(ids[i]);
                JSONClass state = control == null ? null :
                    control.GetJSON(true, true, true);
                JSONNode rot = state == null ? null : state["localRotation"];
                JSONNode pos = state == null ? null : state["localPosition"];
                SuperController.LogMessage(
                    "CharacterRouter v270 2B-KAINE FEET | control=" + ids[i] +
                    " | person=" + person.uid +
                    " | localPosition=" +
                    (pos == null ? "missing" : pos["x"].Value + "," +
                        pos["y"].Value + "," + pos["z"].Value) +
                    " | localRotation=" +
                    (rot == null ? "missing" : rot["x"].Value + "," +
                        rot["y"].Value + "," + rot["z"].Value) +
                    " | jointDriveXTarget=" +
                    (state == null ? "missing" :
                        state["jointDriveXTarget"].Value) +
                    " | rotationState=" +
                    (state == null ? "missing" :
                        state["rotationState"].Value));
            }
        }

        public void Finalize2BVariantFeetAfterSpawn(Atom person, string lookLabel)
        {
            if (person == null || string.IsNullOrEmpty(person.uid))
                return;
            int feet = RestoreExact2BFeetToVariant(person, lookLabel);
            SuperController.LogMessage(
                "CharacterRouter v272 " + lookLabel + " FEET | stage=POST_COLLISION_2B" +
                " | person=" + person.uid +
                " | controls=" + feet);
            if (string.Equals(lookLabel, "2B-SC6",
                    StringComparison.OrdinalIgnoreCase))
                LogSC6FootState(person);
            else
                LogKaineFootState(person);
        }

        private void LogSC6FootState(Atom person)
        {
            string[] ids = { "rFootControl", "lFootControl",
                "rToeControl", "lToeControl" };
            for (int i = 0; i < ids.Length; i++)
            {
                JSONStorable live = person.GetStorableByID(ids[i]);
                JSONClass state = live == null ? null :
                    live.GetJSON(true, true, true);
                SuperController.LogMessage(
                    "CharacterRouter v273 2B-SC6 FEET | control=" + ids[i] +
                    " | localRotation=" + (state == null ? "missing" :
                        state["localRotation"].ToString()) +
                    " | rotationState=" + (state == null ? "missing" :
                        state["rotationState"].Value) +
                    " | spring=" + (state == null ? "missing" :
                        state["holdRotationSpring"].Value) +
                    " | drive=" + (state == null ? "missing" :
                        state["jointDriveXTarget"].Value));
            }
        }

        private int RestoreExact2BFeetToVariant(Atom person, string lookLabel)
        {
            // Saved on the regular "2B" Person in 2b showcase.json.
            // Restore Kaine's observed neutral lateral separation. v269
            // copied all three position axes from 2B, pulling the feet
            // together. Copy 2B Y/Z and rotation/drive, using Kaine X.
            string[] ids = {
                "rFootControl", "lFootControl", "rToeControl", "lToeControl"
            };
            string[] x = { "47.5221", "47.54214", "39.34681", "39.99331" };
            string[] y = { "9.057739", "352.981", "10.16891", "351.7501" };
            string[] z = { "356.1858", "3.620111", "355.9042", "3.839373" };
            string[] drive = { "-45", "-45", "45", "45" };
            string[] px = { "0.1087584", "-0.1087584", "0.1461768", "-0.1461556" };
            string[] py = { "0.06044396", "0.06054413", "0.01809978", "0.01726079" };
            string[] pz = { "-0.007587556", "-0.008195523", "0.09483751", "0.09268191" };
            int changed = 0;
            for (int i = 0; i < ids.Length; i++)
            {
                JSONStorable live = person.GetStorableByID(ids[i]);
                if (live == null)
                {
                    SuperController.LogError(
                        "CharacterRouter v270 2B-KAINE FEET | stage=MISSING" +
                        " | control=" + ids[i]);
                    continue;
                }
                JSONClass before = live.GetJSON(true, true, true);
                JSONNode beforePosition = before == null ? null : before["localPosition"];
                if (beforePosition == null || beforePosition.AsObject == null)
                {
                    SuperController.LogError(
                        "CharacterRouter v270 2B-KAINE FEET | stage=NO_POSITION" +
                        " | control=" + ids[i]);
                    continue;
                }
                SuperController.LogMessage(
                    "CharacterRouter v270 2B-KAINE FEET | stage=BEFORE" +
                    " | control=" + ids[i] +
                    " | localPosition=" + beforePosition["x"].Value + "," +
                    beforePosition["y"].Value + "," +
                    beforePosition["z"].Value);
                JSONClass rotation = new JSONClass();
                rotation["x"] = x[i];
                rotation["y"] = y[i];
                rotation["z"] = z[i];
                JSONClass position = new JSONClass();
                position["x"] = px[i];
                position["y"] = py[i];
                position["z"] = pz[i];
                JSONClass state = new JSONClass();
                state["id"] = ids[i];
                state["localPosition"] = position;
                state["localRotation"] = rotation;
                state["jointDriveXTarget"] = drive[i];
                if (string.Equals(lookLabel, "2B-SC6",
                        StringComparison.OrdinalIgnoreCase) && i >= 2)
                {
                    // Author scene: both toe controls hold rotation, with
                    // different spring values on right and left. Kaine's
                    // default is Off and must not be copied to these boots.
                    state["rotationState"] = "On";
                    state["holdRotationSpring"] = i == 2 ? "2000" : "200";
                }
                try
                {
                    live.RestoreFromJSON(state, true, true, null, false);
                    changed++;
                }
                catch (Exception e)
                {
                    SuperController.LogError(
                        "CharacterRouter v270 2B-KAINE FEET | stage=ERROR" +
                        " | control=" + ids[i] + " | reason=" + e.Message);
                }
            }
            return changed;
        }

        private SceneMatch FindAuthored2BScene(
            string package, string authoredAtomId)
        {
            if (!string.Equals(authoredAtomId, "Person#3",
                    StringComparison.Ordinal) &&
                !string.Equals(authoredAtomId, "Person#2",
                    StringComparison.Ordinal))
                return FindMatchingScene(package, authoredAtomId);

            string path = package +
                ":/Saves/scene/immyneedscake/2b showcase single.json";
            try
            {
                if (!FileManagerSecure.FileExists(path))
                {
                    SuperController.LogError(
                        "CharacterRouter v261 2B-KAINE | stage=SCENE_MISSING" +
                        " | path=" + path);
                    return null;
                }
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v261 2B-KAINE | stage=SCENE_ERROR" +
                    " | detail=" + e.Message);
                return null;
            }
            SceneMatch match = new SceneMatch();
            match.scenePath = path;
            match.atomId = authoredAtomId;
            return match;
        }

        // prestigitis HeelAdjust 1.0.1/1.0.2 does not reliably clean up its
        // foot pose and colliders in OnDestroy. The author requires disabling
        // the plugin before removal. Touch only adapter-owned plugin slots.
        public int DisableManagedPrestigitisHeel(Atom person)
        {
            if (person == null || string.IsNullOrEmpty(person.uid))
                return 0;

            List<ManagedPlugin> list;
            if (!_managedByPerson.TryGetValue(person.uid, out list) ||
                list == null)
                return 0;

            MVRPluginManager manager =
                person.GetStorableByID("PluginManager") as MVRPluginManager;
            if (manager == null)
                return 0;

            int disabled = 0;
            for (int i = 0; i < list.Count; i++)
            {
                ManagedPlugin item = list[i];
                if (item == null || string.IsNullOrEmpty(item.slot) ||
                    string.IsNullOrEmpty(item.path) ||
                    item.path.IndexOf("prestigitis_HeelAdjust.cs",
                        StringComparison.OrdinalIgnoreCase) < 0 ||
                    !string.Equals(GetPluginPath(manager, item.slot),
                        item.path, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    JSONStorable script = person.GetStorableByID(
                        item.slot + "_prestigitis.HeelAdjust");
                    JSONStorableBool enabled = script == null ? null :
                        script.GetBoolJSONParam("enabled");
                    if (enabled != null && enabled.val)
                    {
                        enabled.val = false;
                        disabled++;
                    }
                    else if (enabled == null)
                    {
                        SuperController.LogError(
                            "CharacterRouter v254 REMOVE | stage=PRESTIGITIS_UNEXPOSED" +
                            " | person=" + person.uid + " | slot=" + item.slot);
                    }
                }
                catch (Exception e)
                {
                    SuperController.LogError(
                        "CharacterRouter v254 REMOVE | stage=PRESTIGITIS_DISABLE_ERROR" +
                        " | person=" + person.uid + " | slot=" + item.slot +
                        " | detail=" + e.Message);
                }
            }
            return disabled;
        }

        public void ClearManaged(Atom person)
        {
            if (person == null ||
                string.IsNullOrEmpty(person.uid))
            {
                return;
            }

            List<ManagedPlugin> list;

            if (!_managedByPerson.TryGetValue(
                    person.uid,
                    out list) ||
                list == null)
            {
                return;
            }

            DisableManagedPrestigitisHeel(person);

            try
            {
                MVRPluginManager manager =
                    person.GetStorableByID("PluginManager")
                        as MVRPluginManager;

                if (manager != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        ManagedPlugin item = list[i];

                        if (item == null ||
                            string.IsNullOrEmpty(item.slot))
                        {
                            continue;
                        }

                        string livePath =
                            GetPluginPath(
                                manager,
                                item.slot
                            );

                        if (!string.IsNullOrEmpty(livePath) &&
                            string.Equals(
                                livePath,
                                item.path,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                manager.RemovePluginWithUID(
                                    item.slot
                                );
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "ModelPluginAdapter: cleanup failed: " +
                    e.Message
                );
            }

            _managedByPerson.Remove(person.uid);
        }

        public void ForgetPerson(string personUid)
        {
            if (!string.IsNullOrEmpty(personUid))
                _managedByPerson.Remove(personUid);
        }
    }
}
