using System.Runtime.Serialization;

namespace Flyweight.Core
{
    /// <summary>A preset the user made, such as "Discord", alongside the three built-in levels.</summary>
    [DataContract]
    public sealed class CustomPreset
    {
        /// <summary>Stays the same through renames, so the right-click menu always finds the right one.</summary>
        [DataMember] public string Id { get; set; }

        [DataMember] public string Name { get; set; }

        [DataMember] public LevelSettings Settings { get; set; }
    }
}
