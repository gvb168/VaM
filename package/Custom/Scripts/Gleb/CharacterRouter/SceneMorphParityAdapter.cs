using System;
using System.Collections;
using System.Collections.Generic;
using MVR.FileManagementSecure;
using SimpleJSON;
using UnityEngine;

namespace GlebVAM
{
    // Some author Appearance presets omit the model morphs carried by the
    // corresponding scene Person. Restore only the missing morph state after
    // the normal Appearance load. Clothing, Hair, materials and textures remain
    // those of the selected Appearance variant.
    public class SceneMorphParityAdapter
    {
        // The same VaM UID may be reused by a new Person after removal.
        private readonly HashSet<string> _seenSakuriUids =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private const string SakuriPackage = "Eboy.Sakuri.1";
        private const string SakuriScene =
            "Eboy.Sakuri.1:/Saves/scene/Sakuri.json";
        private const string SakuriPresetPrefix =
            "Eboy.Sakuri.1:/Custom/Atom/Person/Appearance/Preset_Sakuri - ";

        private static bool IsSakuriLipBiteMorph(string name)
        {
            return string.Equals(name, "AA Eyes Lip Bite 1",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "AA Mouth Lip Bite 1",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static JSONNode FindStorable(JSONNode atom, string id)
        {
            JSONArray storables = atom == null ? null : atom["storables"].AsArray;
            for (int i = 0; storables != null && i < storables.Count; i++)
            {
                JSONNode storable = storables[i];
                if (storable != null && storable["id"].Value == id)
                    return storable;
            }
            return null;
        }

        private static bool IsSakuriPreset(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                path.StartsWith(SakuriPresetPrefix,
                    StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".vap", StringComparison.OrdinalIgnoreCase);
        }

        private static string QualifySakuriPath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                path.StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase)
                ? SakuriPackage + ":/" + path.Substring(6)
                : path;
        }

        private static bool SameMorphValue(JSONNode a, JSONNode b)
        {
            float av;
            float bv;
            return a != null && b != null &&
                float.TryParse(a.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out av) &&
                float.TryParse(b.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out bv) &&
                Math.Abs(av - bv) < 0.001f;
        }

        public IEnumerator ApplyForLook(Atom person, string sourcePackage,
            string lookPath)
        {
            if (person == null ||
                !string.Equals(sourcePackage, SakuriPackage,
                    StringComparison.OrdinalIgnoreCase) ||
                !IsSakuriPreset(lookPath))
                yield break;

            JSONArray sourceMorphs = null;
            string expectedGenitalDiffuse = "";
            string expectedGenitalDecal = "";
            bool presetHasMorphs = false;
            try
            {
                JSONNode preset = JSON.Parse(
                    FileManagerSecure.ReadAllText(lookPath));
                JSONNode presetGeometry = FindStorable(preset, "geometry");
                JSONNode presetTextures = FindStorable(preset, "textures");
                expectedGenitalDiffuse = presetTextures == null ? "" :
                    QualifySakuriPath(
                        presetTextures["genitalsDiffuseUrl"].Value);
                expectedGenitalDecal = presetTextures == null ? "" :
                    QualifySakuriPath(
                        presetTextures["genitalsDecalUrl"].Value);
                JSONArray presetMorphs = presetGeometry == null ? null :
                    presetGeometry["morphs"].AsArray;
                if (presetMorphs == null)
                    throw new Exception("Preset geometry has no morph list");
                presetHasMorphs = presetMorphs.Count > 0;
                if (!presetHasMorphs)
                {
                    JSONNode scene = JSON.Parse(
                        FileManagerSecure.ReadAllText(SakuriScene));
                    JSONArray atoms = scene == null ? null : scene["atoms"].AsArray;
                    JSONNode sourcePerson = null;
                    for (int i = 0; atoms != null && i < atoms.Count; i++)
                    {
                        if (atoms[i] != null &&
                            atoms[i]["type"].Value == "Person" &&
                            atoms[i]["id"].Value == "Sakuri")
                        {
                            sourcePerson = atoms[i];
                            break;
                        }
                    }
                    JSONNode sourceGeometry =
                        FindStorable(sourcePerson, "geometry");
                    sourceMorphs = sourceGeometry == null ? null :
                        sourceGeometry["morphs"].AsArray;
                    if (sourceMorphs == null || sourceMorphs.Count == 0)
                        throw new Exception("Source scene has no Sakuri morphs");
                }
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v249 SAKURI | stage=SOURCE_ERROR" +
                    " | detail=" + e);
                yield break;
            }
            if (presetHasMorphs)
            {
                SuperController.LogMessage(
                    "CharacterRouter v249 SAKURI | stage=SKIP_PRESET_HAS_MORPHS" +
                    " | path=" + lookPath);
                yield break;
            }

