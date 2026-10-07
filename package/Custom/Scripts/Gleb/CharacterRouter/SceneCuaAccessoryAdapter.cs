using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using MVR.FileManagementSecure;
using SimpleJSON;

namespace GlebVAM
{
    // Restores scene atoms that are part of a character's appearance but are
    // stored outside the Person. Created atoms are tracked across Look
    // changes and Person removal.
    public class SceneCuaAccessoryAdapter
    {
        // Measured by ElowenAnchorProbe on the actual CUA_TIARA and Person:head
        // in the author's Dragontales.Elowen.3 scene. JSON head.position is
        // bone-local and cannot reconstruct the live head transform by adding
        // it to the saved headControl position (the v243/v244 mistake).
        private static readonly Vector3 ElowenTiaraHeadPosition =
            new Vector3(-0.00014f, 0.10927f, 0.03908f);
        private static readonly Quaternion ElowenTiaraHeadRotation =
            Quaternion.Euler(0.84998f, 0.03289f, 0.08108f);

        private readonly Dictionary<string, Atom> _managed =
            new Dictionary<string, Atom>(StringComparer.Ordinal);
        private readonly Dictionary<string, Dictionary<Renderer, bool>> _kaineHairRenderers =
            new Dictionary<string, Dictionary<Renderer, bool>>(StringComparer.Ordinal);

        // VaM's CUA linkTo can retain its old world offset when a Pose Preset
        // restores the Person controls. Keep the measured head-local anchor
        // during subsequent pose changes as well as at initial load.
        public void Sync2BSceneHairAnchors()
        {
            if (_managed.Count == 0 || SuperController.singleton == null)
                return;

            foreach (KeyValuePair<string, Atom> entry in _managed)
            {
                Atom hair = entry.Value;
                if (hair == null || hair.mainController == null ||
                    hair.uid == null)
                    continue;

                bool sc6 = hair.uid == entry.Key +
                    "#CharacterRouter_2B_SC6_Hair";
                bool kaine = hair.uid == entry.Key +
                    "#CharacterRouter_2B_Kaine_Hair";
                if (!sc6 && !kaine)
                    continue;

                Atom person = SuperController.singleton.GetAtomByUid(entry.Key);
                DAZBone head = person == null ? null :
                    person.GetStorableByID("head") as DAZBone;
                if (head == null)
                    continue;

                Vector3 local = sc6 ?
                    new Vector3(0.00035f, -1.61829f, 0.00438f) :
                    new Vector3(0.00035f, -1.61829f, 0.00436f);
                Quaternion localRotation = sc6 ?
                    Quaternion.Euler(0.17861f, -0.00154f, 0.01053f) :
                    Quaternion.Euler(0.17848f, -0.00135f, 0.01050f);
                Vector3 target = head.transform.TransformPoint(local);
                Quaternion targetRotation = head.transform.rotation *
                    localRotation;
                Transform control = hair.mainController.transform;
                if ((control.position - target).sqrMagnitude > 0.000001f)
                    control.position = target;
                if (Quaternion.Angle(control.rotation, targetRotation) > 0.1f)
                    control.rotation = targetRotation;
            }
        }

        // Kaine's original hairstyle is a separate CUA, outside VaM's Hair
        // bank. Keep it alive for RESET HEAD, but hide its renderers while
        // another Head hairstyle is selected. Other scene CUA accessories
        // (including Elowen's tiara) must remain untouched.
        public bool Set2BSceneHairVisible(Atom person, bool visible)
        {
            if (person == null || string.IsNullOrEmpty(person.uid))
                return false;

            Atom hair;
            if (!_managed.TryGetValue(person.uid, out hair) || hair == null ||
                (hair.uid != person.uid + "#CharacterRouter_2B_Kaine_Hair" &&
                 hair.uid != person.uid + "#CharacterRouter_2B_SC6_Hair"))
                return false;

            Dictionary<Renderer, bool> original;
            if (visible)
            {
                if (!_kaineHairRenderers.TryGetValue(person.uid, out original))
                    return false;
                foreach (KeyValuePair<Renderer, bool> renderer in original)
                    if (renderer.Key != null)
                        renderer.Key.enabled = renderer.Value;
                _kaineHairRenderers.Remove(person.uid);
            }
            else
            {
                if (_kaineHairRenderers.ContainsKey(person.uid))
                    return false;
                Renderer[] renderers = hair.GetComponentsInChildren<Renderer>(true);
                if (renderers == null || renderers.Length == 0)
                    return false;
                original = new Dictionary<Renderer, bool>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null)
                        continue;
                    original[renderers[i]] = renderers[i].enabled;
                    renderers[i].enabled = false;
                }
                _kaineHairRenderers[person.uid] = original;
            }

