using UnityEngine;
using System;
using System.Collections.Generic;

namespace GlebVAM
{
    // Applies Morph Presets as transient BODY-only geometry.
    //
    // v146 retains the hardened v145 live-morph path and is now cleaned by Router before Person removal/fresh spawn:
    // - generated CR_BodySplit_* morphs are owned and removed, not merely zeroed;
    // - orphaned v144 morphs are purged before every new BODY operation;
    // - failure rolls back both source values and any partially registered morph;
    // - Complete reports the exact stage that failed;
    // - ApplyMorphsImmediate is deliberately avoided immediately after changing
    //   the morph-bank structure. VaM gets a normal update/fixed-update frame to
    //   consume the rebuilt lookup tables instead.
    //
    // Body/head split geometry is adapted from Skynet MorphMergeAndSplit v3
    // (CC BY) via BodyMorphSplit.cs.
    internal sealed class BodyPresetAdapter
    {
        private const string OwnedPrefix = "CR_BodySplit_";

        internal sealed class Session
        {
            public DAZCharacterSelector selector;

            public Dictionary<DAZMorph, float> bank1 =
                new Dictionary<DAZMorph, float>();

            public Dictionary<DAZMorph, float> bank2 =
                new Dictionary<DAZMorph, float>();

            public Dictionary<DAZMorph, float> bank3 =
                new Dictionary<DAZMorph, float>();
        }

        private sealed class OwnedMorph
        {
            public DAZMorphBank bank;
            public DAZMorph morph;
        }

        private readonly Dictionary<string, List<OwnedMorph>>
            _activeMorphsByPerson =
                new Dictionary<string, List<OwnedMorph>>();

        private int _serial = 0;

        public Session Begin(
            Atom atom,
            string personUid,
            out string error)
        {
            error = "";

            if (atom == null)
            {
                error = "Person atom is null.";
                return null;
            }

            try
            {
                // This also purges untracked CR_BodySplit_* leftovers from v144.
                ClearActive(atom, personUid);

                DAZCharacterSelector selector =
                    atom.GetComponentInChildren<DAZCharacterSelector>();

                if (selector == null)
                {
                    error = "DAZCharacterSelector was not found.";
                    return null;
                }

                Session session = new Session();
                session.selector = selector;

                SnapshotBank(selector.morphBank1, session.bank1);
                SnapshotBank(selector.morphBank2, session.bank2);
                SnapshotBank(selector.morphBank3, session.bank3);

                return session;
            }
            catch (Exception e)
            {
                error = e.Message;
                SuperController.LogError(
                    "CharacterRouter v146: BodyPresetAdapter.Begin exception: " +
                    e
                );
                return null;
            }
        }

