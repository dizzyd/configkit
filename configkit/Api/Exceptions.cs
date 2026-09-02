// ConfigKit - mod configuration for Vintage Story
// Copyright (C) 2026 Dave (Dizzy) Smith
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU Lesser General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option) any
// later version. See COPYING.LESSER, or <https://www.gnu.org/licenses/>.
//
// Derived from ConfigLib by Maltiez (https://github.com/maltiez2/vsmod_configlib),
// released under CC0 1.0 Universal. Adapted to drop the Dear ImGui dependency.

using System;

namespace ConfigKit;

public class ConfigKitException : Exception
{
    public ConfigKitException() { }
    public ConfigKitException(string message) : base(message) { }
    public ConfigKitException(string message, Exception exception) : base(message, exception) { }
}
public class InvalidTokenException : ConfigKitException
{
    public InvalidTokenException() { }
    public InvalidTokenException(string message) : base(message) { }
    public InvalidTokenException(string message, Exception exception) : base(message, exception) { }
}
public class InvalidConfigException : ConfigKitException
{
    public InvalidConfigException() { }
    public InvalidConfigException(string message) : base(message) { }
    public InvalidConfigException(string message, Exception exception) : base(message, exception) { }
}
