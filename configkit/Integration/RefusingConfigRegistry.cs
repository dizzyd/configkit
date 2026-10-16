// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.

using Vintagestory.API.Common;

namespace ConfigKit;

/// <summary>
/// Stands in for <see cref="ConfigRegistry"/> on a client that is standing down, registered
/// under the same code by <c>ConfigKitModSystem.Start</c>. A server running ConfigKit sends its
/// configs to every client, and without a registry of that code to take them the game's own
/// asset loading throws. Nothing here applies them; they are only counted and handed on.
/// </summary>
internal sealed class RefusingConfigRegistry : RecipeRegistryBase
{
    /// <summary>
    /// Given the number of configs the server sent. A property rather than a constructor
    /// argument because the game instantiates registries itself, by their parameterless
    /// constructor.
    /// </summary>
    public Action<int>? Received { get; set; }

    public override void FromBytes(IWorldAccessor resolver, int quantity, byte[] data) => Received?.Invoke(quantity);

    // A client never sends its registries, and a standing-down server registers none.
    public override void ToBytes(IWorldAccessor resolver, out byte[] data, out int quantity)
    {
        data = [];
        quantity = 0;
    }
}
