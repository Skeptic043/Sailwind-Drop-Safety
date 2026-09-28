namespace DropSafety
{
    /// <summary>Pure drop rule. No Unity calls, so tests can run it directly.</summary>
    public static class DropDecision
    {
        /// <summary>Items outside the enabled inventory filter retain vanilla input behavior.</summary>
        public static bool IsClickDropAllowed(bool disableDrop, bool requireModifier, bool modifierHeld,
            bool inventoryItemsOnly, bool isProtectedInventoryItem)
        {
            return (inventoryItemsOnly && !isProtectedInventoryItem)
                || IsClickDropAllowed(disableDrop, requireModifier, modifierHeld);
        }

        /// <summary>
        /// Whether releasing the PickUp key may drop the held item right now.
        /// RequireModifier wins over DisableDrop. Both off means vanilla.
        /// </summary>
        public static bool IsClickDropAllowed(bool disableDrop, bool requireModifier, bool modifierHeld)
        {
            if (requireModifier)
                return modifierHeld;
            if (disableDrop)
                return false;
            return true;
        }
    }
}
