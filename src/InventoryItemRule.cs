namespace DropSafety
{
    /// <summary>Native inventory item-size rules, independent of purchase and slot state.</summary>
    internal static class InventoryItemRule
    {
        internal static bool IsProtectedInventoryItem(bool hasShipItem, bool big, bool isBottle, float capacity)
        {
            return hasShipItem && !big && !(isBottle && capacity > 10f);
        }
    }
}
