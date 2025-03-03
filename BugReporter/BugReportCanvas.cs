using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using JALib.Core;
using JALib.Core.Patch;
using JALib.Tools;
using SFB;
using TMPro;
using UnityEngine;
using UnityModManagerNet;
using Object = UnityEngine.Object;

namespace BugReporter;

public class BugReportCanvas {
    public static ErrorCanvas errorCanvas;
    public static ReportableBug currentBug;
    public static BugFixer[] bugFixers = new BugFixer[7];
    public static int fixPage = -1;
    public static TextMeshProUGUI SupportPageTitle;
    private static string path;

    public static void Setup() {
        GameObject obj = Object.Instantiate(RDConstants.data.prefab_errorCanvas);
        errorCanvas = obj.GetComponent<ErrorCanvas>();
        errorCanvas.SetValue("initiated", true);
        errorCanvas.txtTitle.text = Main.Instance.Localization["ErrorTitle"];
        errorCanvas.txtSupportPages.text = Main.Instance.Localization["FixError"];
        errorCanvas.btnSupport.onClick.AddListener(FixError);
        errorCanvas.btnLog.GetComponentInChildren<TextMeshProUGUI>().text = Main.Instance.Localization["SaveBugReport"];
        errorCanvas.btnLog.onClick.AddListener(SaveBugReport);
        errorCanvas.btnSubmit.gameObject.SetActive(false);
        errorCanvas.btnBack.onClick.AddListener(Back);
        errorCanvas.btnIgnore.onClick.AddListener(Ignore);
        SupportPageTitle = errorCanvas.supportPagesPanel.transform.GetChild(0).GetComponent<TextMeshProUGUI>();
        SetupFixText(errorCanvas.txtFaq, 0);
        SetupFixText(errorCanvas.txtDiscord, 1);
        SetupFixText(errorCanvas.txtSteam, 2);
        Transform baseTransform = errorCanvas.supportPagesPanel.transform;
        for(int i = 3; i < bugFixers.Length; i++) SetupFixText(Object.Instantiate(errorCanvas.txtFaq, baseTransform), i);
        errorCanvas.txtErrorMessage.fontSizeMax = 8;
        RDString.LoadLevelEditorFonts();
        obj.SetActive(false);
        Object.DontDestroyOnLoad(obj);
    }

    private static void SetupFixText(TextMeshProUGUI text, int index) {
        text.gameObject.SetActive(false);
        text.fontSize = 12;
        Object.Destroy(text.transform.GetChild(0).gameObject);
        Object.Destroy(text.GetComponent<LinkOpener>());
        bugFixers[index] = text.GetOrAddComponent<BugFixer>();
        text.rectTransform.anchoredPosition = new Vector2(text.rectTransform.anchoredPosition.x, 110 - 30 * index);
    }

    public static void ShowError() {
        if(scrController.instance && !scrController.instance.paused && scrConductor.instance && scrConductor.instance.isGameWorld) {
            Main.Instance.Log("Game is work! not showing error");
            return;
        }
        if(errorCanvas.gameObject.activeSelf) {
            Main.Instance.Log("Error canvas is already active");
            return;
        }
        foreach(ReportableBug reportableBug in Main.Instance.reportableBugs) {
            if(!reportableBug.IsReady) continue;
            currentBug = reportableBug;
            errorCanvas.ShowError(reportableBug.GetCompactError());
            errorCanvas.gameObject.SetActive(true);
            Main.Instance.reportableBugs.Remove(currentBug);
            break;
        }
    }

    public static void SetPage(int page) {
        if(page == fixPage) return;
        SupportPageTitle.text = string.Format(Main.Instance.Localization["SupportPageTitle"], page + 1);
        fixPage = page;
        foreach(BugFixer fixer in bugFixers) fixer.gameObject.SetActive(false);
        int used = 0;
        if(page > 0) bugFixers[used++].SetFixer(page - 1);
        int next = page == 0 ? 0 : page * 5 + 1;
        bool needNext = false;
        if(next < currentBug.Affected.Count) {
            foreach(object o in currentBug.Affected) {
                if(o == null) continue;
                if(used == 6) {
                    needNext = true;
                    break;
                }
                if(next-- > 0) continue;
                bugFixers[used++].SetFixer(o);
            }
        } else next -= currentBug.Affected.Count;
        foreach((MethodBase, PatchData) tuple in currentBug.PatchDatas) {
            if(used == 6) {
                needNext = true;
                break;
            }
            foreach(MethodBase prefix in tuple.Item2.Prefixes.Concat(tuple.Item2.Postfixes).Concat(tuple.Item2.Transpilers)
                        .Concat(tuple.Item2.Finalizers).Concat(tuple.Item2.Replaces).Concat(tuple.Item2.Removes)) {
                if(used == 6) {
                    needNext = true;
                    break;
                }
                if(next-- > 0) continue;
                bugFixers[used++].SetFixer((tuple.Item1, prefix));
            }
        }
        if(needNext) bugFixers[6].SetFixer(page + 1);
    }

