using System.Collections.Generic;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// The Core's directives, in the order it asks for them - same registry shape as ItemDatabase /
    /// RecipeDatabase / ResearchDatabase, one asset assigned on GameRuntime.
    ///
    /// Order is the content: the runtime holds an index into this list and nothing else, so a later
    /// chapter is a new entry here rather than a new branch anywhere.
    /// </summary>
    [CreateAssetMenu(fileName = "CoreDirectiveDatabase", menuName = "Game/Core/Directive Database")]
    public sealed class CoreDirectiveDatabase : ScriptableObject
    {
        [SerializeField] CoreDirectiveDefinition[] directives = System.Array.Empty<CoreDirectiveDefinition>();

        public IReadOnlyList<CoreDirectiveDefinition> GetAll() => directives;
    }
}
