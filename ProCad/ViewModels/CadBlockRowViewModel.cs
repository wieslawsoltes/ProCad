using ACadSharp.Blocks;
using ACadSharp.Tables;
using Avalonia.Media.Imaging;
using ReactiveUI.SourceGenerators;

namespace ProCad.ViewModels;

public sealed partial class CadBlockRowViewModel : ViewModelBase
{
    public BlockRecord Block { get; }
    public string Name { get; }
    public string Handle { get; }
    public string LayoutName { get; }
    public string EntityCount { get; }
    public bool IsAnonymous { get; }
    public bool IsDynamic { get; }
    public bool IsXRef { get; }
    public bool IsXRefOverlay { get; }
    public bool IsLayout { get; }
    public bool HasAttributes { get; }

    [Reactive]
    public partial Bitmap? Preview { get; set; }

    public CadBlockRowViewModel(BlockRecord block)
    {
        Block = block;
        Name = block.Name;
        Handle = block.Handle == 0 ? string.Empty : block.Handle.ToString("X");
        LayoutName = block.Layout?.Name ?? string.Empty;
        EntityCount = block.Entities?.Count.ToString() ?? "0";
        IsAnonymous = block.IsAnonymous;
        IsDynamic = block.IsDynamic;
        HasAttributes = block.HasAttributes;
        var flags = block.Flags;
        IsXRef = flags.HasFlag(BlockTypeFlags.XRef) || flags.HasFlag(BlockTypeFlags.XRefDependent) || flags.HasFlag(BlockTypeFlags.XRefResolved);
        IsXRefOverlay = flags.HasFlag(BlockTypeFlags.XRefOverlay);
        IsLayout = block.Layout is not null;
    }
}
