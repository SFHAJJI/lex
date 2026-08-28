using System.Xml;
using System.Xml.Linq;

namespace Lex.Derive;

internal static class StrictPublisherXml
{
    private const long MaximumCharacters = 64L * 1024 * 1024;

    public static XDocument Parse(string source, XmlParserContext? context = null)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumCharacters,
        };
        using var input = new StringReader(source);
        using var reader = XmlReader.Create(input, settings, context);
        return XDocument.Load(reader, LoadOptions.None);
    }
}