            // Let LoadPreset and its geometry rebuild complete before applying
            // the source shape. A partial geometry restore avoids touching the
            // selected variant's hair, clothing and skin state.
            yield return new WaitForFixedUpdate();
            if (person == null)
                yield break;

            bool reusedUid = !_seenSakuriUids.Add(person.uid);

            JSONArray qualifiedMorphs = new JSONArray();
            int neutralized = 0;
            for (int i = 0; i < sourceMorphs.Count; i++)
            {
                JSONNode source = sourceMorphs[i];
                JSONClass morph = source == null ? null :
                    JSON.Parse(source.ToString()).AsObject;
                if (morph == null) continue;
                morph["uid"] = QualifySakuriPath(morph["uid"].Value);
                // The scene stores a lip-bite expression. Keep Sakuri's head
                // shape and eyelid settings, but start with a neutral face.
                if (IsSakuriLipBiteMorph(morph["name"].Value))
                {
                    morph["value"] = "0";
                    neutralized++;
                }
                qualifiedMorphs.Add(morph);
            }

            try
            {
                JSONStorable geometry = person.GetStorableByID("geometry");
                if (geometry == null)
                    throw new Exception("Person geometry is unavailable");
                JSONClass patch = new JSONClass();
                patch["id"] = "geometry";
                patch["morphs"] = qualifiedMorphs;
                geometry.RestoreFromJSON(patch, true, true, null, false);
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v249 SAKURI | stage=MORPH_RESTORE_ERROR" +
                    " | person=" + person.uid + " | detail=" + e);
                yield break;
            }

            yield return null;
            if (person == null) yield break;

            // VaM may reuse a removed Person's UID while its previous genital
            // skin material remains cached. The URL string can still read as
            // correct. Re-trigger the author-saved diffuse texture only on a
            // reused UID; the known-good first spawn keeps its normal load.
            if (reusedUid && !string.IsNullOrEmpty(expectedGenitalDiffuse))
            {
                try
                {
                    JSONStorable textures = person.GetStorableByID("textures");
                    JSONStorableUrl genitalDiffuse = textures == null ? null :
                        textures.GetUrlJSONParam("genitalsDiffuseUrl");
                    if (genitalDiffuse == null)
                        throw new Exception("Genital diffuse URL is unavailable");
                    string before = genitalDiffuse.val;
                    genitalDiffuse.val = "";
                    genitalDiffuse.val = expectedGenitalDiffuse;
                    SuperController.LogMessage(
                        "CharacterRouter v249 SAKURI | stage=GENITAL_DIFFUSE_REFRESH" +
                        " | person=" + person.uid +
                        " | before=" + before +
                        " | expected=" + expectedGenitalDiffuse +
                        " | actual=" + genitalDiffuse.val);
                }
                catch (Exception e)
                {
                    SuperController.LogError(
                        "CharacterRouter v249 SAKURI | stage=GENITAL_DIFFUSE_ERROR" +
                        " | person=" + person.uid + " | detail=" + e);
                }
                yield return new WaitForFixedUpdate();
                if (person == null) yield break;
            }

