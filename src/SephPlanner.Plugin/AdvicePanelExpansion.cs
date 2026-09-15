namespace SephPlanner.Plugin
{
    internal sealed class AdvicePanelExpansion
    {
        private int _previous;

        public bool? Update(bool hasOffers, bool mixerOpen, bool enchantOpen)
        {
            var context = (hasOffers ? 1 : 0) | (mixerOpen ? 2 : 0) | (enchantOpen ? 4 : 0);
            if (context == _previous) return null;
            _previous = context;
            return context != 0;
        }
    }
}
