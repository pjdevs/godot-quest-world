using System;

namespace System.Runtime.CompilerServices;

/// <summary>
/// Minimal runtime surface required by the C# preview union feature when the
/// host runtime predates the SDK that introduced the union primitives.
/// </summary>
public interface IUnion
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class UnionAttribute : Attribute
{
	public UnionAttribute()
	{
		Cases = Array.Empty<Type>();
	}

	public UnionAttribute(params Type[] cases)
	{
		Cases = cases;
	}

	public Type[] Cases { get; }
}