        public bool Complete(
            Atom atom,
            string personUid,
            Session session,
            out string error)
        {
            error = "";

            if (atom == null ||
                session == null ||
                session.selector == null)
            {
                error = "BODY split session is not available.";
                return false;
            }

            List<OwnedMorph> created =
                new List<OwnedMorph>();

            string stage = "prepare";

            try
            {
                DAZMorphBank bank1 = session.selector.morphBank1;
                DAZMorphBank bank2 = session.selector.morphBank2;
                DAZMorphBank bank3 = session.selector.morphBank3;

                stage = "capture-main-delta";
                BodyMorphBuffer main =
                    BuildChangedBuffer(bank1, session.bank1);

                stage = "capture-genital2-delta";
                BodyMorphBuffer genital2 =
                    BuildChangedBuffer(bank2, session.bank2);

                stage = "capture-genital3-delta";
                BodyMorphBuffer genital3 =
                    BuildChangedBuffer(bank3, session.bank3);

                // Restore the native Morph Preset before installing our overlay.
                // The source Look's ordinary morph values are therefore the base
                // from which the generated BODY-only morph is applied.
                stage = "restore-source-morph-values";
                RestoreBank(bank1, session.bank1);
                RestoreBank(bank2, session.bank2);
                RestoreBank(bank3, session.bank3);

                stage = "split-main-head-body";
                BodyMorphBuffer bodyOnly = SplitMainBuffer(main);

                if (!bodyOnly.IsEmpty)
                {
                    stage = "register-main-body-morph";
                    RegisterBuffer(
                        bank1,
                        bodyOnly,
                        MakeName(personUid, "Body"),
                        created
                    );
                }

                if (!genital2.IsEmpty)
                {
                    stage = "register-genital2-morph";
                    RegisterBuffer(
                        bank2,
                        genital2,
                        MakeName(personUid, "Genital2"),
                        created
                    );
                }

                if (!genital3.IsEmpty)
                {
                    stage = "register-genital3-morph";
                    RegisterBuffer(
                        bank3,
                        genital3,
                        MakeName(personUid, "Genital3"),
                        created
                    );
                }

                if (created.Count == 0)
                {
                    error =
                        "The preset produced no BODY/genital geometry changes.";
                    return false;
                }

                // Do not force ApplyMorphsImmediate here. v144 changed the bank
                // structure and immediately entered the apply path in the same
                // call stack; that is exactly where an out-of-range failure can
                // leave VaM with half-installed lookup state. Let VaM process the
                // rebuilt bank on its normal update/fixed-update cycle instead.
                stage = "activate-generated-morphs";
                for (int i = 0; i < created.Count; i++)
                {
                    DAZMorph morph = created[i].morph;
                    if (morph == null)
                        continue;

                    morph.morphValue = 1f;
                    TrySyncMorph(morph);
                }

                _activeMorphsByPerson[personUid] = created;

                SuperController.LogMessage(
                    "CharacterRouter v146: BodyPresetAdapter.Complete OK" +
                    " | person=" + (personUid ?? "") +
                    " | generated=" + created.Count
                );

                return true;
            }
            catch (Exception e)
            {
                // First remove any partially installed generated morphs. The
                // entry is added to 'created' BEFORE AddMorphUsingSubBanks, so
                // even an exception during registration can be rolled back.
                try
                {
                    CleanupOwnedMorphs(created);
                }
                catch
                {
                }

                // Restore the native preset values regardless of failure stage.
                try
                {
                    RestoreBank(
                        session.selector.morphBank1,
                        session.bank1
                    );
                    RestoreBank(
                        session.selector.morphBank2,
                        session.bank2
                    );
                    RestoreBank(
                        session.selector.morphBank3,
                        session.bank3
                    );
                }
                catch
                {
                }

                // Last-resort cleanup for leftovers not represented in 'created'.
                try
                {
                    PurgeOwnedMorphs(session.selector);
                }
                catch
                {
                }

                error = stage + ": " + e.Message;

                SuperController.LogError(
                    "CharacterRouter v146: BodyPresetAdapter.Complete exception" +
                    " | stage=" + stage +
                    " | person=" + (personUid ?? "") +
                    "\n" + e
                );

                return false;
            }
        }

        public void ClearActive(
            Atom atom,
            string personUid)
        {
            List<OwnedMorph> active = null;

            if (!string.IsNullOrEmpty(personUid))
            {
                _activeMorphsByPerson.TryGetValue(
                    personUid,
                    out active
                );
            }

            if (active != null)
            {
                CleanupOwnedMorphs(active);
                _activeMorphsByPerson.Remove(personUid);
            }

            // v144 could fail before it recorded its transient morphs. Scan the
            // actual banks too, so RESET/ORIGINAL BODY and the next preset can
            // recover from those orphaned objects.
            if (atom != null)
            {
                try
                {
                    DAZCharacterSelector selector =
                        atom.GetComponentInChildren<DAZCharacterSelector>();

                    if (selector != null)
                        PurgeOwnedMorphs(selector);
                }
                catch (Exception e)
                {
                    SuperController.LogError(
                        "CharacterRouter v146: BODY transient purge failed: " + e
                    );
                }
            }
        }

        private string MakeName(
            string personUid,
            string suffix)
        {
            _serial++;

            string uid =
                string.IsNullOrEmpty(personUid)
                ? "Person"
                : personUid;

            uid = uid.Replace(" ", "_")
                     .Replace("/", "_")
                     .Replace("\\", "_")
                     .Replace(":", "_");

            return
                OwnedPrefix +
                uid +
                "_" +
                suffix +
                "_" +
                _serial;
        }

        private void SnapshotBank(
            DAZMorphBank bank,
            Dictionary<DAZMorph, float> snapshot)
        {
            if (bank == null || bank.morphs == null)
                return;

            List<DAZMorph> morphs = bank.morphs;

            for (int i = 0; i < morphs.Count; i++)
            {
                DAZMorph morph = morphs[i];

                if (morph != null && !IsOwnedMorph(morph))
                    snapshot[morph] = morph.morphValue;
            }
        }

