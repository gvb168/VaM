using System;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace GlebVAM
{
    // AltFutaAdapter v140
    // AltFuta is native Female in VaM. This adapter only supplies the logical
    // FUTA catalog/editor tag; it never changes selector.gender or native banks.
    public class AltFutaAdapter
    {
        private bool ContainsMarker(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            string lower = value.ToLowerInvariant();
            return lower.Contains("altfuta") ||
                   lower.Contains("alt_futa") ||
                   lower.Contains("alt-futa") ||
                   lower.Contains("alt futa");
        }

        private JSONNode FindGeometry(JSONNode root)
        {
            if (root == null)
                return null;

            JSONArray storables = root["storables"].AsArray;
            if (storables == null)
                return null;

            for (int i = 0; i < storables.Count; i++)
            {
                JSONNode st = storables[i];
                if (st != null && string.Equals(
                        st["id"].Value,
                        "geometry",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return st;
                }
            }

            return null;
        }

        private bool HasStructuralEvidence(JSONNode geometry)
        {
            if (geometry == null)
                return false;

            bool maleMorphsOnFemale = string.Equals(
                geometry["useMaleMorphsOnFemale"].Value,
                "true",
                StringComparison.OrdinalIgnoreCase);

            if (!maleMorphsOnFemale)
                return false;

            JSONArray morphs = geometry["morphs"].AsArray;
            if (morphs == null)
                return false;

            for (int i = 0; i < morphs.Count; i++)
            {
                JSONNode morph = morphs[i];
                if (morph == null)
                    continue;

                string uid = (morph["uid"].Value ?? "").Replace('\\', '/');
                string name = morph["name"].Value ?? "";
                string probe = (uid + " " + name).ToLowerInvariant();

                if (probe.Contains("futa") &&
                    (probe.Contains("/morphs/female_genitalia/") ||
                     probe.Contains("/morphs/female/")))
                {
                    return true;
                }
            }

            return false;
        }

        private JSONNode ReadPresetRoot(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            try
            {
                string content = FileManagerSecure.ReadAllText(path);
                if (!string.IsNullOrEmpty(content))
                    return JSON.Parse(content);
            }
            catch { }

            return null;
        }

        public bool IsAltFutaAppearance(
            string path,
            string source,
            string label)
        {
            string probe = (label ?? "") + " " +
                           (source ?? "") + " " +
                           (path ?? "");

            if (ContainsMarker(probe))
                return true;

            return HasStructuralEvidence(FindGeometry(ReadPresetRoot(path)));
        }

        public bool IsAltFutaScenePerson(
            JSONNode personNode,
            string scenePath,
            string source)
        {
            if (personNode == null)
                return false;

            string probe = personNode["id"].Value + " " +
                           (scenePath ?? "") + " " +
                           (source ?? "");

            if (ContainsMarker(probe))
                return true;

            return HasStructuralEvidence(FindGeometry(personNode));
        }
    }
}
