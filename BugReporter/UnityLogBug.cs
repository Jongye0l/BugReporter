using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BugReporter;

public class UnityLogBug : ReportableBug {
    public string condition;
    public string stackTrace;

    public UnityLogBug(string condition, string stackTrace) {
        this.condition = condition;
        this.stackTrace = stackTrace;
    }

    public override void Ready() {
        foreach(string s in stackTrace.Split("\n")) {
            if(s.IsNullOrEmpty()) continue;
            string[] parts = s.Split(" ");
            bool dynamicMethod = s.StartsWith("(wrapper dynamic-method)");
            bool patchMethod = false;
            string funcFullName;
            if(dynamicMethod) {
                funcFullName = parts[2];
                funcFullName = funcFullName[..funcFullName.IndexOf('(')].Replace("MonoMod.Utils.DynamicMethodDefinition.", "");
                Match match = Regex.Match(funcFullName, @"(_Patch\d+)$");
                if(match.Success) {
                    hasPatchMethod = patchMethod = true;
                    funcFullName = funcFullName[..^match.Length];
                } else {
                    Main.Instance.Warning("Dynamic method is not supported: " + funcFullName);
                    failLoadTrace.Add(s);
                    continue;
                }
            } else funcFullName = parts[0];
            List<MethodBase> methods = FindMethod(funcFullName);
            if(methods == null) {
                failLoadTrace.Add(s);
                continue;
            }
            MethodBase method;
            if(methods.Count == 1) method = methods[0];
            else {
                string[] types;
                if(dynamicMethod) {
                    string typeStr = parts[2];
                    types = typeStr[(typeStr.IndexOf('(') + 1)..^1].Split(",");
                } else types = parts[1][1..^1].Split(",");
                for(int i = 0; i < methods.Count; i++) {
                    if(methods[i].GetParameters().Length + (patchMethod && !methods[i].IsStatic ? 1 : 0) == types.Length) continue;
                    methods.RemoveAt(i--);
                }
                if(methods.Count > 1) {
                    for(int i = 0; i < types.Length; i++) {
                        string t = types[i];
                        for(int i2 = 0; i2 < methods.Count; i2++) {
                            int i3 = patchMethod && !methods[i2].IsStatic ? i - 1 : i;
                            if((i3 == -1 ? methods[i2].DeclaringType.FullName : methods[i2].GetParameters()[i3].ParameterType.FullName) == t.Split(" ")[0]) continue;
                            methods.RemoveAt(i2--);
                        }
                        if(methods.Count <= 1) break;
                    }
                    if(methods.Count == 1) method = methods[0];
                    else {
                        Main.Instance.Warning("Multiple methods found");
                        failLoadTrace.Add(s);
                        continue;
                    }
                } else if(methods.Count == 1) method = methods[0];
                else {
                    Main.Instance.Warning("Method not found: " + funcFullName);
                    failLoadTrace.Add(s);
                    continue;
                }
            }
            if(patchMethod) PatchedMethods.Add(method);
            StackTraces.Add(method);
        }
        SetupModInStackTrace();
    }

    public override string GetErrorMessage() => condition;
    public override string GetStackTrace() => stackTrace;
    public override string GetError() => condition + "\n" + stackTrace;
}