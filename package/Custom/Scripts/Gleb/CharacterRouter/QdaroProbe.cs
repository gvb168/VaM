using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace GlebVAM
{
    // v128: observer-only diagnostics for qdaro piercing clothing.
    // This probe NEVER mutates clothing, managers, or CUA atoms. It only records
    // the actual runtime manager path and the four expected qdaro CUA states.
    internal class QdaroProbe
    {
        public const string ReduxPath =
            "Best-VR-Babes.ClothingPluginManager_patched.2:/Custom/Scripts/Best-VR-Babes/ClothingPluginManager_patched.cs";

        private readonly Dictionary<string, BaselineTarget> _baseline =
            new Dictionary<string, BaselineTarget>(StringComparer.OrdinalIgnoreCase);
        private bool _baselineCaptured = false;

        private class BaselineTarget
        {
            public string assetName;
            public string assetUrl;
            public int choices;
        }

        private class RuntimeTarget
        {
            public string key;
            public string uid;
            public string assetName;
            public string assetUrl;
            public int choices;
        }

        public bool IsReduxAvailable()
        {
            try
            {
                return FileManagerSecure.FileExists(ReduxPath);
            }
            catch
            {
                return false;
            }
        }

        public bool IsQdaroPiercingId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            return id.StartsWith("qdaro:Piercing", StringComparison.OrdinalIgnoreCase);
        }

        public IEnumerator Observe(
            Atom person,
            string context,
            Action<string> diagnostic,
            float durationSeconds)
        {
            if (person == null || diagnostic == null)
                yield break;

            float start = Time.realtimeSinceStartup;
            float[] checkpoints = new float[] { 0.00f, 0.10f, 0.50f, 1.00f, 2.00f, 4.00f, 6.00f };

            Log(diagnostic,
                "QDARO OBSERVE BEGIN | context=" + (context ?? "") +
                " | person=" + person.uid +
                " | reduxAvailable=" + IsReduxAvailable());

            for (int i = 0; i < checkpoints.Length; i++)
            {
                if (checkpoints[i] > durationSeconds)
                    break;

                float deadline = start + checkpoints[i];
                while (Time.realtimeSinceStartup < deadline)
                    yield return null;

                Snapshot(person, context, checkpoints[i], diagnostic);
            }

            Log(diagnostic,
                "QDARO OBSERVE END | context=" + (context ?? "") +
                " | person=" + person.uid);
        }

        private void Snapshot(
            Atom person,
            string context,
            float dt,
            Action<string> diagnostic)
        {
            if (person == null)
                return;

            List<string> managerIds = new List<string>();
            try
            {
                List<string> ids = person.GetStorableIDs();
                if (ids != null)
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        string id = ids[i];
                        if (!IsQdaroPiercingId(id))
                            continue;

                        if (id.IndexOf("ClothingPluginManager", StringComparison.OrdinalIgnoreCase) >= 0)
                            managerIds.Add(id);
                    }
                }
            }
            catch { }

            List<RuntimeTarget> targets = GetRuntimeTargets(person);
            int ready = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                RuntimeTarget target = targets[i];
                if (target != null &&
                    target.choices > 1 &&
                    !string.IsNullOrEmpty(target.assetName) &&
                    !string.Equals(target.assetName, "None", StringComparison.OrdinalIgnoreCase))
                {
                    ready++;
                }
            }

            Log(diagnostic,
                "QDARO SNAPSHOT | context=" + (context ?? "") +
                " | dt=" + dt.ToString("F2") +
                " | person=" + person.uid +
                " | managers=" + managerIds.Count +
                " | cuaTargets=" + targets.Count +
                " | ready=" + ready + "/" + targets.Count +
                " | baselineCaptured=" + _baselineCaptured);

            for (int i = 0; i < managerIds.Count; i++)
            {
                string id = managerIds[i];
                JSONStorable storable = null;
                try { storable = person.GetStorableByID(id); } catch { }

                string typeName = storable == null ? "<missing>" : storable.GetType().FullName;
                string plugin0 = GetPluginZeroFromRuntime(storable);

                Log(diagnostic,
                    "QDARO MANAGER | id=" + id +
                    " | type=" + typeName +
                    " | plugin#0=" + (string.IsNullOrEmpty(plugin0) ? "<unknown>" : plugin0));
            }

            for (int i = 0; i < targets.Count; i++)
            {
                RuntimeTarget t = targets[i];
                Log(diagnostic,
                    "QDARO CUA | key=" + t.key +
                    " | uid=" + t.uid +
                    " | assetUrl=" + (string.IsNullOrEmpty(t.assetUrl) ? "<empty>" : t.assetUrl) +
                    " | assetName=" + (string.IsNullOrEmpty(t.assetName) ? "<empty>" : t.assetName) +
                    " | choices=" + t.choices);
            }

            if (!_baselineCaptured && targets.Count >= 4 && ready == targets.Count)
            {
                _baseline.Clear();
                for (int i = 0; i < targets.Count; i++)
                {
                    RuntimeTarget t = targets[i];
                    BaselineTarget b = new BaselineTarget();
                    b.assetName = t.assetName;
                    b.assetUrl = t.assetUrl;
                    b.choices = t.choices;
                    _baseline[t.key] = b;
                }
                _baselineCaptured = true;

                Log(diagnostic,
                    "QDARO BASELINE CAPTURED | context=" + (context ?? "") +
                    " | targets=" + _baseline.Count);
            }
            else if (_baselineCaptured)
            {
                int missing = 0;
                int degraded = 0;
                Dictionary<string, RuntimeTarget> current =
                    new Dictionary<string, RuntimeTarget>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < targets.Count; i++)
                    current[targets[i].key] = targets[i];

                foreach (KeyValuePair<string, BaselineTarget> pair in _baseline)
                {
                    RuntimeTarget now;
                    if (!current.TryGetValue(pair.Key, out now))
                    {
                        missing++;
                        Log(diagnostic,
                            "QDARO DIFF | key=" + pair.Key +
                            " | status=MISSING_CUA" +
                            " | baselineAsset=" + pair.Value.assetName +
                            " | baselineChoices=" + pair.Value.choices);
                        continue;
                    }

                    if (!string.Equals(now.assetName, pair.Value.assetName, StringComparison.OrdinalIgnoreCase) ||
                        now.choices <= 1)
                    {
                        degraded++;
                        Log(diagnostic,
                            "QDARO DIFF | key=" + pair.Key +
                            " | status=DEGRADED" +
                            " | baselineAsset=" + pair.Value.assetName +
                            " | currentAsset=" + now.assetName +
                            " | baselineChoices=" + pair.Value.choices +
                            " | currentChoices=" + now.choices +
                            " | baselineUrl=" + pair.Value.assetUrl +
                            " | currentUrl=" + now.assetUrl);
                    }
                }

                Log(diagnostic,
                    "QDARO DIFF SUMMARY | context=" + (context ?? "") +
                    " | missing=" + missing +
                    " | degraded=" + degraded +
                    " | baselineTargets=" + _baseline.Count);
            }
        }

        private string GetPluginZeroFromRuntime(JSONStorable storable)
        {
            if (storable == null)
                return "";

            try
            {
                JSONClass state = storable.GetJSON(true, true, true);
                if (state == null)
                    return "";

                JSONClass plugins = state["plugins"].AsObject;
                if (plugins == null)
                    return "";

                return plugins["plugin#0"].Value;
            }
            catch
            {
                return "";
            }
        }

        private List<RuntimeTarget> GetRuntimeTargets(Atom person)
        {
            List<RuntimeTarget> result = new List<RuntimeTarget>();
            if (person == null || SuperController.singleton == null)
                return result;

            List<Atom> atoms = null;
            try { atoms = SuperController.singleton.GetAtoms(); } catch { }
            if (atoms == null)
                return result;

            string prefix = person.uid + "#qdaro:Piercing";
            for (int i = 0; i < atoms.Count; i++)
            {
                Atom atom = atoms[i];
                if (atom == null || atom.type != "CustomUnityAsset" || string.IsNullOrEmpty(atom.uid))
                    continue;

                if (!atom.uid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                RuntimeTarget t = new RuntimeTarget();
                t.uid = atom.uid;
                t.key = atom.uid.Substring(person.uid.Length + 1);
                t.assetName = "<missing chooser>";
                t.assetUrl = "";
                t.choices = 0;

                try
                {
                    JSONStorable asset = atom.GetStorableByID("asset");
                    if (asset != null)
                    {
                        JSONStorableStringChooser chooser =
                            asset.GetStringChooserJSONParam("assetName");
                        if (chooser != null)
                        {
                            t.assetName = chooser.val;
                            if (chooser.choices != null)
                                t.choices = chooser.choices.Count;
                        }

                        JSONClass state = asset.GetJSON(true, true, true);
                        if (state != null)
                            t.assetUrl = state["assetUrl"].Value;
                    }
                }
                catch { }

                result.Add(t);
            }

            result.Sort(delegate(RuntimeTarget a, RuntimeTarget b) {
                return string.Compare(a == null ? "" : a.key, b == null ? "" : b.key, StringComparison.OrdinalIgnoreCase);
            });

            return result;
        }

        private void Log(Action<string> diagnostic, string text)
        {
            try { diagnostic(text); } catch { }
        }
    }
}
