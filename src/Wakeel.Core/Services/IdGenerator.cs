namespace Wakeel.Core.Services;

/// <summary>Generates row ids. Every id in الوكيل is a UUID v7 (DATA-MODEL.md §0).</summary>
public interface IIdGenerator
{
    Guid NewId();
}

/// <summary>Default <see cref="IIdGenerator"/>, using <see cref="Guid.CreateVersion7()"/>.</summary>
public sealed class IdGenerator : IIdGenerator
{
    public Guid NewId() => Guid.CreateVersion7();
}
