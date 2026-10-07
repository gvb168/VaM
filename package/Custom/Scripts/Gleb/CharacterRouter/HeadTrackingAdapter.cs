using UnityEngine;
using System;
using System.Collections.Generic;
using SimpleJSON;

namespace GlebVAM
{
    // v237: post-load policy for author plugins that track the camera with the
    // head. Changes only the current Person's model plugins; VaM Eye Control,
    // CheesyFX WatchMe and the rest of BodyLanguage are left alone.
    public class HeadTrackingAdapter
    {
        // PoseMe's Clients toggles are private JSONStorableBool instances. The
        // plugin loads them only during its one-time LateRestoreFromJSON. Change
        // the imported state for this Person without changing the source VAR,
        // other Gaze clients, or the VaM Eye Control settings.
        public static bool NormalizePoseMeRestoreState(
            JSONClass state, string personUid)
        {
            if (state == null || string.IsNullOrEmpty(personUid))
                return false;

            JSONClass gaze = state["gaze"].AsObject;
            if (gaze == null)
            {
                gaze = new JSONClass();
                state["gaze"] = gaze;
            }

            string key = personUid + " Enabled";
            gaze[key] = "false";

            // PoseMe checks for this key before it calls Gaze.LoadGlobals.
            if (!state.HasKey("poses"))
                state["poses"] = new JSONArray();

            SuperController.LogMessage(
                "CharacterRouter v237 HEAD TRACKING | stage=CHEESY_PRESET_OFF" +
                " | person=" + personUid + " | client=" + key);
            return true;
        }

