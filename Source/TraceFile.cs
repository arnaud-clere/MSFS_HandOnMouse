using System.Diagnostics;
using System.IO;

namespace HandOnMouse
{
    public class TraceFile : TextWriterTraceListener
    {
        public TraceFile(string logFileName)
            : base(logFileName)
        {
            Writer = new StreamWriter(logFileName, false);
        }
    }
}
