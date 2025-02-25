using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using JALib.Core;
using JALib.Core.Patch;
using JALib.Tools;
using UnityModManagerNet;

namespace BugReporter;

public abstract class ReportableBug {
    public bool IsReady;
    public bool hasPatchMethod;
    public HashSet<object> Affected = [];
    public HashSet<object> AffectedCandidate = [];
    public List<MethodBase> StackTraces = [];
    public HashSet<MethodBase> PatchedMethods = [];
    public List<string> failLoadTrace = [];
    public HashSet<(MethodBase, PatchData)> PatchDatas = [];

    public ReportableBug() {
        JATask.Run(Main.Instance, Ready0);
    }

    private void Ready0() {
        try {
            Ready();
            IsReady = true;
            MainThread.Run(Main.Instance, BugReportCanvas.ShowError);
        } catch (Exception) {
            Main.Instance.reportableBugs.Remove(this);
            throw;
        }
    }

    protected List<MethodBase> FindMethod(string funcFullName) {
        string[] func = funcFullName.Split(".");
        string typeName;
        string funcName;
        if(func.Length > 1 && string.IsNullOrEmpty(func[^2])) {
            typeName = string.Join(".", func[..^2]);
            funcName = '.' + func[^1];
        } else {
            typeName = string.Join(".", func[..^1]);
            funcName = func[^1];
        }
        Type type = SimpleReflect.GetType(typeName);
        if(type == null) {
            Main.Instance.Warning("Type not found: " + typeName);
            return null;
        }
        List<MethodBase> methods;
        if(funcName == ".ctor") {
            ConstructorInfo[] ctors = type.Constructors();
            methods = new List<MethodBase>(ctors.Length);
            methods.AddRange(ctors);
        } else if(funcName == ".cctor") methods = [type.TypeInitializer];
        else {
            methods = [];
            foreach(MethodInfo method1 in type.Methods())
                if(method1.Name == funcName)
                    methods.Add(method1);
        }
        if(methods.Count == 0) {
            Main.Instance.Warning("Method not found: " + typeName + "." + funcName);
            return null;
        }
        return methods;
    }

    protected void TryAddMod(MethodBase method, bool aff, ref bool affect) {
        if(method is DynamicMethod) return;
        JAMod mod = method.DeclaringType.GetJAMod();
        if(mod != null) {
            if(aff) {
                Affected.Add(mod);
                affect = false;
            } else AffectedCandidate.Add(mod);
        } else {
            UnityModManager.ModEntry modEntry = method.DeclaringType.GetMod();
            if(modEntry != null) {
                if(aff) {
                    Affected.Add(modEntry);
                    affect = false;
                } else AffectedCandidate.Add(modEntry);
            } else AffectedCandidate.Add(method.DeclaringType.Assembly);
        }
    }

    protected void SetupModInStackTrace() {
        bool affect = true;
        HashSet<Assembly> addedAssemblies = [];
        foreach(MethodBase method in StackTraces) {
            bool aff = affect;
            if(!addedAssemblies.Contains(method.DeclaringType.Assembly)) {
                TryAddMod(method, affect, ref affect);
                addedAssemblies.Add(method.DeclaringType.Assembly);
            }
            if(!PatchedMethods.Contains(method)) continue;
            PatchData patchData = JAPatcher.GetPatchData(method);
            PatchDatas.Add((method, patchData));
            foreach(MethodBase methodBase in patchData.Prefixes.Concat(patchData.Postfixes).Concat(patchData.Transpilers)
                        .Concat(patchData.Finalizers).Concat(patchData.Replaces).Concat(patchData.Removes)) {
                if(addedAssemblies.Contains(methodBase.DeclaringType.Assembly)) continue;
                TryAddMod(methodBase, aff, ref affect);
                addedAssemblies.Add(methodBase.DeclaringType.Assembly);
            }
        }
    }

    public abstract void Ready();
    public abstract string GetErrorMessage();
    public abstract string GetStackTrace();
    public abstract string GetError();

    public string GetCompactError() {
        StringBuilder sb = new();
        JALocalization localization = Main.Instance.Localization;
        sb.Append("<size=8>").Append(GetErrorMessage()).Append("<size=4>\n\n</size><size=12>").Append(localization["AffectedMods"]).Append("</size>\n");
        foreach(object o in Affected) {
            if(o is JAMod mod) sb.Append('[').Append(mod.Name).Append(' ').Append(mod.Version).Append("(JAMod)]\n");
            else if(o is UnityModManager.ModEntry modEntry) sb.Append('[').Append(modEntry.Info.Id).Append(' ').Append(modEntry.Info.Version).Append("]\n");
            else throw new NotSupportedException(o.GetType().Name + " is not Support");
        }
        sb.Append("<size=4>\n</size><size=12>").Append(localization["AffectedCandidateMods"]).Append("</size>\n");
        foreach(object o in AffectedCandidate) {
            if(o is JAMod mod) sb.Append('[').Append(mod.Name).Append(' ').Append(mod.Version).Append("(JAMod)]\n");
            else if(o is UnityModManager.ModEntry modEntry) sb.Append('[').Append(modEntry.Info.Id).Append(' ').Append(modEntry.Info.Version).Append("]\n");
            else if(o is Assembly assembly) sb.Append('[').Append(assembly.FullName).Append("]\n");
            else throw new NotSupportedException(o.GetType().Name + " is not Support");
        }
        sb.Length -= 1;
        sb.Append("</size>");
        if(failLoadTrace.Count != 0) {
            sb.Append("\n<size=4>\n</size><size=12>").Append(localization["NotFoundMethods"]).Append("</size>\n");
            foreach(string s in failLoadTrace) sb.Append(s).Append('\n');
        }
        return sb.ToString();
    }
}