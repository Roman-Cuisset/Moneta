using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;
using Moneta.Localization;

namespace Moneta.App;

public sealed class LocalizedTextExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{key}]") { Source = L.Current, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}

public sealed class LocalizedLanguageExtension : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(L.CultureName))
        {
            Source = L.Current,
            Mode = BindingMode.OneWay,
            ConverterCulture = CultureInfo.InvariantCulture
        }.ProvideValue(serviceProvider);
}