        private float GetBeforeValue(
            DAZMorph morph,
            Dictionary<DAZMorph, float> snapshot)
        {
            float value;

            if (snapshot.TryGetValue(morph, out value))
                return value;

            if (morph != null && morph.jsonFloat != null)
                return morph.jsonFloat.defaultVal;

            return 0f;
        }

        private BodyMorphBuffer BuildChangedBuffer(
            DAZMorphBank bank,
            Dictionary<DAZMorph, float> before)
        {
            BodyMorphBuffer result = new BodyMorphBuffer();

            if (bank == null || bank.morphs == null)
                return result;

            List<DAZMorph> morphs = bank.morphs;

            for (int i = 0; i < morphs.Count; i++)
            {
                DAZMorph morph = morphs[i];

                if (morph == null || IsOwnedMorph(morph))
                    continue;

                float oldValue = GetBeforeValue(morph, before);
                float deltaValue = morph.morphValue - oldValue;

                if (Mathf.Abs(deltaValue) < 0.00001f)
                    continue;

                try
                {
                    if (morph.numDeltas > 0 &&
                        (morph.deltas == null || morph.deltas.Length == 0))
                    {
                        morph.LoadDeltas();
                    }
                }
                catch
                {
                }

                if (morph.deltas != null)
                {
                    for (int j = 0; j < morph.deltas.Length; j++)
                    {
                        DAZMorphVertex v = morph.deltas[j];
                        result.AddDelta(v.vertex, v.delta, deltaValue);
                    }
                }

                if (morph.formulas != null)
                {
                    for (int j = 0; j < morph.formulas.Length; j++)
                    {
                        DAZMorphFormula f = morph.formulas[j];

                        if (f == null)
                            continue;

                        // Same exclusions used by MorphMergeAndSplit.
                        if (f.targetType == DAZMorphFormulaTargetType.MCM ||
                            f.targetType == DAZMorphFormulaTargetType.MCMMult ||
                            f.targetType == DAZMorphFormulaTargetType.MorphValue)
                        {
                            continue;
                        }

                        result.AddFormula(
                            f.target,
                            f.targetType,
                            f.multiplier,
                            deltaValue
                        );
                    }
                }
            }

            return result;
        }

        private void RestoreBank(
            DAZMorphBank bank,
            Dictionary<DAZMorph, float> before)
        {
            if (bank == null || bank.morphs == null)
                return;

            List<DAZMorph> morphs = bank.morphs;

            for (int i = 0; i < morphs.Count; i++)
            {
                DAZMorph morph = morphs[i];

                if (morph == null || IsOwnedMorph(morph))
                    continue;

                float value;

                if (before.TryGetValue(morph, out value))
                {
                    morph.morphValue = value;
                }
                else
                {
                    try
                    {
                        morph.SetDefaultValue();
                    }
                    catch
                    {
                        morph.morphValue = 0f;
                    }
                }

                TrySyncMorph(morph);
            }
        }

        private BodyMorphBuffer SplitMainBuffer(
            BodyMorphBuffer source)
        {
            if (source == null || source.IsEmpty)
                return new BodyMorphBuffer();

            BodyMorphBuffer head;
            BodyMorphBuffer body;

            source.ApplyFilter(
                BodyMorphSplitData.HeadFilter,
                out head,
                out body
            );

            Vector3 neckOffset =
                CalculateAverageOffset(
                    source,
                    BodyMorphSplitData.NeckSeamVertices
                );

            if (neckOffset.sqrMagnitude > 0.0001f)
            {
                head = head.ApplyOffsetOn(
                    -neckOffset,
                    BodyMorphSplitData.HeadFilter
                );

                body = body.ApplyOffsetOn(
                    neckOffset,
                    BodyMorphSplitData.HeadFilter
                );
            }

            const int neckFalloffLoops = 7;

            BodyMorphBuffer.ApplyFalloffTransition(
                source,
                BodyMorphSplitData.NeckFalloff,
                head,
                body,
                neckFalloffLoops
            );

            if (neckOffset.sqrMagnitude > 0.0001f)
            {
                BodyMorphBuffer.ApplyFalloffOffset(
                    BodyMorphSplitData.NeckFalloff,
                    head,
                    body,
                    neckOffset,
                    neckFalloffLoops
                );
            }

            return body;
        }

