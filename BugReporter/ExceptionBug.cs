using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BugReporter;

public class ExceptionBug : ReportableBug {
    public string key;
    public Exception exception;

    public ExceptionBug(string key, Exception exception) {
        this.key = key;
        this.exception = exception;
    }

    public override void Ready() {
        if(exception is AggregateException { InnerExceptions.Count: 1 }) exception = exception.InnerException;
        SetupStackTrace(exception);
    }

    private void SetupStackTrace(Exception exception) {
        if(exception is AggregateException aggregateException)
            foreach(Exception innerException in aggregateException.InnerExceptions)
                SetupStackTrace(innerException);
        else if(exception.InnerException != null) SetupStackTrace(exception.InnerException);
        StackFrame[] frames = new StackTrace(exception, true).GetFrames();
        string[] stackStrings = exception.StackTrace.Split("\n");
        for(int i = 0; i < Math.Min(frames.Length, stackStrings.Length); i++) {
            StackFrame frame = frames[i];
            MethodBase methodBase = frame.GetMethod();
            if(methodBase == null) {
                string trimedString = stackStrings[i].Trim();
                string funcFullNameWithParams = trimedString.Split(" ")[3];
                string funcFullName;
                bool dynamicMethod = trimedString.StartsWith("at (wrapper dynamic-method)");
                bool patchMethod = false;
                try {
                    funcFullName = funcFullNameWithParams[..funcFullNameWithParams.IndexOf('(')];
                    if(dynamicMethod) funcFullName = funcFullName.Replace("MonoMod.Utils.DynamicMethodDefinition.", "");
                } catch (Exception) {
                    Main.Instance.Error(funcFullNameWithParams);
                    throw;
                }
                Match match = Regex.Match(funcFullName, @"(_Patch\d+)$");
                if(match.Success) {
                    hasPatchMethod = patchMethod = true;
                    funcFullName = funcFullName[..^match.Length];
                } else if(dynamicMethod) {
                    Main.Instance.Warning("Dynamic method is not supported: " + funcFullName);
                    failLoadTrace.Add(stackStrings[i]);
                    continue;
                }
                List<MethodBase> methods = FindMethod(funcFullName);
                if(methods == null) {
                    failLoadTrace.Add(frame.ToString());
                    continue;
                }
                if(methods.Count == 1) methodBase = methods[0];
                else {
                    string[] types = funcFullNameWithParams[(funcFullNameWithParams.IndexOf('(') + 1)..^1].Split(",");
                    for(int i2 = 0; i2 < methods.Count; i2++) {
                        if(methods[i2].GetParameters().Length + (!dynamicMethod || methods[i2].IsStatic ? 0 : 1) == types.Length) continue;
                        methods.RemoveAt(i2--);
                    }
                    if(methods.Count > 1) {
                        for(int i2 = 0; i2 < types.Length; i2++) {
                            string t = types[i2];
                            for(int i3 = 0; i3 < methods.Count; i3++) {
                                int i4 = patchMethod && !methods[i3].IsStatic ? i2 - 1 : i2;
                                if((i4 == -1 ? methods[i3].DeclaringType.FullName : methods[i3].GetParameters()[i4].ParameterType.FullName) == t.Split(" ")[0]) continue;
                                methods.RemoveAt(i3--);
                            }
                            if(methods.Count <= 1) break;
                        }
                        if(methods.Count == 1) methodBase = methods[0];
                        else {
                            Main.Instance.Warning("Multiple methods found");
                            failLoadTrace.Add(frame.ToString());
                            continue;
                        }
                    } else if(methods.Count == 1) methodBase = methods[0];
                    else {
                        Main.Instance.Warning("Method not found: " + funcFullName);
                        failLoadTrace.Add(frame.ToString());
                        continue;
                    }
                }
                PatchedMethods.Add(methodBase);
            }
            StackTraces.Add(methodBase);
        }
        SetupModInStackTrace();
    }

    public override string GetErrorMessage() => (key == null ? "" : key + ": ") + exception.Message;
    public override string GetStackTrace() => exception.StackTrace;
    public override string GetError() => (key == null ? "" : key + ": ") + exception;
}