    private static void FixError() {
        if(fixPage == -1) SetPage(0);
        errorCanvas.supportPagesPanel.SetActive(true);
        errorCanvas.mainPanel.SetActive(false);
    }

    private static void SaveBugReport() {
        if(MainThread.IsMainThread()) {
            Task.Run(SaveBugReport);
            return;
        }
        StandaloneFileBrowser.SaveFilePanelAsync(Main.Instance.Localization["SaveBugReportTitle"], null,
            "BugReport-" + DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss") + ".log", [
                new ExtensionFilter(Main.Instance.Localization["LogFile"], "log"),
                new ExtensionFilter(Main.Instance.Localization["TextFile"], "txt")
            ], RunSaveBugReporter);
    }

    private static void RunSaveBugReporter(string path) {
        if(path == null) return;
        BugReportCanvas.path = path;
        Task.Run(RunSave);
    }

    private static async void RunSave() {
        try {
            await using FileStream fileStream = new(path, FileMode.Create);
            await using StreamWriter streamWriter = new(fileStream);
            Task task = streamWriter.WriteAsync(currentBug.GetError() + "\n\n");
            StringBuilder sb = new("=====Affected Mods/문제일 가능성이 높은 모드=====\n");
            foreach(object o in currentBug.Affected) {
                if(o is JAMod mod) sb.Append("[JAMod Name: ").Append(mod.Name).Append(", Version: ").Append(mod.Version).Append(", Assembly: ").Append(mod.ModEntry.Assembly.FullName).Append("]\n");
                else if(o is UnityModManager.ModEntry modEntry)
                    sb.Append("[Mod Name: ").Append(modEntry.Info.Id).Append(", Version: ").Append(modEntry.Info.Version).Append(", DisplayName: ")
                        .Append(modEntry.Info.DisplayName).Append(", Assembly: ").Append(modEntry.Assembly.FullName).Append("]\n");
                else sb.Append("[Unknown: ").Append(o).Append("]\n");
            }
            sb.Append('\n');
            await task;
            task = streamWriter.WriteAsync(sb.ToString());
            sb.Clear();
            sb.Append("=====Affected Candidates/문제일 가능성이 있는 후보 모드=====\n");
            foreach(object o in currentBug.AffectedCandidate) {
                if(o is JAMod mod) sb.Append("[JAMod Name: ").Append(mod.Name).Append(", Version: ").Append(mod.Version).Append(", Assembly: ").Append(mod.ModEntry.Assembly.FullName).Append("]\n");
                else if(o is UnityModManager.ModEntry modEntry)
                    sb.Append("[Mod Name: ").Append(modEntry.Info.Id).Append(", Version: ").Append(modEntry.Version).Append('(').Append(modEntry.Info.Version)
                        .Append("), DisplayName: ").Append(modEntry.Info.DisplayName).Append(", Assembly: ").Append(modEntry.Assembly.FullName).Append("]\n");
                else if(o is Assembly assembly) sb.Append('[').Append(assembly.FullName).Append("]\n");
                else sb.Append("[Unknown: ").Append(o).Append("]\n");
            }
            sb.Append('\n');
            await task;
            task = streamWriter.WriteAsync(sb.ToString());
            sb.Clear();
            if(currentBug.failLoadTrace.Count != 0) {
                sb.Append("=====Not Found Methods/분석할 수 없는 메서드=====\n");
                foreach(string s in currentBug.failLoadTrace) sb.Append(s).Append("\n\n");
                await task;
                task = streamWriter.WriteAsync(sb.ToString());
                sb.Clear();
            }
            if(currentBug.PatchedMethods.Count != 0) {
                sb.Append("=====Patched Methods/패치된 메서드=====\n");
                foreach(MethodBase methodBase in currentBug.PatchedMethods) {
                    sb.Append(methodBase.FullDescription()).Append('\n');
                }
                sb.Append('\n');
                await task;
                task = streamWriter.WriteAsync(sb.ToString());
                sb.Clear();
            }
            if(currentBug.PatchDatas.Count != 0) {
                sb.Append("=====Patch Data/패치 데이터=====\n");
                foreach((MethodBase, PatchData) tuple in currentBug.PatchDatas) {
                    sb.Append("[").Append(tuple.Item1.FullDescription()).Append("]\n");
                    if(tuple.Item2.Prefixes.Length != 0) {
                        sb.Append("--Prefixes--\n");
                        foreach(MethodBase methodBase in tuple.Item2.Prefixes) sb.Append(methodBase.FullDescription()).Append('\n');
                        sb.Append('\n');
                    }
                    if(tuple.Item2.TryPrefixes.Length != 0) {
                        sb.Append("--TryPrefixes--\n");
                        foreach(MethodBase methodBase in tuple.Item2.TryPrefixes) sb.Append(methodBase.FullDescription()).Append('\n');
                        sb.Append('\n');
                    }
                    if(tuple.Item2.Postfixes.Length != 0) {
                        sb.Append("--Postfixes--\n");
                        foreach(MethodBase methodBase in tuple.Item2.Postfixes) sb.Append(methodBase.FullDescription()).Append('\n');
                        sb.Append('\n');
                    }
                    if(tuple.Item2.TryPostfixes.Length != 0) {
                        sb.Append("--TryPostfixes--\n");
                        foreach(MethodBase methodBase in tuple.Item2.TryPostfixes) sb.Append(methodBase.FullDescription()).Append('\n');
                        sb.Append('\n');
                    }
                    if(tuple.Item2.Transpilers.Length != 0) {
                        sb.Append("--Transpilers--\n");
                        foreach(MethodBase methodBase in tuple.Item2.Transpilers) sb.Append(methodBase.FullDescription()).Append('\n');
                        sb.Append('\n');
                    }
                    if(tuple.Item2.Finalizers.Length != 0) {
                        sb.Append("--Finalizers--\n");
                        foreach(MethodBase methodBase in tuple.Item2.Finalizers) sb.Append(methodBase.FullDescription()).Append('\n');
                        sb.Append('\n');
                    }
                    if(tuple.Item2.Replaces.Length != 0) {
                        sb.Append("--Replaces--\n");
                        foreach(MethodBase methodBase in tuple.Item2.Replaces) sb.Append(methodBase.FullDescription()).Append('\n');
                        sb.Append('\n');
                    }
                    if(tuple.Item2.Removes.Length != 0) {
                        sb.Append("--Removes--\n");
                        foreach(MethodBase methodBase in tuple.Item2.Removes) sb.Append(methodBase.FullDescription()).Append('\n');
                        sb.Append('\n');
                    }
                }
                sb.Append('\n');
                await task;
                task = streamWriter.WriteAsync(sb.ToString());
                sb.Clear();
            }
            sb.Append("=====All Mods/모든 모드=====\n");
            foreach(UnityModManager.ModEntry modEntry in UnityModManager.modEntries)
                sb.Append("[Mod Name: ").Append(modEntry.Info.Id).Append(", Version: ").Append(modEntry.Version).Append('(')
                    .Append(modEntry.Info.Version).Append("), DisplayName: ").Append(modEntry.Info.DisplayName).Append(", Assembly: ").Append(modEntry.Assembly?.GetName().Name ?? "Not Loaded").Append("]\n");
            sb.Append('\n');
            await task;
            task = streamWriter.WriteAsync(sb.ToString());
            sb.Clear();
            sb.Append("=====All Assembly/모든 어셈블리=====\n");
            foreach(Assembly assembly in SimpleReflect.GetAssemblies()) sb.Append('[').Append(assembly.FullName).Append("]\n");
            sb.Length -= 1;
            await task;
            task = streamWriter.WriteAsync(sb.ToString());
            sb.Clear();
            await task;
        } catch (Exception e) {
            string key = "Error while saving bug report";
            Main.Instance.LogException(key, e);
            Main.Instance.ReportException(key, e);
        }
    }

    private static void Back() {
        errorCanvas.supportPagesPanel.SetActive(false);
        errorCanvas.mainPanel.SetActive(true);
    }

    private static void Ignore() {
        currentBug = null;
        Back();
        if(fixPage != -1) {
            fixPage = -1;
            foreach(BugFixer fixer in bugFixers) fixer.gameObject.SetActive(false);
        }
        errorCanvas.gameObject.SetActive(false);
        ShowError();
    }
}