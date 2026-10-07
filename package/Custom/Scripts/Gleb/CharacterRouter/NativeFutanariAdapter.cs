using UnityEngine;
using System;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace GlebVAM
{
    // NativeFutanariAdapter v141
    // Recognition/ownership only. It never mutates ordinary Male/Female Persons;
    // CharacterRouter v141 accepts native X lifecycle only through this adapter.
    public class NativeFutanariAdapter
    {
        private bool IsAltFutaMarker(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            string lower = value.ToLowerInvariant();
            return lower.Contains("altfuta") ||
                   lower.Contains("alt_futa") ||
                   lower.Contains("alt-futa") ||
                   lower.Contains("alt futa");
        }

        private bool ContainsNativeFutaMarker(string value)
        {
            if (string.IsNullOrEmpty(value) || IsAltFutaMarker(value))
                return false;

            string lower = value.ToLowerInvariant();
            return lower.Contains("futanari") ||
                   lower.Contains("futa_") ||
                   lower.Contains("_futa") ||
                   lower.Contains("futa-") ||
                   lower.Contains("-futa") ||
                   lower.Contains("futa ") ||
                   lower.StartsWith("futa");
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

        private bool GeometryIsNativeFutanari(JSONNode geometry)
        {
            if (geometry == null)
                return false;

            string character = geometry["character"].Value;
            if (ContainsNativeFutaMarker(character))
                return true;

            bool femaleMorphsOnMale = string.Equals(
                geometry["useFemaleMorphsOnMale"].Value,
                "true",
                StringComparison.OrdinalIgnoreCase);

            bool maleMorphsOnFemale = string.Equals(
                geometry["useMaleMorphsOnFemale"].Value,
                "true",
                StringComparison.OrdinalIgnoreCase);

            string serialized = geometry.ToString();
            bool hasFemaleBase = serialized.IndexOf(
                "MVR_G2Female",
                StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasMaleBase = serialized.IndexOf(
                "MVR_G2Male",
                StringComparison.OrdinalIgnoreCase) >= 0;

            return (femaleMorphsOnMale && hasFemaleBase) ||
                   (maleMorphsOnFemale && hasMaleBase);
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

        public bool IsNativeFutanariAppearance(
            string path,
            string source,
            string label)
        {
            string probe = (label ?? "") + " " +
                           (source ?? "") + " " +
                           (path ?? "");

            if (IsAltFutaMarker(probe))
                return false;

            if (ContainsNativeFutaMarker(probe))
                return true;

            JSONNode root = ReadPresetRoot(path);
            return GeometryIsNativeFutanari(FindGeometry(root));
        }

        public bool IsNativeFutanariBodyPreset(
            string path,
            string label)
        {
            string probe = (label ?? "") + " " + (path ?? "");
            if (IsAltFutaMarker(probe))
                return false;

            if (ContainsNativeFutaMarker(probe))
                return true;

            JSONNode root = ReadPresetRoot(path);
            return GeometryIsNativeFutanari(FindGeometry(root));
        }

        public bool IsNativeFutanariScenePerson(
            JSONNode personNode,
            string scenePath,
            string source)
        {
            if (personNode == null)
                return false;

            string probe = personNode["id"].Value + " " +
                           (scenePath ?? "") + " " +
                           (source ?? "");

            if (IsAltFutaMarker(probe))
                return false;

            JSONNode geometry = FindGeometry(personNode);
            string character = geometry == null ? "" : geometry["character"].Value;

            if (ContainsNativeFutaMarker(probe + " " + character))
                return true;

            return GeometryIsNativeFutanari(geometry);
        }

        public bool IsNativeFutanariAtom(Atom atom)
        {
            if (atom == null)
                return false;

            try
            {
                JSONStorable geometry = atom.GetStorableByID("geometry");
                if (geometry == null)
                    return false;

                string[] names = new string[] { "character", "Character" };
                for (int i = 0; i < names.Length; i++)
                {
                    JSONStorableStringChooser chooser =
                        geometry.GetStringChooserJSONParam(names[i]);
                    if (chooser != null && ContainsNativeFutaMarker(chooser.val))
                        return true;

                    JSONStorableString text =
                        geometry.GetStringJSONParam(names[i]);
                    if (text != null && ContainsNativeFutaMarker(text.val))
                        return true;
                }
            }
            catch { }

            return false;
        }
    }
}
