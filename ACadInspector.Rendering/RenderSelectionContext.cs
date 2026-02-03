using System;
using System.Collections.Generic;
using ACadSharp.Entities;

namespace ACadInspector.Rendering;

/// <summary>
/// Tracks the current entity and logical owner while building render primitives.
/// </summary>
public sealed class RenderSelectionContext
{
    private readonly Stack<Entity?> _entityStack = new();
    private readonly Stack<Entity?> _ownerOverrides = new();

    public Entity? CurrentEntity { get; private set; }

    public Entity? CurrentOwner => _ownerOverrides.Count > 0 ? _ownerOverrides.Peek() : CurrentEntity;

    public IDisposable EnterEntity(Entity entity)
    {
        var previous = CurrentEntity;
        _entityStack.Push(previous);
        CurrentEntity = entity;
        return new EntityScope(this);
    }

    public IDisposable EnterOwnerOverride(Entity owner)
    {
        _ownerOverrides.Push(owner);
        return new OwnerScope(this);
    }

    public RenderPrimitiveMetadata? CreateMetadata()
    {
        if (CurrentEntity is null && CurrentOwner is null)
        {
            return null;
        }

        return new RenderPrimitiveMetadata(CurrentEntity, CurrentOwner);
    }

    private sealed class EntityScope : IDisposable
    {
        private readonly RenderSelectionContext _context;
        private bool _disposed;

        public EntityScope(RenderSelectionContext context)
        {
            _context = context;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _context.CurrentEntity = _context._entityStack.Count > 0 ? _context._entityStack.Pop() : null;
        }
    }

    private sealed class OwnerScope : IDisposable
    {
        private readonly RenderSelectionContext _context;
        private bool _disposed;

        public OwnerScope(RenderSelectionContext context)
        {
            _context = context;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_context._ownerOverrides.Count > 0)
            {
                _context._ownerOverrides.Pop();
            }
        }
    }
}