        public void DisableCameraHeadTracking(Atom person)
        {
            if (person == null) return;

            List<string> ids = person.GetStorableIDs();
            if (ids == null) return;

            MVRPluginManager manager = person.GetStorableByID("PluginManager")
                as MVRPluginManager;
            JSONClass plugins = null;
            try
            {
                JSONClass state = manager == null ? null :
                    manager.GetJSON(true, true, true);
                plugins = state == null ? null : state["plugins"].AsObject;
            }
            catch (Exception e)
            {
                SuperController.LogError(
                    "CharacterRouter v237 HEAD TRACKING | stage=MANAGER_ERROR" +
                    " | person=" + person.uid + " | detail=" + e.Message
                );
            }

            int macCount = 0;
            int cheesyCount = 0;
            int cheesyDisabled = 0;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (string.IsNullOrEmpty(id) ||
                    !id.StartsWith("plugin#", StringComparison.OrdinalIgnoreCase))
                    continue;

                int divider = id.IndexOf('_');
                if (divider <= 0) continue;
                string slot = id.Substring(0, divider);
                string path = plugins == null ? "" : plugins[slot].Value;
                JSONStorable storable = person.GetStorableByID(id);
                if (storable == null) continue;

                // MacGruber.Gaze is a storable inside MacGruber_Life.cslist;
                // the manager's package path does not contain "Gaze".
                if (id.EndsWith("_MacGruber.Gaze", StringComparison.OrdinalIgnoreCase))
                {
                    macCount++;
                    try
                    {
                        // VaM's plugin Enabled checkbox is the storable's own
                        // enabled parameter. Use its callback when available.
                        JSONStorableBool enabled =
                            storable.GetBoolJSONParam("enabled");
                        if (enabled == null)
                            enabled = storable.GetBoolJSONParam("Enabled");
                        bool wasEnabled = enabled == null
                            ? storable.enabled
                            : enabled.val;
                        if (wasEnabled)
                        {
                            if (enabled != null)
                                enabled.val = false;
                            else
                                storable.enabled = false;
                        }

                        SuperController.LogMessage(
                            "CharacterRouter v237 HEAD TRACKING | stage=" +
                            (wasEnabled ? "MACGRUBER_OFF" : "MACGRUBER_ALREADY_OFF") +
                            " | person=" + person.uid + " | id=" + id +
                            " | host=" + path +
                            " | param=" + (enabled != null) +
                            " | final=" +
                            (enabled == null ? storable.enabled : enabled.val)
                        );
                    }
                    catch (Exception e)
                    {
                        SuperController.LogError(
                            "CharacterRouter v237 HEAD TRACKING | stage=MACGRUBER_ERROR" +
                            " | person=" + person.uid + " | detail=" + e.Message
                        );
                    }
                }

                if (id.EndsWith("_CheesyFX.PoseMe", StringComparison.OrdinalIgnoreCase) &&
                    path.IndexOf("CheesyFX.BodyLanguage", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    cheesyCount++;
                    try
                    {
                        // Match the exact Gaze > Clients control shown in
                        // BodyLanguage's UI ("Person#N Enabled"). Avoid the
                        // generic plugin Enabled flag, which disables PoseMe.
                        string name = person.uid + " Enabled";
                        JSONStorableBool client =
                            storable.GetBoolJSONParam(name);
                        JSONClass state = null;
                        if (client == null)
                        {
                            state = storable.GetJSON(true, true, true);
                            if (state != null)
                            {
                                foreach (string key in state.Keys)
                                {
                                    // A dynamically registered client may use a
                                    // different label in its serialized state.
                                    if (key.IndexOf(person.uid, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                        key.IndexOf("enabled", StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        client = storable.GetBoolJSONParam(key);
                                        if (client != null)
                                        {
                                            name = key;
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                        if (client != null)
                        {
                            client.val = false;
                            cheesyDisabled++;
                            SuperController.LogMessage(
                                "CharacterRouter v237 HEAD TRACKING | stage=CHEESY_CLIENT_OFF" +
                                " | person=" + person.uid + " | id=" + id +
                                " | param=" + name
                            );
                        }
                        else
                        {
                            if (state == null)
                                state = storable.GetJSON(true, true, true);
                            JSONClass gaze = state == null ? null :
                                state["gaze"].AsObject;
                            if (gaze != null &&
                                gaze.HasKey(name) &&
                                string.Equals(gaze[name].Value, "false",
                                    StringComparison.OrdinalIgnoreCase))
                            {
                                cheesyDisabled++;
                                SuperController.LogMessage(
                                    "CharacterRouter v237 HEAD TRACKING | stage=CHEESY_CLIENT_OFF" +
                                    " | person=" + person.uid + " | id=" + id +
                                    " | source=nested_state");
                                continue;
                            }
                            // Some releases keep the custom UI control private.
                            // Log a compact schema so its exact API can be added
                            // without changing poses or disabling eye tracking.
                            if (state == null)
                                state = storable.GetJSON(true, true, true);
                            List<string> keys = new List<string>();
                            if (state != null)
                            {
                                foreach (string key in state.Keys)
                                {
                                    if (keys.Count == 30) break;
                                    keys.Add(key);
                                }
                            }
                            SuperController.LogMessage(
                                "CharacterRouter v237 HEAD TRACKING | stage=CHEESY_CLIENT_UNEXPOSED" +
                                " | person=" + person.uid + " | id=" + id +
                                " | keys=" + string.Join(",", keys.ToArray())
                            );
                            // Report public state keys beyond the first 30;
                            // never alter the private BodyLanguage client.
                            List<string> relevant = new List<string>();
                            int count = 0;
                            if (state != null)
                            {
                                foreach (string key in state.Keys)
                                {
                                    count++;
                                    if (key.IndexOf("gaze", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        key.IndexOf("client", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        key.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        key.IndexOf(person.uid, StringComparison.OrdinalIgnoreCase) >= 0)
                                    {
                                        if (relevant.Count < 24)
                                        {
                                            string value = state[key] == null
                                                ? "null"
                                                : state[key].ToString();
                                            if (value.Length > 240)
                                                value = value.Substring(0, 240) + "...";
                                            relevant.Add(key + "=" + value);
                                        }
                                    }
                                }
                            }
                            SuperController.LogMessage(
                                "CharacterRouter v237 HEAD TRACKING | stage=CHEESY_SCHEMA" +
                                " | person=" + person.uid +
                                " | totalKeys=" + count +
                                " | relevant=" + string.Join(";", relevant.ToArray())
                            );
                        }
                    }
                    catch (Exception e)
                    {
                        SuperController.LogError(
                            "CharacterRouter v237 HEAD TRACKING | stage=CHEESY_ERROR" +
                            " | person=" + person.uid + " | detail=" + e.Message
                        );
                    }
                }
            }

            SuperController.LogMessage(
                "CharacterRouter v237 HEAD TRACKING | stage=DONE" +
                " | person=" + person.uid + " | mac=" + macCount +
                " | cheesy=" + cheesyCount +
                " | cheesyClientOff=" + cheesyDisabled
            );
        }
    }
}
