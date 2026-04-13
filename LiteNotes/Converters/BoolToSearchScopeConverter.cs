using System.Globalization;
using System.Windows.Data;

namespace LiteNotes.Converters;

public class BoolToSearchScopeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? "全域" : "本筆記本";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
