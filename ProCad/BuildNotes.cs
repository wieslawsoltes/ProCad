namespace ProCad;

internal static class BuildNotes
{
    // NOTE: ACadSharp multi-target builds fail on net48/netstandard2.x because of Encoding.Latin1 usage.
    // The solution-level workaround is in `Directory.Build.props` (restricting ACadSharp TFMs until upstream fixes it).
}
