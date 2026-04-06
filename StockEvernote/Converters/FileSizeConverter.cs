using StockEvernote.Services;
using System.Globalization;
using System.Windows.Data;

namespace StockEvernote.Converters;

public class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes)
        {
            return AzureBlobService.FormatFileSize(bytes);
        }
        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
