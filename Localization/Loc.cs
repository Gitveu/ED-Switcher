using Microsoft.UI.Xaml.Markup;

namespace EDAccountSwitcher.Localization
{
    [MarkupExtensionReturnType(ReturnType = typeof(string))]
    public sealed class Loc : MarkupExtension
    {
        public string Key { get; set; }

        protected override object ProvideValue() => LocalizationManager.Get(Key);
    }
}