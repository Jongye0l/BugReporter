using System;
using System.Reflection;
using JALib.Core;
using JALib.Core.Patch;
using JALib.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityModManagerNet;

namespace BugReporter;

public class BugFixer : MonoBehaviour, IPointerClickHandler {
    public TextMeshProUGUI text;
    public object fixer;

    public void SetFixer(object fixer) {
        if(fixer == null) throw new NullReferenceException();
        text ??= GetComponent<TextMeshProUGUI>();
        if(fixer is JAMod mod) text.text = string.Format(Main.Instance.Localization["OffMod"], mod.Name);
        else if(fixer is UnityModManager.ModEntry modEntry) text.text = string.Format(Main.Instance.Localization["OffMod"], modEntry.Info.Id);
        else if(fixer is (MethodBase method, MethodBase patch)) text.text = string.Format(Main.Instance.Localization["UnpatchMethod"], patch.DeclaringType.GetMod().Info.Id, method.Name, patch.Name);
        else if(fixer is int index) text.text = string.Format(Main.Instance.Localization[BugReportCanvas.fixPage < index ? "NextPage" : "PrevPage"]);
        else {
            Main.Instance.Error("Unknown fixer type: " + fixer.GetType());
            return;
        }
        this.fixer = fixer;
        gameObject.SetActive(true);
    }

    public void OnPointerClick(PointerEventData eventData) {
        switch(fixer) {
            case null:
                Main.Instance.Error("BugFixer is not set");
                break;
            case JAMod mod:
                mod.Disable();
                UnityModManager.SaveSettingsAndParams();
                text.text = string.Format(Main.Instance.Localization["ModDisabled"], mod.Name);
                break;
            case UnityModManager.ModEntry modEntry:
                modEntry.Enabled = false;
                modEntry.Active = false;
                UnityModManager.SaveSettingsAndParams();
                text.text = string.Format(Main.Instance.Localization["ModDisabled"], modEntry.Info.Id);
                break;
            case (MethodBase method, MethodBase patch):
                JAPatcher.Unpatch(method, (MethodInfo) patch);
                text.text = string.Format(Main.Instance.Localization["MethodUnpatched"], patch.DeclaringType.GetMod().Info.Id, method.Name, patch.Name);
                break;
            case int index:
                BugReportCanvas.SetPage(index);
                break;
            default:
                Main.Instance.Error("Unknown fixer type: " + fixer.GetType());
                break;
        }
    }
}