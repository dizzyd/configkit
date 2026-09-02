// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// Released under the MIT License. See LICENSE at the repository root.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using Vintagestory.API.Common;
using Vintagestory.Common;

namespace ConfigKit
{
    internal class SettingsOrigin : IAssetOrigin
    {
        public string OriginPath { get; protected set; }

        private readonly byte[] _data;
        private readonly AssetLocation _location;

        public SettingsOrigin(byte[] data, AssetLocation location)
        {
            _data = data;
            _location = location;
            OriginPath = _location.Path;
        }

        public void LoadAsset(IAsset asset)
        {

        }

        public bool TryLoadAsset(IAsset asset)
        {
            return true;
        }

        public List<IAsset> GetAssets(AssetCategory category, bool shouldLoad = true)
        {
            List<IAsset> list = new()
            {
                new Asset(_data, _location, this)
            };

            return list;
        }

        public List<IAsset> GetAssets(AssetLocation baseLocation, bool shouldLoad = true)
        {
            List<IAsset> list = new()
            {
                new Asset(_data, _location, this)
            };

            return list;
        }

        public virtual bool IsAllowedToAffectGameplay()
        {
            return true;
        }
    }
}