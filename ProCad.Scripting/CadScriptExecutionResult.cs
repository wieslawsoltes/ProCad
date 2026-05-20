using System;
using System.Collections.Generic;

namespace ProCad.Scripting;

public sealed class CadScriptExecutionResult
{
    private CadScriptExecutionResult(
        bool success,
        string output,
        object? returnValue,
        string? error,
        IReadOnlyList<string> diagnostics,
        TimeSpan duration)
    {
        Success = success;
        Output = output;
        ReturnValue = returnValue;
        Error = error;
        Diagnostics = diagnostics;
        Duration = duration;
    }

    public bool Success { get; }

    public string Output { get; }

    public object? ReturnValue { get; }

    public string? Error { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public TimeSpan Duration { get; }

    public static CadScriptExecutionResult FromDiagnostics(IReadOnlyList<string> diagnostics)
    {
        return new CadScriptExecutionResult(false, string.Empty, null, "Script compilation failed.", diagnostics, TimeSpan.Zero);
    }

    public static CadScriptExecutionResult FromException(Exception exception, string output, IReadOnlyList<string> diagnostics, TimeSpan duration)
    {
        return new CadScriptExecutionResult(false, output, null, exception.ToString(), diagnostics, duration);
    }

    public static CadScriptExecutionResult FromSuccess(object? returnValue, string output, IReadOnlyList<string> diagnostics, TimeSpan duration)
    {
        return new CadScriptExecutionResult(true, output, returnValue, null, diagnostics, duration);
    }
}
