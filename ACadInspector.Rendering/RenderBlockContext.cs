using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ACadSharp.Tables;

namespace ACadInspector.Rendering;

public sealed class RenderBlockContext
{
    private readonly Stack<BlockRecord> _stack = new();
    private readonly HashSet<BlockRecord> _active;
    private readonly int _maxDepth;

    public int Depth => _stack.Count;

    public RenderBlockContext(int maxDepth = 100)
    {
        _maxDepth = Math.Max(1, maxDepth);
        _active = new HashSet<BlockRecord>(new BlockRecordComparer());
    }

    public bool TryEnter(BlockRecord block, out IDisposable scope)
    {
        if (block is null)
        {
            scope = EmptyScope.Instance;
            return false;
        }

        if (_active.Contains(block) || _stack.Count >= _maxDepth)
        {
            scope = EmptyScope.Instance;
            return false;
        }

        _active.Add(block);
        _stack.Push(block);
        scope = new BlockScope(this, block);
        return true;
    }

    public IDisposable EnterRoot(BlockRecord block)
    {
        if (block is null)
        {
            return EmptyScope.Instance;
        }

        if (_active.Contains(block))
        {
            return EmptyScope.Instance;
        }

        _active.Add(block);
        _stack.Push(block);
        return new BlockScope(this, block);
    }

    private void Exit(BlockRecord block)
    {
        if (_stack.Count > 0 && ReferenceEquals(_stack.Peek(), block))
        {
            _stack.Pop();
        }
        else if (_stack.Count > 0)
        {
            var temp = new Stack<BlockRecord>(_stack.Count);
            while (_stack.Count > 0 && !ReferenceEquals(_stack.Peek(), block))
            {
                temp.Push(_stack.Pop());
            }

            if (_stack.Count > 0)
            {
                _stack.Pop();
            }

            while (temp.Count > 0)
            {
                _stack.Push(temp.Pop());
            }
        }

        _active.Remove(block);
    }

    private sealed class BlockScope : IDisposable
    {
        private readonly RenderBlockContext _context;
        private readonly BlockRecord _block;
        private bool _disposed;

        public BlockScope(RenderBlockContext context, BlockRecord block)
        {
            _context = context;
            _block = block;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _context.Exit(_block);
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        public void Dispose()
        {
        }
    }

    private sealed class BlockRecordComparer : IEqualityComparer<BlockRecord>
    {
        public bool Equals(BlockRecord? x, BlockRecord? y) => ReferenceEquals(x, y);

        public int GetHashCode(BlockRecord obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