            // The Alternative 1 preset has a separate genital tattoo decal.
            // A geometry rebuild after LoadPreset can leave that region blank,
            // even when other skin decals remain visible. Replay only the
            // author-saved genital decal, after the geometry has settled.
            if (!string.IsNullOrEmpty(expectedGenitalDecal))
            {
                yield return new WaitForFixedUpdate();
                if (person == null) yield break;
                try
                {
                    JSONStorable textures = person.GetStorableByID("textures");
                    JSONStorableUrl genitalDecal = textures == null ? null :
                        textures.GetUrlJSONParam("genitalsDecalUrl");
                    if (genitalDecal == null)
                        throw new Exception("Genital decal URL is unavailable");
                    string before = genitalDecal.val;
                    // Force its callback even if VaM already reports the URL:
                    // the material may have been rebuilt after the first load.
                    genitalDecal.val = "";
                    genitalDecal.val = expectedGenitalDecal;
                    SuperController.LogMessage(
                        "CharacterRouter v249 SAKURI | stage=GENITAL_DECAL" +
                        " | person=" + person.uid +
                        " | before=" + before +
                        " | expected=" + expectedGenitalDecal +
                        " | actual=" + genitalDecal.val);
                }
                catch (Exception e)
                {
                    SuperController.LogError(
                        "CharacterRouter v249 SAKURI | stage=GENITAL_DECAL_ERROR" +
                        " | person=" + person.uid + " | detail=" + e);
                }
                yield return null;
                if (person == null) yield break;
            }
            try
            {
                JSONStorable geometry = person.GetStorableByID("geometry");
                JSONClass live = geometry == null ? null :
                    geometry.GetJSON(true, true, true);
                JSONArray liveMorphs = live == null ? null : live["morphs"].AsArray;
                List<string> missing = new List<string>();
                for (int i = 0; i < qualifiedMorphs.Count; i++)
                {
                    JSONNode requested = qualifiedMorphs[i];
                    bool found = false;
                    bool nameFound = false;
                    for (int j = 0; liveMorphs != null && j < liveMorphs.Count; j++)
                    {
                        JSONNode actual = liveMorphs[j];
                        if (actual != null &&
                            actual["name"].Value == requested["name"].Value)
                        {
                            nameFound = true;
                            found = SameMorphValue(actual["value"],
                                requested["value"]);
                            break;
                        }
                    }
                    // VaM may omit zero-valued morphs when saving geometry.
                    bool zeroOmitted = liveMorphs != null && !nameFound &&
                        IsSakuriLipBiteMorph(requested["name"].Value);
                    if (!found && !zeroOmitted)
                        missing.Add(requested["name"].Value);
                }

                JSONStorable textures = person.GetStorableByID("textures");
                JSONStorableUrl genital = textures == null ? null :
                    textures.GetUrlJSONParam("genitalsDiffuseUrl");
                JSONStorableUrl decal = textures == null ? null :
                    textures.GetUrlJSONParam("genitalsDecalUrl");
                SuperController.LogMessage(
                    "CharacterRouter v249 SAKURI | stage=MORPH_PARITY" +
                    " | person=" + person.uid +
                    " | reusedUid=" + reusedUid +
                    " | requested=" + qualifiedMorphs.Count +
                    " | neutralized=" + neutralized +
                    " | missing=" + (missing.Count == 0 ? "none" :
                        string.Join(",", missing.ToArray())) +
                    " | genitalsDiffuse=" +
                    (genital == null ? "unavailable" : genital.val) +
                    " | genitalsDecal=" +
                    (decal == null ? "unavailable" : decal.val));
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v249 SAKURI | stage=VERIFY_ERROR" +
                    " | person=" + person.uid + " | detail=" + e);
            }
        }
    }
}
