using System;
using System.Collections.Generic;
using SimpleJSON;

namespace GlebVAM
{
    // AltFutaHairAdapter v190
    // AltFuta is a native Female Person. Only INTIMATE uses Male Hair presets.
    // Keep a completely separate clone/source-resolution lifecycle from native Futanari,
    // while reusing the already-tested opposite-bank engine internally.
    public sealed class AltFutaHairAdapter
    {
        private readonly NativeFutanariHairAdapter _crossGenderEngine =
            new NativeFutanariHairAdapter();

        public void RegisterInstalledPackageUid(string packageUid)
        {
            _crossGenderEngine.RegisterInstalledPackageUid(packageUid);
        }

        public void ClearPerson(string uid)
        {
            _crossGenderEngine.ClearPerson(uid);
        }

        public bool ResolveExactSourceClones(
            string personUid,
            DAZCharacterSelector selector,
            JSONArray desiredHair,
            string nativeSex,
            string desiredSex,
            out List<string> resolvedNames,
            out HashSet<int> preferredInstanceIds,
            out string error)
        {
            return _crossGenderEngine.ResolveExactSourceClones(
                personUid, selector, desiredHair, nativeSex, desiredSex,
                out resolvedNames, out preferredInstanceIds, out error
            );
        }

        public void DeactivateOppositeBankRegion(
            DAZCharacterSelector selector,
            string nativeSex,
            string targetRegion,
            Func<string, string> resolveRegion)
        {
            _crossGenderEngine.DeactivateOppositeBankRegion(
                selector, nativeSex, targetRegion, resolveRegion
            );
        }
    }
}
