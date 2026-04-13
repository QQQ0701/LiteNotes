using System.Globalization;
using System.Windows.Data;

namespace LiteNotes.Converters;

public class BoolToAttachmentIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (bool)value ? "🖼️" : "📄";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
