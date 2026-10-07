using System;
using System.Collections;
using UnityEngine;
using SimpleJSON;

// Add this as a plugin to Person#3 in the original 2B showcase single scene.
// It only reads the running scene; CharacterRouter.cslist does not load it.
public class KaineAnchorProbe : MVRScript
{
    public override void Init()
    {
        try { StartCoroutine(Report()); }
        catch (Exception e)
        {
            SuperController.LogError("2B-KAINE ANCHOR PROBE | ERROR | " + e);
        }
    }

    private IEnumerator Report()
    {
        Atom person = null;
        Atom hair = null;
        DAZBone head = null;
        float deadline = Time.realtimeSinceStartup + 20f;
        while (Time.realtimeSinceStartup < deadline)
        {
            person = SuperController.singleton.GetAtomByUid("Person#3");
            hair = SuperController.singleton.GetAtomByUid(
                "CustomUnityAsset_Person#3");
            head = person == null ? null :
                person.GetStorableByID("head") as DAZBone;
            if (hair != null && hair.mainController != null &&
                head != null &&
                hair.GetComponentsInChildren<Renderer>(true).Length > 0)
                break;
            yield return null;
        }
        if (hair == null || hair.mainController == null || head == null)
        {
            SuperController.LogError(
                "2B-KAINE ANCHOR PROBE | SOURCE_NOT_READY" +
                " | person=" + (person == null ? "missing" : "ready") +
                " | hair=" + (hair == null ? "missing" : "ready") +
                " | head=" + (head == null ? "missing" : "ready"));
            yield break;
        }

        yield return new WaitForEndOfFrame();
        Transform h = head.transform;
        Transform c = hair.mainController.transform;
        Vector3 local = h.InverseTransformPoint(c.position);
        Quaternion rotation = Quaternion.Inverse(h.rotation) * c.rotation;
        Renderer[] renderers = hair.GetComponentsInChildren<Renderer>(true);
        Renderer visible = null;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null && renderers[i].enabled)
            {
                visible = renderers[i];
                break;
            }
        SuperController.LogMessage(
            "2B-KAINE ANCHOR PROBE | SOURCE" +
            " | headPos=" + h.position.ToString("F5") +
            " | headRot=" + h.rotation.eulerAngles.ToString("F5") +
            " | hairPos=" + c.position.ToString("F5") +
            " | hairRot=" + c.rotation.eulerAngles.ToString("F5") +
            " | headLocalPos=" + local.ToString("F5") +
            " | headLocalRot=" + rotation.eulerAngles.ToString("F5") +
            " | renderCenter=" +
                (visible == null ? "none" :
                    visible.bounds.center.ToString("F5")));

        string[] controls = {
            "rFootControl", "lFootControl", "rToeControl", "lToeControl"
        };
        for (int i = 0; i < controls.Length; i++)
        {
            JSONStorable storable = person.GetStorableByID(controls[i]);
            JSONClass state = storable == null ? null :
                storable.GetJSON(true, true, true);
            JSONNode euler = state == null ? null :
                state["localRotation"];
            SuperController.LogMessage(
                "2B-KAINE ANCHOR PROBE | FOOT" +
                " | control=" + controls[i] +
                " | localRotation=" +
                    (euler == null ? "missing" :
                        euler["x"].Value + "," + euler["y"].Value +
                        "," + euler["z"].Value) +
                " | jointDriveXTarget=" +
                    (state == null ? "missing" :
                        state["jointDriveXTarget"].Value) +
                " | rotationState=" +
                    (state == null ? "missing" :
                        state["rotationState"].Value));
        }
    }
}
