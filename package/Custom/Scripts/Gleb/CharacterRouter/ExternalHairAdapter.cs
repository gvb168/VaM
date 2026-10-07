using System;
using System.Collections.Generic;
using SimpleJSON;

namespace GlebVAM
{
    // Compatibility module for hairstyles implemented as Clothing + plugin.
    // It stays outside CharacterRouter's native Hair-bank logic.
    internal class ExternalHairAdapter
    {
        private const string ClothingPluginMarker = ":plugin#0_Stopper.ClothingPluginManager";

        private readonly Dictionary<string, List<string>> _originalByPerson =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);

        public bool HasSnapshot(string personUid)
        {
            return !string.IsNullOrEmpty(personUid) && _originalByPerson.ContainsKey(personUid);
        }

        public void ClearPerson(string personUid)
        {
            if (!string.IsNullOrEmpty(personUid))
                _originalByPerson.Remove(personUid);
        }

        public bool CaptureAndDisable(
            Atom atom,
            DAZCharacterSelector selector,
            string personUid,
            out bool disabledAny,
            out string detail)
        {
            disabledAny = false;
            detail = "";

            if (atom == null || selector == null || string.IsNullOrEmpty(personUid))
            {
                detail = "External Hair adapter: Person/geometry unavailable.";
                return false;
            }

            List<string> original;
            if (!_originalByPerson.TryGetValue(personUid, out original))
            {
                original = FindActiveExternalHairDisplayNames(atom, selector);
                _originalByPerson[personUid] = original;
            }

            if (original == null || original.Count == 0)
                return true;

            try
            {
                foreach (DAZClothingItem item in selector.clothingItems)
                {
                    if (item == null || !item.active)
                        continue;

                    if (ContainsName(original, item.displayName))
                    {
                        selector.SetActiveClothingItem(item, false);
                        disabledAny = true;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                detail = "External Hair adapter could not disable source hair: " + e.Message;
                return false;
            }
        }

        public bool RestoreOriginal(
            Atom atom,
            DAZCharacterSelector selector,
            string personUid,
            out bool restoredAny,
            out string detail)
        {
            restoredAny = false;
            detail = "";

            if (atom == null || selector == null || string.IsNullOrEmpty(personUid))
            {
                detail = "External Hair adapter: Person/geometry unavailable.";
                return false;
            }

            List<string> original;
            if (!_originalByPerson.TryGetValue(personUid, out original) || original == null || original.Count == 0)
                return true;

            try
            {
                foreach (DAZClothingItem item in selector.clothingItems)
                {
                    if (item == null || item.active)
                        continue;

                    if (ContainsName(original, item.displayName))
                    {
                        selector.SetActiveClothingItem(item, true);
                        restoredAny = true;
                    }
                }
                return true;
            }
            catch (Exception e)
            {
                detail = "External Hair adapter could not restore source hair: " + e.Message;
                return false;
            }
        }

        private List<string> FindActiveExternalHairDisplayNames(Atom atom, DAZCharacterSelector selector)
        {
            List<string> result = new List<string>();
            HashSet<string> candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                List<string> storableIds = atom.GetStorableIDs();
                if (storableIds != null)
                {
                    for (int i = 0; i < storableIds.Count; i++)
                    {
                        string id = storableIds[i];
                        if (string.IsNullOrEmpty(id))
                            continue;

                        int marker = id.IndexOf(ClothingPluginMarker, StringComparison.OrdinalIgnoreCase);
                        if (marker <= 0)
                            continue;

                        string internalId = id.Substring(0, marker).Trim();
                        if (!LooksLikeHairName(internalId))
                            continue;

                        JSONStorable storable = atom.GetStorableByID(id);
                        if (storable == null || !UsesCuaClothingAlt(storable))
                            continue;

                        string displayName = DisplayNameFromInternalId(internalId);
                        if (!string.IsNullOrEmpty(displayName))
                            candidateNames.Add(displayName);
                    }
                }

                foreach (DAZClothingItem item in selector.clothingItems)
                {
                    if (item == null || !item.active || string.IsNullOrEmpty(item.displayName))
                        continue;

                    if (candidateNames.Contains(item.displayName) && !ContainsName(result, item.displayName))
                        result.Add(item.displayName);
                }
            }
            catch
            {
                // Fail safe: unknown foreign clothing is left untouched.
            }

            return result;
        }

        private bool UsesCuaClothingAlt(JSONStorable storable)
        {
            try
            {
                JSONClass state = storable.GetJSON(true, true, true);
                if (state == null)
                    return false;

                JSONClass plugins = state["plugins"].AsObject;
                if (plugins == null)
                    return false;

                foreach (string key in plugins.Keys)
                {
                    string path = plugins[key].Value;
                    if (!string.IsNullOrEmpty(path) &&
                        path.IndexOf("CUAClothingAlt", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private string DisplayNameFromInternalId(string internalId)
        {
            if (string.IsNullOrEmpty(internalId))
                return "";

            int colon = internalId.LastIndexOf(':');
            return colon >= 0 && colon + 1 < internalId.Length
                ? internalId.Substring(colon + 1).Trim()
                : internalId.Trim();
        }

        private bool LooksLikeHairName(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   value.IndexOf("hair", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool ContainsName(List<string> values, string name)
        {
            if (values == null || string.IsNullOrEmpty(name))
                return false;

            for (int i = 0; i < values.Count; i++)
            {
                if (string.Equals(values[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
