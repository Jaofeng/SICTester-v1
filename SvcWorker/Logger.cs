using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace STE.TGL.SIPanel
{
    internal interface ILog
    {
        void Debug(string text);
        void Debug(string format, object arg0);
        void Debug(string format, object arg0, object arg1);
        void Debug(string format, object arg0, object arg1, object arg2);
        void Debug(string format, params object[] args);
        void Error(Exception exp);
    }

    /// <summary>
    /// TODO: Logging with ETW... 
    /// </summary>
    internal class Logging : ILog
    {
        public static ILog GetLogger(Type type)
        {
            return new Logging(type.Name);
        }

        public Logging(string name)
        {
            _EvtSource = new EventSource(name, EventSourceSettings.Default);
        }

        EventSource _EvtSource;

        public void Debug(string text)
        {
            System.Diagnostics.Debug.WriteLine(text);
            _EvtSource.Write(text, new EventSourceOptions() { Level = EventLevel.Warning, Opcode = EventOpcode.Info });
        }

        public void Debug(string format, object arg0)
        {
            Debug(string.Format(format, arg0));
        }

        public void Debug(string format, object arg0, object arg1)
        {
            Debug(string.Format(format, arg0, arg1));
        }
        public void Debug(string format, object arg0, object arg1, object arg2)
        {
            Debug(string.Format(format, arg0, arg1, arg2));
        }

        public void Debug(string format, params object[] args)
        {
            Debug(string.Format(format, args));
        }

        public void Error(Exception exp)
        {
            System.Diagnostics.Debug.WriteLine(exp);
        }

    }
}