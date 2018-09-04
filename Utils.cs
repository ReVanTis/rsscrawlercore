using System.IO;
using System.Text;

namespace rsscrawlercore
{
    public class StringWriterWithEncoding : StringWriter
    {
        private readonly Encoding _encoding;

        public StringWriterWithEncoding(Encoding encoding)
        {
            this._encoding = encoding;
        }

        public override Encoding Encoding
        {
            get { return _encoding; }
        }
    }
    public class Utils
    {
        
    }
}