        private Vector3 CalculateAverageOffset(
            BodyMorphBuffer buffer,
            int[] vertices)
        {
            Vector3 total = Vector3.zero;
            int count = 0;

            if (buffer == null || vertices == null)
                return total;

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 delta;

                if (buffer.deltas.TryGetValue(vertices[i], out delta))
                {
                    total += delta;
                    count++;
                }
            }

            if (count == 0)
                return Vector3.zero;

            return total / count;
        }

        private void RegisterBuffer(
            DAZMorphBank bank,
            BodyMorphBuffer buffer,
            string name,
            List<OwnedMorph> created)
        {
            if (bank == null || buffer == null || buffer.IsEmpty)
                return;

            DAZMorph morph =
                buffer.ToMorph(
                    name,
                    "CharacterRouter Body",
                    bank
                );

            OwnedMorph owned = new OwnedMorph();
            owned.bank = bank;
            owned.morph = morph;

            // Add to rollback list before touching the bank. If the VaM call
            // itself throws after a partial insertion, cleanup can still find it.
            created.Add(owned);

            bool added = bank.AddMorphUsingSubBanks(morph);

            if (!added)
            {
                throw new Exception(
                    "VaM rejected transient BODY morph " + name
                );
            }

            bank.RebuildAllLookups();
        }

        private void CleanupOwnedMorphs(
            List<OwnedMorph> ownedMorphs)
        {
            if (ownedMorphs == null)
                return;

            HashSet<DAZMorphBank> rebuild =
                new HashSet<DAZMorphBank>();

            for (int i = ownedMorphs.Count - 1; i >= 0; i--)
            {
                OwnedMorph entry = ownedMorphs[i];

                if (entry == null || entry.morph == null)
                    continue;

                entry.morph.morphValue = 0f;
                TrySyncMorph(entry.morph);

                if (RemoveMorphFromBank(entry.bank, entry.morph))
                    rebuild.Add(entry.bank);
            }

            foreach (DAZMorphBank bank in rebuild)
            {
                if (bank != null)
                    bank.RebuildAllLookups();
            }
        }

        private bool RemoveMorphFromBank(
            DAZMorphBank bank,
            DAZMorph morph)
        {
            if (bank == null || bank.morphs == null || morph == null)
                return false;

            bool removed = false;
            List<DAZMorph> morphs = bank.morphs;

            for (int i = morphs.Count - 1; i >= 0; i--)
            {
                DAZMorph candidate = morphs[i];

                if (object.ReferenceEquals(candidate, morph))
                {
                    morphs.RemoveAt(i);
                    removed = true;
                }
            }

            return removed;
        }

        private void PurgeOwnedMorphs(
            DAZCharacterSelector selector)
        {
            if (selector == null)
                return;

            PurgeOwnedMorphsFromBank(selector.morphBank1);
            PurgeOwnedMorphsFromBank(selector.morphBank2);
            PurgeOwnedMorphsFromBank(selector.morphBank3);
        }

        private void PurgeOwnedMorphsFromBank(
            DAZMorphBank bank)
        {
            if (bank == null || bank.morphs == null)
                return;

            bool removed = false;
            List<DAZMorph> morphs = bank.morphs;

            for (int i = morphs.Count - 1; i >= 0; i--)
            {
                DAZMorph morph = morphs[i];

                if (!IsOwnedMorph(morph))
                    continue;

                morph.morphValue = 0f;
                TrySyncMorph(morph);
                morphs.RemoveAt(i);
                removed = true;
            }

            if (removed)
                bank.RebuildAllLookups();
        }

        private bool IsOwnedMorph(DAZMorph morph)
        {
            if (morph == null)
                return false;

            if (!string.IsNullOrEmpty(morph.uid) &&
                morph.uid.StartsWith(
                    OwnedPrefix,
                    StringComparison.Ordinal
                ))
            {
                return true;
            }

            return
                !string.IsNullOrEmpty(morph.morphName) &&
                morph.morphName.StartsWith(
                    OwnedPrefix,
                    StringComparison.Ordinal
                );
        }

        private void TrySyncMorph(DAZMorph morph)
        {
            if (morph == null)
                return;

            try
            {
                morph.SyncJSON();
            }
            catch
            {
            }
        }
    }
}
