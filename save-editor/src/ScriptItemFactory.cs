using System;

namespace SealSaveEditor
{
    public static class ScriptItemFactory
    {
        public static ItemInitialState Create(ScriptItemCatalog catalog, ScriptBookDefinition definition, int seed)
        {
            if (catalog == null) throw new ArgumentNullException("catalog");
            if (definition == null || !Object.ReferenceEquals(catalog.Find(definition.Path), definition))
                throw new ArgumentException("Select a concrete original skill book from this catalog.", "definition");
            return ItemFactory.CreateScript(catalog.GameDirectory, definition, seed);
        }
    }
}
