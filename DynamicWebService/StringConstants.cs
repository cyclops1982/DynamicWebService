using System;
using System.Collections.Generic;
using System.Text;

namespace DynamicWebService
{

    /// <summary>
    /// This class is responsible for constants. If a value/string/thing is used more then 1 time, it should be listed here.
    /// </summary>
    public static class Constants
    {
        public static class Config
        {
            public static string WebServiceUrl = "URL";
            public static string WebServiceTimeout = "WebServiceTimeout";
            public static string WebServiceDynamicUrl = "WebServiceDynamicUrl";
            public static string SkipUnsupportedMethods = "Skip unsupported methods";
            public static int DefaultWebServiceTimeout = 30;
        }

        public static class Properties
        {
            public static string DynamicUrl = "_DynamicWebServiceUrl";
        }
    }
}
