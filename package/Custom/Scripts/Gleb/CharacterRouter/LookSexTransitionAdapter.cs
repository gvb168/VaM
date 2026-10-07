using System;

namespace GlebVAM
{
    // v232: isolates profile-boundary cleanup for Look changes on the same Person.
    // VaM Appearance presets can leave cross-sex morph-bank flags alive when a Person
    // moves through Female/Male/native-Futanari/AltFuta profiles without respawning.
    // This adapter clears only those cross-bank enable flags before the target Look is
    // loaded; CharacterRouter then performs one settled confirmation load of the same
    // target preset. Same-profile Look changes are not touched.
    internal sealed class LookSexTransitionAdapter
    {
        public string TargetProfile(string catalogSex, bool isAltFuta)
        {
            if (isAltFuta)
                return "ALTFUTA";

            if (string.Equals(catalogSex, "X", StringComparison.OrdinalIgnoreCase))
                return "NATIVE_FUTA";

            if (string.Equals(catalogSex, "M", StringComparison.OrdinalIgnoreCase))
                return "M";

            if (string.Equals(catalogSex, "F", StringComparison.OrdinalIgnoreCase))
                return "F";

            return "?";
        }

        public bool RequiresBoundaryStabilization(string fromProfile, string toProfile)
        {
            if (string.IsNullOrEmpty(fromProfile) || string.IsNullOrEmpty(toProfile) ||
                fromProfile == "?" || toProfile == "?")
                return false;

            return !string.Equals(fromProfile, toProfile, StringComparison.OrdinalIgnoreCase);
        }

        public bool Prepare(Atom atom, string fromProfile, string toProfile, out string detail)
        {
            detail = "";

            if (atom == null)
            {
                detail = "Person atom is null.";
                return false;
            }

            if (!RequiresBoundaryStabilization(fromProfile, toProfile))
            {
                detail = "same-profile";
                return true;
            }

            try
            {
                JSONStorable geometry = atom.GetStorableByID("geometry");
                if (geometry == null)
                {
                    detail = "geometry storable unavailable";
                    return false;
                }

                int found = 0;
                int cleared = 0;

                string[] names = new string[]
                {
                    "useFemaleMorphsOnMale",
                    "useMaleMorphsOnFemale"
                };

                for (int i = 0; i < names.Length; i++)
                {
                    JSONStorableBool p = geometry.GetBoolJSONParam(names[i]);
                    if (p == null)
                        continue;

                    found++;
                    if (p.val)
                    {
                        p.val = false;
                        cleared++;
                    }
                }

                detail =
                    "from=" + (fromProfile ?? "") +
                    " | to=" + (toProfile ?? "") +
                    " | flagsFound=" + found +
                    " | flagsCleared=" + cleared;

                return true;
            }
            catch (Exception e)
            {
                detail = e.Message;
                return false;
            }
        }
    }
}
