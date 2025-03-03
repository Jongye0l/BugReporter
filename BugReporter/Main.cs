using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using JALib.Core;
using JALib.Core.Patch;
using JALib.Tools;
using UnityEngine;
using UnityModManagerNet;

namespace BugReporter;

public class Main : JAMod {
    public static Main Instance;
    public List<ReportableBug> reportableBugs = [];
    private List<Hash> alreadyReportedBugs = [];

    protected override void OnSetup() {
        Patcher.AddPatch(typeof(Main));
    }

    protected override void OnEnable() {
        BugReportCanvas.Setup();
        Application.logMessageReceived += OnLogReceived;
    }

    protected override void OnDisable() {
        Application.logMessageReceived -= OnLogReceived;
    }

    private void OnLogReceived(string condition, string stackTrace, LogType type) {
        if(type != LogType.Exception) return;
        string data = condition + stackTrace;
        using MD5 md5 = MD5.Create();
        Hash hash = new(md5.ComputeHash(Encoding.UTF8.GetBytes(data)));
        lock(alreadyReportedBugs) {
            if(alreadyReportedBugs.Contains(hash)) return;
            alreadyReportedBugs.Add(hash);
        }
        reportableBugs.Add(new UnityLogBug(condition, stackTrace));
    }

    [JAPatch(typeof(JAMod), nameof(ReportException), PatchType.Replace, false, ArgumentTypesType = [typeof(string), typeof(Exception), typeof(JAMod[])])]
    private static new void ReportException(string key, Exception e, JAMod[] mod) {
        string data = e.Message + e.StackTrace;
        using MD5 md5 = MD5.Create();
        Hash hash = new(md5.ComputeHash(Encoding.UTF8.GetBytes(data)));
        lock(Instance.alreadyReportedBugs) {
            if(Instance.alreadyReportedBugs.Contains(hash)) return;
            Instance.alreadyReportedBugs.Add(hash);
        }
        ExceptionBug bug = new(key, e);
        foreach(JAMod jaMod in mod)
            if(jaMod != null)
                bug.Affected.Add(jaMod);
        Instance.reportableBugs.Add(bug);
    }

    [JAPatch(typeof(scrUIController), nameof(scrUIController.WipeToBlack), PatchType.Postfix, false)]
    [JAPatch(typeof(scnEditor), "ResetScene", PatchType.Postfix, false)]
    [JAPatch(typeof(scrController), nameof(scrController.StartLoadingScene), PatchType.Postfix, false)]
    private static void GameEnd() => MainThread.Run(Instance, BugReportCanvas.ShowError);

    [JAPatch(typeof(UnityModManager), nameof(UnityModManager.SaveSettingsAndParams), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.Param), nameof(UnityModManager.Param.Save), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.Param), nameof(UnityModManager.Param.Load), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.InstallerParam), nameof(UnityModManager.InstallerParam.Load), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.ModEntry), "Active.set", PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.ModEntry), nameof(UnityModManager.ModEntry.Load), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.ModEntry), nameof(UnityModManager.ModEntry.Invoke), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.ModSettings), nameof(UnityModManager.ModSettings.Save), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.ModSettings), nameof(UnityModManager.ModSettings.Load), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.UI), nameof(UnityModManager.ModSettings.Load), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.UI), "Update", PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.UI), "FixedUpdate", PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.UI), "LateUpdate", PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.UI), "OnGUI", PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.UI), "DrawTab", PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.UI), "ToggleWindow", PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.UI), nameof(UnityModManager.UI.DrawFields), PatchType.Transpiler, false)]
    [JAPatch(typeof(UnityModManager.UI), "ToggleWindow", PatchType.Transpiler, false)]
    private static IEnumerable<CodeInstruction> ReportExceptionTranspiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator) {
        List<CodeInstruction> list = instructions.ToList();
        for(int i = 0; i < list.Count; i++) {
            CodeInstruction code = list[i];
            if(code.operand is MethodInfo { Name: "LogException" } methodInfo) {
                LocalBuilder key = null;
                CodeInstruction exception;
                if(methodInfo.GetParameters().Length == 2) {
                    key = generator.DeclareLocal(typeof(string));
                    list.InsertRange(i++ - 1, [
                        new CodeInstruction(OpCodes.Stloc, key),
                        new CodeInstruction(OpCodes.Ldloc, key)
                    ]);
                    exception = list[i++].Clone();
                } else if(list[i - 1].operand is MethodInfo { Name: "Error" }) {
                    key = generator.DeclareLocal(typeof(string));
                    list.InsertRange(i++ - 1, [
                        new CodeInstruction(OpCodes.Stloc, key),
                        new CodeInstruction(OpCodes.Ldloc, key)
                    ]);
                    LocalBuilder ex = generator.DeclareLocal(typeof(Exception));
                    list.InsertRange(i - 1, [
                        new CodeInstruction(OpCodes.Stloc, ex),
                        new CodeInstruction(OpCodes.Ldloc, ex)
                    ]);
                    i += 3;
                    exception = new CodeInstruction(OpCodes.Ldloc, ex);
                } else if(list[i - 4].operand is MethodInfo { Name: "Error" }) {
                    key = generator.DeclareLocal(typeof(string));
                    list.InsertRange(i++ - 4, [
                        new CodeInstruction(OpCodes.Stloc, key),
                        new CodeInstruction(OpCodes.Ldloc, key)
                    ]);
                    exception = list[i++].Clone();
                } else if(code.blocks.Count != 0) {
                    LocalBuilder ex = generator.DeclareLocal(typeof(Exception));
                    list.InsertRange(i + 1, [
                        new CodeInstruction(OpCodes.Ldloc, ex),
                        list[i].Clone()
                    ]);
                    list[i].opcode = OpCodes.Stloc;
                    list[i].operand = ex;
                    exception = new CodeInstruction(OpCodes.Ldloc, ex);
                    i += 2;
                } else exception = list[i - 1].Clone();
                list.InsertRange(i + 1, [
                    key == null ? new CodeInstruction(OpCodes.Ldnull) : new CodeInstruction(OpCodes.Ldloc, key),
                    exception,
                    new CodeInstruction(OpCodes.Call, ((Delegate) Array.Empty<JAMod>).Method),
                    new CodeInstruction(OpCodes.Call, ((Action<string, Exception, JAMod[]>) ReportException).Method)
                ]);
            }
        }
        return list;
    }

    private readonly struct Hash(byte[] data) : IEquatable<Hash> {
        private readonly byte[] data = data;

        public override bool Equals(object obj) => obj is Hash hash ? Equals(hash) : obj is byte[] bytes && Equals(bytes);

        public bool Equals(Hash other) {
            byte[] hash = other.data;
            if(data.Length != hash.Length) return false;
            for(int i = 0; i < data.Length; i++)
                if(data[i] != hash[i])
                    return false;
            return true;
        }

        public override int GetHashCode() => data != null ? ToString().GetHashCode() : 0;

        public static bool operator ==(Hash left, Hash right) => left.Equals(right);
        public static bool operator !=(Hash left, Hash right) => !(left == right);

        public override string ToString() => data.Join(b => b.ToString("x2"), "");
    }
}