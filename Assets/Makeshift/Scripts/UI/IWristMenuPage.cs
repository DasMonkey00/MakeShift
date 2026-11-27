namespace Makeshift.UI
{
    /// <summary>
    /// Put a component with this interface next to a <see cref="WristMenu"/> and the menu shows its buttons
    /// instead of the playground ones.
    /// </summary>
    public interface IWristMenuPage
    {
        void BuildMenu(WristMenu menu);
    }
}