            SuperController.LogMessage(
                "CharacterRouter v272 2B SCENE HAIR | stage=" +
                (visible ? "SHOW_ORIGINAL" : "HIDE_ORIGINAL") +
                " | person=" + person.uid +
                " | renderers=" + original.Count);
            return true;
        }

        private static float Number(JSONNode node, string key)
        {
            float value;
            return node != null && float.TryParse(
                node[key].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out value) ? value : 0f;
        }

        private static JSONClass JsonVector(Vector3 v)
        {
            JSONClass node = new JSONClass();
            node["x"] = v.x.ToString("R",
                System.Globalization.CultureInfo.InvariantCulture);
            node["y"] = v.y.ToString("R",
                System.Globalization.CultureInfo.InvariantCulture);
            node["z"] = v.z.ToString("R",
                System.Globalization.CultureInfo.InvariantCulture);
            return node;
        }

        private static JSONNode FindStorable(JSONNode atom, string id)
        {
            JSONArray list = atom == null ? null : atom["storables"].AsArray;
            for (int i = 0; list != null && i < list.Count; i++)
                if (list[i] != null && list[i]["id"].Value == id)
                    return list[i];
            return null;
        }

        private static string Qualify(string path, string package)
        {
            return !string.IsNullOrEmpty(path) &&
                path.StartsWith("SELF:/", StringComparison.OrdinalIgnoreCase)
                ? package + ":/" + path.Substring(6) : path;
        }

        private static string ResolveInstalledBundle(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            int separator = path.IndexOf(":/", StringComparison.Ordinal);
            if (separator < 0)
                return path;

            string package = path.Substring(0, separator);
            if (!package.EndsWith(".latest", StringComparison.OrdinalIgnoreCase))
                return path;

            // A direct assignment of .latest:/ to CustomUnityAssetLoader can
            // fail even though the same CUA loads from its original scene.
            // Give it an installed, numeric package version instead.
            string baseUid = package.Substring(0,
                package.Length - ".latest".Length);
            string suffix = path.Substring(separator);
            try
            {
                string normalized = FileManagerSecure.NormalizeLoadPath(path);
                if (!string.IsNullOrEmpty(normalized) &&
                    normalized.IndexOf(".latest:/",
                        StringComparison.OrdinalIgnoreCase) < 0 &&
                    FileManagerSecure.FileExists(normalized))
                    return normalized;
            }
            catch { }

            try
            {
                int version = FileManagerSecure.GetPackageVersion(baseUid);
                if (version > 0)
                {
                    string exact = baseUid + "." + version + suffix;
                    if (FileManagerSecure.FileExists(exact))
                        return exact;
                }
            }
            catch { }

            // GetPackageVersion may return 0 for a mounted package, so use
            // the actual assetbundle as the final check.
            for (int version = 20; version >= 1; version--)
            {
                string exact = baseUid + "." + version + suffix;
                try
                {
                    if (FileManagerSecure.FileExists(exact))
                        return exact;
                }
                catch { }
            }
            return path;
        }

        public void ClearManaged(Atom person)
        {
            if (person == null || string.IsNullOrEmpty(person.uid))
                return;

            _kaineHairRenderers.Remove(person.uid);

            Atom accessory;
            if (!_managed.TryGetValue(person.uid, out accessory))
                return;
            _managed.Remove(person.uid);

            if (accessory == null || SuperController.singleton == null)
                return;

            try
            {
                bool wasKaineHair = accessory.uid != null &&
                    (accessory.uid.IndexOf("CharacterRouter_2B_Kaine_Hair",
                        StringComparison.Ordinal) >= 0 ||
                     accessory.uid.IndexOf("CharacterRouter_2B_SC6_Hair",
                        StringComparison.Ordinal) >= 0);
                SuperController.singleton.RemoveAtom(accessory);
                SuperController.LogMessage(
                    (wasKaineHair
                        ? "CharacterRouter v261 2B-KAINE HAIR | stage=REMOVED"
                        : "CharacterRouter v245 ELOWEN | stage=TIARA_REMOVED") +
                    " | person=" + person.uid);
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=REMOVE_ERROR" +
                    " | person=" + person.uid + " | detail=" + e);
            }
        }

        private static IEnumerator RestorePlugin(
            Atom accessory, MVRPluginManager manager, JSONNode source,
            string slot, string package)
        {
            string path = Qualify(source["plugins"][slot].Value, package);
            if (string.IsNullOrEmpty(path))
                yield break;

            HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
            JSONClass before = manager.GetJSON(true, true, true)["plugins"].AsObject;
            if (before != null)
                foreach (string key in before.Keys) existing.Add(key);

            var created = manager.CreatePlugin();
            if (created == null || created.pluginURLJSON == null)
                yield break;
            created.pluginURLJSON.val = path;

            string targetSlot = "";
            float until = Time.realtimeSinceStartup + 6f;
            while (string.IsNullOrEmpty(targetSlot) &&
                   Time.realtimeSinceStartup < until)
            {
                JSONClass current = manager.GetJSON(true, true, true)["plugins"].AsObject;
                if (current != null)
                    foreach (string key in current.Keys)
                        if (!existing.Contains(key) && current[key].Value == path)
                        { targetSlot = key; break; }
                if (string.IsNullOrEmpty(targetSlot)) yield return null;
            }

            JSONArray states = source["storables"].AsArray;
            for (int i = 0; !string.IsNullOrEmpty(targetSlot) &&
                            states != null && i < states.Count; i++)
            {
                JSONNode saved = states[i];
                string id = saved == null ? "" : saved["id"].Value;
                if (!id.StartsWith(slot + "_", StringComparison.Ordinal))
                    continue;

                string targetId = targetSlot + id.Substring(slot.Length);
                JSONStorable target = null;
                float ready = Time.realtimeSinceStartup + 8f;
                while (accessory != null && target == null &&
                       Time.realtimeSinceStartup < ready)
                {
                    target = accessory.GetStorableByID(targetId);
                    if (target == null) yield return null;
                }

                if (target == null)
                    continue;
                JSONClass state = JSON.Parse(saved.ToString()).AsObject;
                if (state == null)
                    continue;
                state["id"] = targetId;
                try { target.RestoreFromJSON(state, true, true, null, false); }
                catch (Exception e)
                {
                    SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=PLUGIN_STATE_ERROR" +
                        " | storable=" + targetId + " | detail=" + e);
                }
            }
        }

        public IEnumerator ApplyFromScene(Atom person, string scenePath,
            string sceneAtomId, string package, string lookLabel)
        {
            if (person != null && !string.IsNullOrEmpty(package) &&
                package.IndexOf("immyneedscake.Nier-2B-Compendium",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (string.Equals(lookLabel, "2B-Kaine",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lookLabel, "2B Kaine",
                        StringComparison.OrdinalIgnoreCase))
                {
                    yield return Apply2BSceneHair(person, package, false);
                    yield break;
                }
                if (string.Equals(lookLabel, "2B-SC6",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lookLabel, "2B SC6",
                        StringComparison.OrdinalIgnoreCase))
                {
                    yield return Apply2BSceneHair(person, package, true);
                    yield break;
                }
            }

            if (person == null || person.mainController == null ||
                string.IsNullOrEmpty(package) ||
                !package.Equals("Dragontales.Elowen.3",
                    StringComparison.OrdinalIgnoreCase))
                yield break;

            // The same VAR includes a regular Appearance preset. The catalog
            // deliberately prefers it and hides its duplicate scene Look, so
            // recover the author scene path for that regular preset as well.
            if (string.IsNullOrEmpty(scenePath))
                scenePath = package +
                    ":/Saves/scene/Sean/FINAL_CC-BY/ELOWEN_By Dragontale3D.json";
            if (string.IsNullOrEmpty(sceneAtomId))
                sceneAtomId = "Person";

            JSONNode sourcePerson = null;
            JSONNode sourceTiara = null;
            try
            {
                string raw = FileManagerSecure.ReadAllText(scenePath);
                JSONNode root = JSON.Parse(raw);
                JSONArray atoms = root == null ? null : root["atoms"].AsArray;
                for (int i = 0; atoms != null && i < atoms.Count; i++)
                {
                    JSONNode node = atoms[i];
                    if (node == null) continue;
                    if (node["id"].Value == sceneAtomId &&
                        node["type"].Value == "Person") sourcePerson = node;
                    if (node["id"].Value == "CUA_TIARA" &&
                        node["type"].Value == "CustomUnityAsset")
                        sourceTiara = node;
                }
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=SOURCE_ERROR" +
                    " | detail=" + e);
                yield break;
            }

            JSONNode control = FindStorable(sourceTiara, "control");
            JSONNode asset = FindStorable(sourceTiara, "asset");
            string originalUrl = asset == null ? "" :
                Qualify(asset["assetUrl"].Value, package);
            string url = ResolveInstalledBundle(originalUrl);
            if (sourcePerson == null || control == null || asset == null ||
                control["linkTo"].Value != sceneAtomId + ":head" ||
                url.IndexOf("Dragontales.Crowns_and_Tiaras",
                    StringComparison.OrdinalIgnoreCase) < 0)
                yield break;

            SuperController.LogMessage(
                "CharacterRouter v245 ELOWEN | stage=BUNDLE_PATH" +
                " | original=" + originalUrl +
                " | resolved=" + url);
            if (url.IndexOf(".latest:/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=BUNDLE_NOT_FOUND" +
                    " | bundle=" + originalUrl);
                yield break;
            }

            // The image also contains the original native face decal. Log what
            // VaM actually restored, since the source scene's green lighting can
            // otherwise make identical makeup appear different.
            JSONStorable liveTextures = person.GetStorableByID("textures");
            JSONClass face = liveTextures == null ? null :
                liveTextures.GetJSON(true, true, true);
            JSONNode sourceTextures = FindStorable(sourcePerson, "textures");
            string expectedDecal = sourceTextures == null ? "" :
                Qualify(sourceTextures["faceDecalUrl"].Value, package);
            string actualDecal = face == null ? "" :
                face["faceDecalUrl"].Value;
            if (!string.IsNullOrEmpty(expectedDecal) &&
                !string.Equals(actualDecal, expectedDecal,
                    StringComparison.OrdinalIgnoreCase) &&
                liveTextures != null)
            {
                JSONStorableUrl liveDecal =
                    liveTextures.GetUrlJSONParam("faceDecalUrl");
                if (liveDecal != null)
                {
                    try
                    {
                        liveDecal.val = expectedDecal;
                        actualDecal = expectedDecal;
                    }
                    catch (Exception e)
                    {
                        SuperController.LogError(
                            "CharacterRouter v245 ELOWEN | stage=FACE_ERROR" +
                            " | detail=" + e);
                    }
                }
            }
            SuperController.LogMessage(
                "CharacterRouter v245 ELOWEN | stage=FACE" +
                " | person=" + person.uid +
                " | expected=" + expectedDecal +
                " | actual=" + actualDecal);

            string uid = person.uid + "#CharacterRouter_Elowen_Tiara";
            if (SuperController.singleton.GetAtomByUid(uid) != null)
            {
                SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=UID_IN_USE" +
                    " | person=" + person.uid + " | cua=" + uid);
                yield break;
            }

            IEnumerator adding = null;
            try
            {
                adding = SuperController.singleton.AddAtomByType(
                    "CustomUnityAsset", uid, false, false, false);
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=CREATE_ERROR" +
                    " | detail=" + e);
                yield break;
            }
            if (adding != null)
                while (adding.MoveNext()) yield return adding.Current;

            Atom cua = SuperController.singleton.GetAtomByUid(uid);
            if (cua == null || cua.mainController == null)
            {
                SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=CREATE_MISSING" +
                    " | cua=" + uid);
                yield break;
            }
            _managed[person.uid] = cua;
            _kaineHairRenderers.Remove(person.uid);

            DAZBone liveHead = person.GetStorableByID("head") as DAZBone;
            if (liveHead == null)
            {
                SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=HEAD_BONE_MISSING" +
                    " | person=" + person.uid);
                ClearManaged(person);
                yield break;
            }

            Vector3 relativePos = ElowenTiaraHeadPosition;
            Quaternion relativeRot = ElowenTiaraHeadRotation;
            Transform targetHead = liveHead.transform;
            Vector3 worldPos = targetHead.TransformPoint(relativePos);
            Quaternion worldRot = targetHead.rotation * relativeRot;
            SuperController.LogMessage(
                "CharacterRouter v245 ELOWEN | stage=HEAD_OFFSET" +
                " | person=" + person.uid +
                " | sourceLocalPos=" + relativePos.ToString("F5") +
                " | sourceLocalRot=" + relativeRot.eulerAngles.ToString("F5") +
                " | head=" + targetHead.position.ToString("F5") +
                " | tiara=" + worldPos.ToString("F5"));

            try
            {
                JSONClass linked = JSON.Parse(control.ToString()).AsObject;
                linked["linkTo"] = person.uid + ":head";
                linked["position"] = JsonVector(worldPos);
                linked["rotation"] = JsonVector(worldRot.eulerAngles);
                JSONStorable controller = cua.GetStorableByID("control");
                if (controller != null)
                    controller.RestoreFromJSON(linked, true, true, null, false);
                cua.mainController.transform.position = worldPos;
                cua.mainController.transform.rotation = worldRot;

                JSONNode savedScale = FindStorable(sourceTiara, "scale");
                JSONStorable liveScale = cua.GetStorableByID("scale");
                if (savedScale != null && liveScale != null)
                {
                    JSONStorableFloat scale =
                        liveScale.GetFloatJSONParam("scale");
                    if (scale != null)
                        scale.val = Number(savedScale, "scale");
                }

                JSONStorable loader = cua.GetStorableByID("asset");
                JSONStorableUrl assetUrl = loader == null ? null :
                    loader.GetUrlJSONParam("assetUrl");
                if (assetUrl == null)
                    throw new Exception("CUA asset URL parameter is unavailable");
                assetUrl.val = url;
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=SETUP_ERROR" +
                    " | detail=" + e);
                ClearManaged(person);
                yield break;
            }

            JSONStorable assetLoader = cua.GetStorableByID("asset");
            JSONStorableStringChooser assetName = assetLoader == null ? null :
                assetLoader.GetStringChooserJSONParam("assetName");
            string desired = asset["assetName"].Value;
            float assetDeadline = Time.realtimeSinceStartup + 20f;
            while (cua != null && assetName != null &&
                   (assetName.choices == null ||
                    !assetName.choices.Contains(desired)) &&
                   Time.realtimeSinceStartup < assetDeadline)
                yield return null;

            if (cua == null || assetName == null ||
                assetName.choices == null ||
                !assetName.choices.Contains(desired))
            {
                SuperController.LogError(
                    "CharacterRouter v245 ELOWEN | stage=ASSET_NOT_READY" +
                    " | cua=" + uid + " | asset=" + desired +
                    " | choices=" + (assetName == null ||
                        assetName.choices == null ? 0 :
                        assetName.choices.Count) + " | bundle=" + url);
                ClearManaged(person);
                yield break;
            }
            assetName.val = desired;

            float renderDeadline = Time.realtimeSinceStartup + 10f;
            while (cua != null &&
                   cua.GetComponentsInChildren<Renderer>(true).Length == 0 &&
                   Time.realtimeSinceStartup < renderDeadline)
                yield return null;

            // Preserve the author's color scaling and VaMifier settings if the
            // two original scene plugins are installed.
            JSONNode pluginState = FindStorable(sourceTiara, "PluginManager");
            MVRPluginManager manager =
                cua.GetStorableByID("PluginManager") as MVRPluginManager;
            JSONClass paths = pluginState == null ? null :
                pluginState["plugins"].AsObject;
            if (manager != null && paths != null)
            {
                foreach (string slot in paths.Keys)
                    yield return RestorePlugin(cua, manager, sourceTiara,
                        slot, package);
            }

            // Plugins and the bundle can finish initialization on later frames.
            // Reapply the bone-relative placement after they are ready.
            if (cua != null && liveHead != null)
            {
                cua.mainController.transform.position =
                    liveHead.transform.TransformPoint(relativePos);
                cua.mainController.transform.rotation =
                    liveHead.transform.rotation * relativeRot;
            }

            // Let ParentLink and the asset's transform finish one update before
            // recording the visible renderer's position.
            yield return null;
            if (cua == null || cua.mainController == null || liveHead == null)
                yield break;

            Renderer[] finalRenderers = cua == null ? null :
                cua.GetComponentsInChildren<Renderer>(true);
            SuperController.LogMessage(
                "CharacterRouter v245 ELOWEN | stage=FINAL_PLACEMENT" +
                " | person=" + person.uid +
                " | expected=" + worldPos.ToString("F5") +
                " | actual=" + cua.mainController.transform.position.ToString("F5") +
                " | headLocalPos=" + liveHead.transform.InverseTransformPoint(
                    cua.mainController.transform.position).ToString("F5") +
                " | headLocalRot=" + (Quaternion.Inverse(
                    liveHead.transform.rotation) *
                    cua.mainController.transform.rotation).eulerAngles.ToString("F5") +
                " | renderCenter=" +
                (finalRenderers == null || finalRenderers.Length == 0 ?
                    "none" : finalRenderers[0].bounds.center.ToString("F5")) +
                " | head=" + liveHead.transform.position.ToString("F5"));

            SuperController.LogMessage(
                "CharacterRouter v245 ELOWEN | stage=TIARA_READY" +
                " | person=" + person.uid + " | cua=" + uid +
                " | asset=" + desired +
                " | renderers=" +
                cua.GetComponentsInChildren<Renderer>(true).Length +
                " | linkTo=" + person.uid + ":head");
        }

        private IEnumerator Apply2BSceneHair(Atom person, string package, bool sc6)
        {
            if (person.mainController == null ||
                SuperController.singleton == null)
                yield break;

            string scenePath = package +
                ":/Saves/scene/immyneedscake/2b showcase single.json";
            JSONNode sourcePerson = null;
            JSONNode sourceHair = null;
            try
            {
                JSONNode root = JSON.Parse(FileManagerSecure.ReadAllText(scenePath));
                JSONArray atoms = root == null ? null : root["atoms"].AsArray;
                for (int i = 0; atoms != null && i < atoms.Count; i++)
                {
                    if (atoms[i]["id"].Value == (sc6 ? "Person#2" : "Person#3") &&
                        atoms[i]["type"].Value == "Person")
                        sourcePerson = atoms[i];
                    if (atoms[i]["id"].Value ==
                            (sc6 ? "CustomUnityAsset" : "CustomUnityAsset_Person#3") &&
                        atoms[i]["type"].Value == "CustomUnityAsset")
                        sourceHair = atoms[i];
                }
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v272 2B SCENE HAIR | stage=SOURCE_ERROR" +
                    " | detail=" + e.Message);
                yield break;
            }

            JSONNode authorControl = FindStorable(sourceHair, "control");
            JSONNode authorAsset = FindStorable(sourceHair, "asset");
            if (sourcePerson == null || authorControl == null ||
                authorAsset == null ||
                authorControl["linkTo"].Value != (sc6 ? "Person#2:head" : "Person#3:head"))
            {
                SuperController.LogError(
                    "CharacterRouter v272 2B SCENE HAIR | stage=SOURCE_INCOMPLETE");
                yield break;
            }

            string url = Qualify(authorAsset["assetUrl"].Value, package);
            string assetName = authorAsset["assetName"].Value;
            if (string.IsNullOrEmpty(url) ||
                string.IsNullOrEmpty(assetName) ||
                !FileManagerSecure.FileExists(url))
            {
                SuperController.LogError(
                    "CharacterRouter v272 2B SCENE HAIR | stage=ASSET_MISSING" +
                    " | bundle=" + url);
                yield break;
            }

            // Measured in the running author scene by KaineAnchorProbe:
            // CUA pivot in Person#3:head coordinates. Its prefab pivot sits
            // near the Person root, so root-relative scene JSON positions
            // are not a usable head anchor (the v261/v262 offset).
            Vector3 localPos = sc6 ?
                new Vector3(0.00035f, -1.61829f, 0.00438f) :
                new Vector3(0.00035f, -1.61829f, 0.00436f);
            Quaternion localRot = sc6 ?
                Quaternion.Euler(0.17861f, -0.00154f, 0.01053f) :
                Quaternion.Euler(0.17848f, -0.00135f, 0.01050f);
            DAZBone liveHead = person.GetStorableByID("head") as DAZBone;
            if (liveHead == null)
            {
                SuperController.LogError(
                    "CharacterRouter v272 2B SCENE HAIR | stage=HEAD_MISSING" +
                    " | person=" + person.uid);
                yield break;
            }
            Vector3 worldPos = liveHead.transform.TransformPoint(localPos);
            Quaternion worldRot = liveHead.transform.rotation * localRot;

            string uid = person.uid + (sc6 ?
                "#CharacterRouter_2B_SC6_Hair" :
                "#CharacterRouter_2B_Kaine_Hair");
            if (SuperController.singleton.GetAtomByUid(uid) != null)
            {
                SuperController.LogError(
                    "CharacterRouter v272 2B SCENE HAIR | stage=UID_IN_USE" +
                    " | cua=" + uid);
                yield break;
            }

            IEnumerator adding = null;
            try
            {
                adding = SuperController.singleton.AddAtomByType(
                    "CustomUnityAsset", uid, false, false, false);
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v272 2B SCENE HAIR | stage=CREATE_ERROR" +
                    " | detail=" + e.Message);
                yield break;
            }
            if (adding != null)
                while (adding.MoveNext()) yield return adding.Current;
            Atom cua = SuperController.singleton.GetAtomByUid(uid);
            if (cua == null || cua.mainController == null)
            {
                SuperController.LogError(
                    "CharacterRouter v272 2B SCENE HAIR | stage=CREATE_MISSING" +
                    " | cua=" + uid);
                yield break;
            }
            _managed[person.uid] = cua;

            try
            {
                JSONClass control = JSON.Parse(authorControl.ToString()).AsObject;
                control["linkTo"] = person.uid + ":head";
                control["position"] = JsonVector(worldPos);
                control["rotation"] = JsonVector(worldRot.eulerAngles);
                JSONStorable liveControl = cua.GetStorableByID("control");
                if (liveControl == null)
                    throw new Exception("CUA control missing");
                liveControl.RestoreFromJSON(control, true, true, null, false);
                cua.mainController.transform.position = worldPos;
                cua.mainController.transform.rotation = worldRot;

                JSONStorable savedScale = cua.GetStorableByID("scale");
                JSONStorableFloat scale = savedScale == null ? null :
                    savedScale.GetFloatJSONParam("scale");
                if (scale != null)
                    scale.val = Number(FindStorable(sourceHair, "scale"),
                        "scale");

                JSONStorable loader = cua.GetStorableByID("asset");
                JSONStorableUrl assetUrl = loader == null ? null :
                    loader.GetUrlJSONParam("assetUrl");
                if (assetUrl == null)
                    throw new Exception("CUA asset URL missing");
                assetUrl.val = url;
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v272 2B SCENE HAIR | stage=SETUP_ERROR" +
                    " | detail=" + e.Message);
                ClearManaged(person);
                yield break;
            }

            JSONStorable assetLoader = cua.GetStorableByID("asset");
            JSONStorableStringChooser choice = assetLoader == null ? null :
                assetLoader.GetStringChooserJSONParam("assetName");
            float deadline = Time.realtimeSinceStartup + 20f;
            while (cua != null && choice != null &&
                   (choice.choices == null ||
                    !choice.choices.Contains(assetName)) &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;
            if (cua == null || choice == null || choice.choices == null ||
                !choice.choices.Contains(assetName))
            {
                SuperController.LogError(
                    "CharacterRouter v272 2B SCENE HAIR | stage=ASSET_NOT_READY" +
                    " | bundle=" + url + " | asset=" + assetName);
                ClearManaged(person);
                yield break;
            }
            choice.val = assetName;
            float renderDeadline = Time.realtimeSinceStartup + 10f;
            while (cua != null &&
                   cua.GetComponentsInChildren<Renderer>(true).Length == 0 &&
                   Time.realtimeSinceStartup < renderDeadline)
                yield return null;
            if (cua == null || cua.mainController == null)
                yield break;
            cua.mainController.transform.position =
                liveHead.transform.TransformPoint(localPos);
            cua.mainController.transform.rotation =
                liveHead.transform.rotation * localRot;
            yield return null;
            Vector3 headLocalPos = liveHead.transform.InverseTransformPoint(
                cua.mainController.transform.position);
            Quaternion headLocalRot =
                Quaternion.Inverse(liveHead.transform.rotation) *
                cua.mainController.transform.rotation;
            SuperController.LogMessage(
                (sc6 ? "CharacterRouter v272 2B-SC6 HAIR | stage=READY" :
                    "CharacterRouter v263 2B-KAINE HAIR | stage=READY") +
                " | person=" + person.uid +
                " | cua=" + uid +
                " | asset=" + assetName +
                " | renderers=" +
                    cua.GetComponentsInChildren<Renderer>(true).Length +
                " | linkTo=" + person.uid + ":head" +
                " | sourceHeadLocal=" + localPos.ToString("F5") +
                " | position=" +
                    cua.mainController.transform.position.ToString("F5") +
                " | headLocalPos=" + headLocalPos.ToString("F5") +
                " | headLocalRot=" +
                    headLocalRot.eulerAngles.ToString("F5"));
        }
    }
}
